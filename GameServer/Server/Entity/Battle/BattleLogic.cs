using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Command;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;
using GameShared.SkillGraph;

namespace Fantasy;

public enum HashReportResult
{
    Matched,
    Mismatch,
    NoRecord
}

public sealed class BattleLogic : IBuffCommandSink
{
    private readonly Dictionary<long, PlayerState> _statesByPlayerId = new();
    private readonly FrameSyncPhysicsWorld _physicsWorld = new();
    private readonly Dictionary<long, Dictionary<uint, PendingInput>> _pendingInputsByPlayerId = new();
    private readonly Dictionary<long, ReusablePlayerInput> _reusableInputsByPlayerId = new();
    private readonly Dictionary<long, SubmittedInput> _lastSubmittedInputByPlayerId = new();
    private readonly Dictionary<long, uint> _latestAcceptedInputFrameByPlayerId = new();
    private readonly List<long> _playerIdBuffer = new();
    private readonly List<ApplyBuffCommand> _pendingApplyBuffCommands = new();
    private readonly List<RemoveBuffCommand> _pendingRemoveBuffCommands = new();
    private readonly CommandPool<ApplyBuffCommand> _applyBuffCommandPool = new();
    private readonly CommandPool<RemoveBuffCommand> _removeBuffCommandPool = new();
    private readonly IBuffConfigProvider _buffConfigProvider;
    private readonly BattleSkillGraphRuntime _skillGraphRuntime;
    private readonly Action<string>? _logDebug;
    private readonly Action<string>? _logWarning;
    private readonly SnapshotBuffer<ulong> _authoritativeHashHistory = new(64);
    private readonly HashSet<ContactPair> _currentContactPairs = new();
    private readonly HashSet<DashContactEpochKey> _triggeredDashContactEpochs = new();
    private readonly List<DashContactEpochKey> _staleDashContactEpochs = new();
    private bool _hasProcessedFrame;

    public BattleLogic(
        Action<string>? logDebug = null,
        Action<string>? logWarning = null,
        IReadOnlyDictionary<int, RuntimeSkillGraph>? skillGraphs = null)
    {
        _logDebug = logDebug;
        _logWarning = logWarning;
        _buffConfigProvider = new DefaultBuffConfigProvider();
        _skillGraphRuntime = new BattleSkillGraphRuntime(
            this,
            skillGraphs ?? BattleSkillGraphLibrary.LoadDefaultGraphs(),
            new DelegateSkillRuntimeServices(TryConsumeStamina, message => _logDebug?.Invoke(message)));
    }

    public uint LastFrameIndex { get; private set; }
    public Action<TestSnapshot>? OnBroadcast { get; set; }
    public int AcceptedInputCount { get; private set; }
    public int LateInputDropCount { get; private set; }
    public int FutureInputRejectCount { get; private set; }
    public int ReusedInputCount { get; private set; }
    public int ZeroInputFallbackCount { get; private set; }
    public int HashReportsMatched { get; private set; }
    public int HashMismatchCount { get; private set; }
    public int HashNoRecordCount { get; private set; }
    public int ActiveSkillExecutionCount => _skillGraphRuntime.ActiveExecutionCount;
    public int KnockbackTriggerCount { get; private set; }

    public PlayerState JoinPlayer(long playerId, Fixed64 x, Fixed64 y)
    {
        if (_statesByPlayerId.TryGetValue(playerId, out PlayerState? existingState))
        {
            EnsureReusableInput(playerId);
            return existingState!;
        }

        PlayerState newState = new PlayerState(playerId, x, y);
        _statesByPlayerId.Add(playerId, newState);
        _physicsWorld.EnsureBody(checked((int)playerId), x, y);
        EnsureReusableInput(playerId);
        return newState;
    }

    public bool RemovePlayer(long playerId)
    {
        _pendingInputsByPlayerId.Remove(playerId);
        _reusableInputsByPlayerId.Remove(playerId);
        _lastSubmittedInputByPlayerId.Remove(playerId);
        _latestAcceptedInputFrameByPlayerId.Remove(playerId);
        RemovePendingBuffCommands(playerId);
        _skillGraphRuntime.RemovePlayer(playerId);
        _physicsWorld.RemoveBody(checked((int)playerId));
        return _statesByPlayerId.Remove(playerId);
    }

    public bool HasPlayer(long playerId)
    {
        return _statesByPlayerId.ContainsKey(playerId);
    }

    public bool TryGetPlayer(long playerId, out PlayerState state)
    {
        bool found = _statesByPlayerId.TryGetValue(playerId, out PlayerState? resolvedState);
        state = resolvedState!;
        return found;
    }

    public uint GetLatestAcceptedInputFrame(long playerId)
    {
        return _latestAcceptedInputFrameByPlayerId.TryGetValue(playerId, out uint frameIndex)
            ? frameIndex
            : 0u;
    }

    public bool IsInputSuppressed(long playerId)
    {
        return _reusableInputsByPlayerId.TryGetValue(playerId, out ReusablePlayerInput? input) &&
               input.IsSuppressed;
    }

    public bool SetInputSuppressed(long playerId, bool suppressed)
    {
        if (!_statesByPlayerId.ContainsKey(playerId))
        {
            return false;
        }

        ReusablePlayerInput reusableInput = EnsureReusableInput(playerId);
        if (!reusableInput.SetSuppressed(suppressed))
        {
            return false;
        }

        if (suppressed)
        {
            // Buffered and last-submitted inputs belong to the disconnected session.
            _pendingInputsByPlayerId.Remove(playerId);
            _lastSubmittedInputByPlayerId.Remove(playerId);
        }

        return true;
    }

    public void SubmitInput(long playerId, uint frameIndex, uint inputSeq, long dxRaw, long dyRaw, int skillId = 0)
    {
        if (!_statesByPlayerId.ContainsKey(playerId))
        {
            return;
        }

        if (IsInputSuppressed(playerId))
        {
            return;
        }

        Fixed64 fixedDx = Fixed64.FromRaw(dxRaw);
        Fixed64 fixedDy = Fixed64.FromRaw(dyRaw);
        bool isInputEdge = IsSubmittedInputEdge(playerId, fixedDx, fixedDy);
        // Allow frame 0 input before the first authoritative tick starts.
        if (_hasProcessedFrame && frameIndex <= LastFrameIndex)
        {
            LateInputDropCount++;
            if (isInputEdge)
            {
                _logWarning?.Invoke(
                    $"[Battle][LateInputDrop] player={playerId} frame={frameIndex} current={LastFrameIndex} input={FormatInput(fixedDx, fixedDy)} seq={inputSeq}");
            }

            return;
        }

        uint maxAcceptedFrame = unchecked(LastFrameIndex + (uint)InputBufferTuning.MaxFutureInputFrames);
        if (IsFutureFrameRejected(frameIndex, maxAcceptedFrame))
        {
            FutureInputRejectCount++;
            _logWarning?.Invoke(
                $"[Battle][FutureInput] Reject player={playerId} frame={frameIndex} current={LastFrameIndex} maxFuture={maxAcceptedFrame}");
            return;
        }

        if (isInputEdge)
        {
            _logDebug?.Invoke(
                $"[Battle][SubmitInputEdge] player={playerId} frame={frameIndex} current={LastFrameIndex} input={FormatInput(fixedDx, fixedDy)} seq={inputSeq}");
        }

        if (!_pendingInputsByPlayerId.TryGetValue(playerId, out Dictionary<uint, PendingInput>? playerInputs))
        {
            playerInputs = new Dictionary<uint, PendingInput>();
            _pendingInputsByPlayerId[playerId] = playerInputs;
        }

        if (playerInputs.TryGetValue(frameIndex, out PendingInput pendingInput) &&
            !IsInputNewerOrEqual(inputSeq, pendingInput.InputSeq))
        {
            return;
        }

        playerInputs[frameIndex] = new PendingInput(frameIndex, inputSeq, fixedDx, fixedDy, skillId);
        _lastSubmittedInputByPlayerId[playerId] = new SubmittedInput(frameIndex, inputSeq, fixedDx, fixedDy);
        if (!_latestAcceptedInputFrameByPlayerId.TryGetValue(playerId, out uint latestAcceptedFrame) ||
            frameIndex >= latestAcceptedFrame)
        {
            _latestAcceptedInputFrameByPlayerId[playerId] = frameIndex;
        }
        AcceptedInputCount++;
    }

    // Test harnesses can still describe inputs as floats; the network path above is raw-only.
    public void SubmitInput(long playerId, uint frameIndex, uint inputSeq, float dx, float dy, int skillId = 0)
    {
        SubmitInput(
            playerId,
            frameIndex,
            inputSeq,
            ((Fixed64)dx).m_rawValue,
            ((Fixed64)dy).m_rawValue,
            skillId);
    }

    public void Tick(uint frameIndex, Fixed64 fixedDt)
    {
        DeterminismRules.AssertFixedDt(fixedDt);
        LastFrameIndex = frameIndex;
        _hasProcessedFrame = true;

        BuildSortedPlayerBuffer();
        QueueSkillRequests(frameIndex);
        _skillGraphRuntime.Step(frameIndex);
        ProcessBuffCommands(frameIndex);

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState? state))
            {
                continue;
            }

            Fixed64 dx = Fixed64.Zero;
            Fixed64 dy = Fixed64.Zero;
            bool consumedCurrentFrameInput = false;
            ReusablePlayerInput reusableInput = EnsureReusableInput(playerId);
            bool hadLastConsumedInput = reusableInput.TryGet(
                out Fixed64 lastConsumedDx,
                out Fixed64 lastConsumedDy);
            if (reusableInput.IsSuppressed)
            {
                ZeroInputFallbackCount++;
            }
            else if (_pendingInputsByPlayerId.TryGetValue(playerId, out Dictionary<uint, PendingInput>? playerInputs) &&
                playerInputs.TryGetValue(frameIndex, out PendingInput input))
            {
                dx = input.Dx;
                dy = input.Dy;

                if (!hadLastConsumedInput || !AreInputsEqual(dx, dy, lastConsumedDx, lastConsumedDy))
                {
                    string previousInput = hadLastConsumedInput
                        ? FormatInput(lastConsumedDx, lastConsumedDy)
                        : "(none)";
                    _logDebug?.Invoke(
                        $"[Battle][ConsumeInputEdge] frame={frameIndex} player={playerId} input={previousInput}->{FormatInput(dx, dy)}");
                }

                reusableInput.Record(dx, dy);
                consumedCurrentFrameInput = true;
                playerInputs.Remove(frameIndex);
                if (playerInputs.Count == 0)
                {
                    _pendingInputsByPlayerId.Remove(playerId);
                }
            }
            else if (hadLastConsumedInput)
            {
                dx = lastConsumedDx;
                dy = lastConsumedDy;
                ReusedInputCount++;
            }
            else
            {
                ZeroInputFallbackCount++;
            }

            _physicsWorld.EnsureBody(checked((int)state!.PlayerId), state.X, state.Y);
            _physicsWorld.SetBodyMovementInput(checked((int)state.PlayerId), dx, dy);
            if (!consumedCurrentFrameInput && _logDebug != null)
            {
                _logDebug($"[Battle][ReuseInput] Frame={frameIndex}, Player={playerId}, Input={FormatInput(dx, dy)}");
            }
        }

        ApplyDisplacements();
        _physicsWorld.Step(fixedDt);
        SyncPlayerStatesFromPhysics();
        ApplyBuffTicks(frameIndex);
        DetectContactsAndTriggerKnockback(frameIndex);
        TickStamina();
        RecalculateNumericStates();
        SyncSkillExecutionsToStates();

        TestSnapshot snapshot = BuildTestSnapshot(frameIndex);
        _authoritativeHashHistory.Save(frameIndex, StateHasher.Hash(snapshot.ToBattleWorldSnapshot()));
        OnBroadcast?.Invoke(snapshot);
    }

    public ulong GetStateHash()
    {
        return StateHasher.Hash(BuildBattleWorldSnapshot(LastFrameIndex));
    }

    public HashReportResult TryCompareReportedHash(long playerId, uint frameIndex, ulong reportedHash)
    {
        if (!_authoritativeHashHistory.TryGet(frameIndex, out ulong authoritativeHash))
        {
            HashNoRecordCount++;
            _logWarning?.Invoke(
                $"[Battle][HashNoRecord] player={playerId} frame={frameIndex} " +
                $"reported=0x{reportedHash:X16} current={LastFrameIndex}");
            return HashReportResult.NoRecord;
        }

        if (authoritativeHash == reportedHash)
        {
            HashReportsMatched++;
            return HashReportResult.Matched;
        }

        HashMismatchCount++;
        _logWarning?.Invoke(
            $"[Battle][HashMismatch] player={playerId} frame={frameIndex} " +
            $"authoritative=0x{authoritativeHash:X16} reported=0x{reportedHash:X16}");
        return HashReportResult.Mismatch;
    }

    private TestSnapshot BuildTestSnapshot(uint frameIndex)
    {
        return TestSnapshot.FromBattleWorldSnapshot(BuildBattleWorldSnapshot(frameIndex));
    }

    private BattleWorldSnapshot BuildBattleWorldSnapshot(uint frameIndex)
    {
        PlayerStateSnapshot[] players = BuildPlayerSnapshots();
        return new BattleWorldSnapshot(frameIndex, players, _physicsWorld.TakeSnapshot());
    }

    private PlayerStateSnapshot[] BuildPlayerSnapshots()
    {
        BuildSortedPlayerBuffer();
        PlayerStateSnapshot[] players = new PlayerStateSnapshot[_playerIdBuffer.Count];

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            PlayerState state = _statesByPlayerId[playerId];
            players[i] = new PlayerStateSnapshot(
                state.PlayerId,
                state.X,
                state.Y,
                state.CaptureAttributeSnapshot(),
                state.ActiveBuffs,
                state.NextRuntimeBuffId,
                state.Numeric.CaptureSnapshot(),
                state.StaminaRegenCounterFrames,
                state.DashVelocityX,
                state.DashVelocityY,
                state.DashRemainingFrames,
                state.DashRuntimeBuffId,
                state.KnockbackVelocityX,
                state.KnockbackVelocityY,
                state.KnockbackRemainingFrames,
                state.KnockbackRuntimeBuffId,
                state.SkillExecutions);
        }

        return players;
    }

    public void EnqueueApplyBuff(ApplyBuffCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        ApplyBuffCommand queuedCommand = _applyBuffCommandPool.Rent();
        queuedCommand.CasterId = command.CasterId;
        queuedCommand.TargetId = command.TargetId;
        queuedCommand.BuffId = command.BuffId;
        queuedCommand.DurationFrames = command.DurationFrames;
        queuedCommand.StackCount = command.StackCount;
        queuedCommand.FrameIndex = command.FrameIndex;
        queuedCommand.Flags = command.Flags;
        queuedCommand.HasDisplacementVelocityOverride = command.HasDisplacementVelocityOverride;
        queuedCommand.DisplacementVelocityX = command.DisplacementVelocityX;
        queuedCommand.DisplacementVelocityY = command.DisplacementVelocityY;
        InsertApplyCommand(queuedCommand);
    }

    public void EnqueueRemoveBuff(RemoveBuffCommand command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        RemoveBuffCommand queuedCommand = _removeBuffCommandPool.Rent();
        queuedCommand.TargetId = command.TargetId;
        queuedCommand.RuntimeBuffId = command.RuntimeBuffId;
        queuedCommand.BuffId = command.BuffId;
        queuedCommand.RemoveReason = command.RemoveReason;
        queuedCommand.FrameIndex = command.FrameIndex;
        InsertRemoveCommand(queuedCommand);
    }

    public bool HasBuff(long targetId, int buffId)
    {
        return _statesByPlayerId.TryGetValue(targetId, out PlayerState? state) &&
               BuffSystem.HasBuff(state, buffId);
    }

    public int GetBuffStackCount(long targetId, int buffId)
    {
        return _statesByPlayerId.TryGetValue(targetId, out PlayerState? state)
            ? BuffSystem.GetBuffStackCount(state, buffId)
            : 0;
    }

    private void BuildSortedPlayerBuffer()
    {
        _playerIdBuffer.Clear();

        foreach (long playerId in _statesByPlayerId.Keys)
        {
            _playerIdBuffer.Add(playerId);
        }

        _playerIdBuffer.Sort();
    }

    private ReusablePlayerInput EnsureReusableInput(long playerId)
    {
        if (_reusableInputsByPlayerId.TryGetValue(playerId, out ReusablePlayerInput? existing))
        {
            return existing!;
        }

        ReusablePlayerInput created = new ReusablePlayerInput();
        _reusableInputsByPlayerId[playerId] = created;
        return created;
    }

    private void SyncPlayerStatesFromPhysics()
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState state))
            {
                continue;
            }

            if (!_physicsWorld.TryGetBodySnapshot(checked((int)playerId), out PhysicsBodySnapshot bodySnapshot))
            {
                continue;
            }

            state.X = bodySnapshot.PositionX;
            state.Y = bodySnapshot.PositionY;
        }
    }

    private void QueueSkillRequests(uint frameIndex)
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (IsInputSuppressed(playerId) ||
                !_pendingInputsByPlayerId.TryGetValue(playerId, out Dictionary<uint, PendingInput>? playerInputs) ||
                !playerInputs.TryGetValue(frameIndex, out PendingInput input) ||
                input.SkillId <= 0)
            {
                continue;
            }

            _skillGraphRuntime.QueueSkillRequest(
                playerId,
                playerId,
                input.SkillId,
                frameIndex,
                input.Dx,
                input.Dy);
        }
    }

    private void ApplyDisplacements()
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            if (_statesByPlayerId.TryGetValue(_playerIdBuffer[i], out PlayerState? state))
            {
                DisplacementSystem.Apply(state, _physicsWorld);
            }
        }

        // Decay is intentionally adjacent to Apply. Buffs created by the
        // post-physics contact pass retain their full initial velocity until
        // the next frame.
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            if (_statesByPlayerId.TryGetValue(_playerIdBuffer[i], out PlayerState? state))
            {
                DisplacementSystem.Decay(state);
            }
        }
    }

    private void TickStamina()
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            if (_statesByPlayerId.TryGetValue(_playerIdBuffer[i], out PlayerState? state))
            {
                StaminaSystem.Tick(state);
            }
        }
    }

    private bool TryConsumeStamina(long playerId, int amount)
    {
        return _statesByPlayerId.TryGetValue(playerId, out PlayerState? state) &&
               StaminaSystem.TryConsume(state, amount);
    }

    private void SyncSkillExecutionsToStates()
    {
        Dictionary<long, GameShared.SkillGraph.ActiveSkillExecutionSnapshot> executions =
            _skillGraphRuntime.CaptureExecutions();
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState? state))
            {
                continue;
            }

            state.SkillExecutions.Clear();
            if (executions.TryGetValue(
                    playerId,
                    out GameShared.SkillGraph.ActiveSkillExecutionSnapshot? execution))
            {
                state.SkillExecutions[playerId] = execution;
            }
        }
    }

    private void DetectContactsAndTriggerKnockback(uint frameIndex)
    {
        PhysicsWorldSnapshot physicsSnapshot = _physicsWorld.TakeSnapshot();
        _currentContactPairs.Clear();
        for (int i = 0; i < physicsSnapshot.Contacts.Count; i++)
        {
            PhysicsContactSnapshot contact = physicsSnapshot.Contacts[i];
            if (contact.IsTouching)
            {
                _currentContactPairs.Add(new ContactPair(contact.BodyAId, contact.BodyBId));
            }
        }

        _staleDashContactEpochs.Clear();
        foreach (DashContactEpochKey key in _triggeredDashContactEpochs)
        {
            if (!_currentContactPairs.Contains(key.Pair))
            {
                _staleDashContactEpochs.Add(key);
            }
        }

        for (int i = 0; i < _staleDashContactEpochs.Count; i++)
        {
            _triggeredDashContactEpochs.Remove(_staleDashContactEpochs[i]);
        }

        _staleDashContactEpochs.Clear();
        for (int i = 0; i < physicsSnapshot.Contacts.Count; i++)
        {
            PhysicsContactSnapshot contact = physicsSnapshot.Contacts[i];
            if (!contact.IsTouching ||
                !_statesByPlayerId.TryGetValue(contact.BodyAId, out PlayerState? stateA) ||
                !_statesByPlayerId.TryGetValue(contact.BodyBId, out PlayerState? stateB))
            {
                continue;
            }

            long dashEpochA = GetActiveDashEpoch(stateA);
            long dashEpochB = GetActiveDashEpoch(stateB);
            if (dashEpochA <= 0 && dashEpochB <= 0)
            {
                continue;
            }

            ContactPair pair = new ContactPair(contact.BodyAId, contact.BodyBId);
            DashContactEpochKey keyA = new DashContactEpochKey(pair, stateA.PlayerId, dashEpochA);
            DashContactEpochKey keyB = new DashContactEpochKey(pair, stateB.PlayerId, dashEpochB);
            bool shouldTrigger =
                (dashEpochA > 0 && !_triggeredDashContactEpochs.Contains(keyA)) ||
                (dashEpochB > 0 && !_triggeredDashContactEpochs.Contains(keyB));
            if (!shouldTrigger)
            {
                continue;
            }

            if (dashEpochA > 0)
            {
                _triggeredDashContactEpochs.Add(keyA);
            }

            if (dashEpochB > 0)
            {
                _triggeredDashContactEpochs.Add(keyB);
            }

            TriggerKnockback(stateA, stateB, dashEpochA > 0, dashEpochB > 0, frameIndex);
            KnockbackTriggerCount++;
        }
    }

    private static long GetActiveDashEpoch(PlayerState state)
    {
        return state.DashRuntimeBuffId > 0 &&
               state.DashRemainingFrames > 0 &&
               BuffSystem.HasBuff(state, DashTuning.DashBuffId)
            ? state.DashRuntimeBuffId
            : 0L;
    }

    private void TriggerKnockback(
        PlayerState stateA,
        PlayerState stateB,
        bool stateADashing,
        bool stateBDashing,
        uint frameIndex)
    {
        Fixed64 deltaX = stateB.X - stateA.X;
        Fixed64 deltaY = stateB.Y - stateA.Y;
        SeparationNormalResolver.ResolveWithoutVelocity(
            checked((int)stateA.PlayerId),
            checked((int)stateB.PlayerId),
            deltaX,
            deltaY,
            out Fixed64 normalX,
            out Fixed64 normalY);

        if (stateADashing && stateBDashing)
        {
            ApplyKnockback(stateA, stateB.PlayerId, -normalX, -normalY, frameIndex);
            ApplyKnockback(stateB, stateA.PlayerId, normalX, normalY, frameIndex);
            StopDashAndEnterRecover(stateA, frameIndex);
            StopDashAndEnterRecover(stateB, frameIndex);
            return;
        }

        if (stateADashing)
        {
            ApplyKnockback(stateB, stateA.PlayerId, normalX, normalY, frameIndex);
            StopDashAndEnterRecover(stateA, frameIndex);
            return;
        }

        ApplyKnockback(stateA, stateB.PlayerId, -normalX, -normalY, frameIndex);
        StopDashAndEnterRecover(stateB, frameIndex);
    }

    private void ApplyKnockback(
        PlayerState target,
        long casterId,
        Fixed64 directionX,
        Fixed64 directionY,
        uint frameIndex)
    {
        BuffSystem.AddBuff(
            target,
            new ApplyBuffCommand
            {
                CasterId = casterId,
                TargetId = target.PlayerId,
                BuffId = KnockbackTuning.KnockbackBuffId,
                DurationFrames = KnockbackTuning.KnockbackFrames,
                StackCount = 1,
                FrameIndex = frameIndex,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable,
                HasDisplacementVelocityOverride = true,
                DisplacementVelocityX = directionX * KnockbackTuning.KnockbackSpeedFixed,
                DisplacementVelocityY = directionY * KnockbackTuning.KnockbackSpeedFixed
            },
            _buffConfigProvider);
    }

    private void StopDashAndEnterRecover(PlayerState state, uint frameIndex)
    {
        BuffSystem.RemoveBuff(state, 0, DashTuning.DashBuffId);
        _skillGraphRuntime.CancelExecution(state.PlayerId, DashTuning.DashSkillId);
        BuffSystem.AddBuff(
            state,
            new ApplyBuffCommand
            {
                CasterId = state.PlayerId,
                TargetId = state.PlayerId,
                BuffId = DashTuning.RecoverBuffId,
                DurationFrames = DashTuning.RecoverFrames,
                StackCount = 1,
                FrameIndex = frameIndex,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            },
            _buffConfigProvider);
    }

    private void ProcessBuffCommands(uint frameIndex)
    {
        while (_pendingApplyBuffCommands.Count > 0 && _pendingApplyBuffCommands[0].FrameIndex <= frameIndex)
        {
            ApplyBuffCommand command = _pendingApplyBuffCommands[0];
            _pendingApplyBuffCommands.RemoveAt(0);
            if (_statesByPlayerId.TryGetValue(command.TargetId, out PlayerState? targetState))
            {
                BuffSystem.AddBuff(targetState, command, _buffConfigProvider);
            }

            _applyBuffCommandPool.Return(command);
        }

        while (_pendingRemoveBuffCommands.Count > 0 && _pendingRemoveBuffCommands[0].FrameIndex <= frameIndex)
        {
            RemoveBuffCommand command = _pendingRemoveBuffCommands[0];
            _pendingRemoveBuffCommands.RemoveAt(0);
            if (_statesByPlayerId.TryGetValue(command.TargetId, out PlayerState? targetState))
            {
                BuffSystem.RemoveBuff(targetState, command);
            }

            _removeBuffCommandPool.Return(command);
        }
    }

    private void RecalculateNumericStates()
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (_statesByPlayerId.TryGetValue(playerId, out PlayerState? playerState))
            {
                playerState.Numeric.Recalculate(playerState);
            }
        }
    }

    private void ApplyBuffTicks(uint frameIndex)
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (_statesByPlayerId.TryGetValue(playerId, out PlayerState? playerState))
            {
                BuffSystem.ApplyTick(playerState, frameIndex);
            }
        }
    }

    private void RemovePendingBuffCommands(long targetId)
    {
        for (int i = _pendingApplyBuffCommands.Count - 1; i >= 0; i--)
        {
            ApplyBuffCommand command = _pendingApplyBuffCommands[i];
            if (command.TargetId != targetId && command.CasterId != targetId)
            {
                continue;
            }

            _pendingApplyBuffCommands.RemoveAt(i);
            _applyBuffCommandPool.Return(command);
        }

        for (int i = _pendingRemoveBuffCommands.Count - 1; i >= 0; i--)
        {
            RemoveBuffCommand command = _pendingRemoveBuffCommands[i];
            if (command.TargetId != targetId)
            {
                continue;
            }

            _pendingRemoveBuffCommands.RemoveAt(i);
            _removeBuffCommandPool.Return(command);
        }
    }

    private void InsertApplyCommand(ApplyBuffCommand command)
    {
        int insertIndex = _pendingApplyBuffCommands.Count;
        for (int i = 0; i < _pendingApplyBuffCommands.Count; i++)
        {
            if (Compare(_pendingApplyBuffCommands[i], command) > 0)
            {
                insertIndex = i;
                break;
            }
        }

        _pendingApplyBuffCommands.Insert(insertIndex, command);
    }

    private void InsertRemoveCommand(RemoveBuffCommand command)
    {
        int insertIndex = _pendingRemoveBuffCommands.Count;
        for (int i = 0; i < _pendingRemoveBuffCommands.Count; i++)
        {
            if (Compare(_pendingRemoveBuffCommands[i], command) > 0)
            {
                insertIndex = i;
                break;
            }
        }

        _pendingRemoveBuffCommands.Insert(insertIndex, command);
    }

    private static int Compare(ApplyBuffCommand left, ApplyBuffCommand right)
    {
        int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
        if (byFrame != 0)
        {
            return byFrame;
        }

        int byTarget = left.TargetId.CompareTo(right.TargetId);
        if (byTarget != 0)
        {
            return byTarget;
        }

        return left.BuffId.CompareTo(right.BuffId);
    }

    private static int Compare(RemoveBuffCommand left, RemoveBuffCommand right)
    {
        int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
        if (byFrame != 0)
        {
            return byFrame;
        }

        int byTarget = left.TargetId.CompareTo(right.TargetId);
        if (byTarget != 0)
        {
            return byTarget;
        }

        return left.RuntimeBuffId.CompareTo(right.RuntimeBuffId);
    }

    private static bool IsInputNewerOrEqual(uint incomingSeq, uint cachedSeq)
    {
        return incomingSeq == cachedSeq || unchecked(incomingSeq - cachedSeq) < 0x80000000;
    }

    private static bool IsFutureFrameRejected(uint inputFrameIndex, uint maxAcceptedFrame)
    {
        return unchecked(inputFrameIndex - maxAcceptedFrame) < 0x80000000 && inputFrameIndex > maxAcceptedFrame;
    }

    private bool IsSubmittedInputEdge(long playerId, Fixed64 dx, Fixed64 dy)
    {
        if (!_lastSubmittedInputByPlayerId.TryGetValue(playerId, out SubmittedInput lastSubmittedInput))
        {
            return true;
        }

        return !AreInputsEqual(dx, dy, lastSubmittedInput.Dx, lastSubmittedInput.Dy);
    }

    private static bool AreInputsEqual(Fixed64 leftDx, Fixed64 leftDy, Fixed64 rightDx, Fixed64 rightDy)
    {
        return leftDx.m_rawValue == rightDx.m_rawValue && leftDy.m_rawValue == rightDy.m_rawValue;
    }

    private static string FormatInput(Fixed64 dx, Fixed64 dy)
    {
        return $"({(float)dx:F3},{(float)dy:F3})";
    }

    private readonly struct ContactPair : IEquatable<ContactPair>
    {
        public ContactPair(int bodyAId, int bodyBId)
        {
            if (bodyAId <= bodyBId)
            {
                BodyAId = bodyAId;
                BodyBId = bodyBId;
            }
            else
            {
                BodyAId = bodyBId;
                BodyBId = bodyAId;
            }
        }

        public int BodyAId { get; }
        public int BodyBId { get; }

        public bool Equals(ContactPair other)
        {
            return BodyAId == other.BodyAId && BodyBId == other.BodyBId;
        }

        public override bool Equals(object? obj)
        {
            return obj is ContactPair other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((BodyAId * 397) ^ BodyBId);
        }
    }

    private readonly struct DashContactEpochKey : IEquatable<DashContactEpochKey>
    {
        public DashContactEpochKey(ContactPair pair, long dashPlayerId, long dashRuntimeBuffId)
        {
            Pair = pair;
            DashPlayerId = dashPlayerId;
            DashRuntimeBuffId = dashRuntimeBuffId;
        }

        public ContactPair Pair { get; }
        public long DashPlayerId { get; }
        public long DashRuntimeBuffId { get; }

        public bool Equals(DashContactEpochKey other)
        {
            return Pair.Equals(other.Pair) &&
                   DashPlayerId == other.DashPlayerId &&
                   DashRuntimeBuffId == other.DashRuntimeBuffId;
        }

        public override bool Equals(object? obj)
        {
            return obj is DashContactEpochKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Pair.GetHashCode();
                hash = (hash * 397) ^ DashPlayerId.GetHashCode();
                return (hash * 397) ^ DashRuntimeBuffId.GetHashCode();
            }
        }
    }

    private readonly struct PendingInput
    {
        public PendingInput(uint frameIndex, uint inputSeq, Fixed64 dx, Fixed64 dy, int skillId)
        {
            FrameIndex = frameIndex;
            InputSeq = inputSeq;
            Dx = dx;
            Dy = dy;
            SkillId = skillId;
        }

        public uint FrameIndex { get; }
        public uint InputSeq { get; }
        public Fixed64 Dx { get; }
        public Fixed64 Dy { get; }
        public int SkillId { get; }
    }

    private readonly struct SubmittedInput
    {
        public SubmittedInput(uint frameIndex, uint inputSeq, Fixed64 dx, Fixed64 dy)
        {
            FrameIndex = frameIndex;
            InputSeq = inputSeq;
            Dx = dx;
            Dy = dy;
        }

        public uint FrameIndex { get; }
        public uint InputSeq { get; }
        public Fixed64 Dx { get; }
        public Fixed64 Dy { get; }
    }

}
