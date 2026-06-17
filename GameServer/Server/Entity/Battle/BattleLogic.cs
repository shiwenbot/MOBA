using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Command;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;
using GameShared.SkillGraph;

namespace Fantasy;

public sealed class BattleLogic : IBuffCommandSink
{
    private readonly Dictionary<long, PlayerState> _statesByPlayerId = new();
    private readonly FrameSyncPhysicsWorld _physicsWorld = new();
    private readonly Dictionary<long, Dictionary<uint, PendingInput>> _pendingInputsByPlayerId = new();
    private readonly Dictionary<long, ConsumedInput> _lastConsumedInputByPlayerId = new();
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
    private bool _hasProcessedFrame;

    public BattleLogic(
        Action<string>? logDebug = null,
        Action<string>? logWarning = null,
        IReadOnlyDictionary<int, RuntimeSkillGraph>? skillGraphs = null)
    {
        _logDebug = logDebug;
        _logWarning = logWarning;
        _buffConfigProvider = new DefaultBuffConfigProvider();
        _skillGraphRuntime = new BattleSkillGraphRuntime(this, skillGraphs ?? BattleSkillGraphLibrary.LoadDefaultGraphs());
    }

    public uint LastFrameIndex { get; private set; }
    public Action<TestSnapshot>? OnBroadcast { get; set; }
    public int AcceptedInputCount { get; private set; }
    public int LateInputDropCount { get; private set; }
    public int FutureInputRejectCount { get; private set; }
    public int ReusedInputCount { get; private set; }
    public int ZeroInputFallbackCount { get; private set; }

    public PlayerState JoinPlayer(long playerId, float x, float y)
    {
        return JoinPlayer(playerId, (Fixed64)x, (Fixed64)y);
    }

    public PlayerState JoinPlayer(long playerId, Fixed64 x, Fixed64 y)
    {
        if (_statesByPlayerId.TryGetValue(playerId, out PlayerState? existingState))
        {
            return existingState!;
        }

        PlayerState newState = new PlayerState(playerId, x, y);
        _statesByPlayerId.Add(playerId, newState);
        _physicsWorld.EnsureBody(checked((int)playerId), x, y);
        return newState;
    }

    public bool RemovePlayer(long playerId)
    {
        _pendingInputsByPlayerId.Remove(playerId);
        _lastConsumedInputByPlayerId.Remove(playerId);
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

    public void SubmitInput(long playerId, uint frameIndex, uint inputSeq, float dx, float dy, int skillId = 0)
    {
        if (!_statesByPlayerId.ContainsKey(playerId))
        {
            return;
        }

        Fixed64 fixedDx = (Fixed64)dx;
        Fixed64 fixedDy = (Fixed64)dy;
        bool isInputEdge = IsSubmittedInputEdge(playerId, fixedDx, fixedDy);
        // Allow frame 0 input before the first authoritative tick starts.
        if (_hasProcessedFrame && frameIndex <= LastFrameIndex)
        {
            LateInputDropCount++;
            if (isInputEdge)
            {
                _logWarning?.Invoke(
                    $"[Battle][LateInputDrop] player={playerId} frame={frameIndex} current={LastFrameIndex} input=({dx:F3},{dy:F3}) seq={inputSeq}");
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
                $"[Battle][SubmitInputEdge] player={playerId} frame={frameIndex} current={LastFrameIndex} input=({dx:F3},{dy:F3}) seq={inputSeq}");
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
            bool hadLastConsumedInput = _lastConsumedInputByPlayerId.TryGetValue(playerId, out ConsumedInput lastConsumedInput);
            if (_pendingInputsByPlayerId.TryGetValue(playerId, out Dictionary<uint, PendingInput>? playerInputs) &&
                playerInputs.TryGetValue(frameIndex, out PendingInput input))
            {
                dx = input.Dx;
                dy = input.Dy;

                if (!hadLastConsumedInput || !AreInputsEqual(dx, dy, lastConsumedInput.Dx, lastConsumedInput.Dy))
                {
                    string previousInput = hadLastConsumedInput
                        ? FormatInput(lastConsumedInput.Dx, lastConsumedInput.Dy)
                        : "(none)";
                    _logDebug?.Invoke(
                        $"[Battle][ConsumeInputEdge] frame={frameIndex} player={playerId} input={previousInput}->{FormatInput(dx, dy)}");
                }

                _lastConsumedInputByPlayerId[playerId] = new ConsumedInput(dx, dy);
                consumedCurrentFrameInput = true;
                playerInputs.Remove(frameIndex);
                if (playerInputs.Count == 0)
                {
                    _pendingInputsByPlayerId.Remove(playerId);
                }
            }
            else if (hadLastConsumedInput)
            {
                dx = lastConsumedInput.Dx;
                dy = lastConsumedInput.Dy;
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

        _physicsWorld.Step(fixedDt);
        SyncPlayerStatesFromPhysics();
        RecalculateNumericStates();
        ApplyBuffTicks(frameIndex);

        if (OnBroadcast != null)
        {
            OnBroadcast(BuildTestSnapshot(frameIndex));
        }
    }

    public ulong GetStateHash()
    {
        return StateHasher.Hash(BuildBattleWorldSnapshot(LastFrameIndex));
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
                state.Numeric.CaptureSnapshot());
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
            if (!_pendingInputsByPlayerId.TryGetValue(playerId, out Dictionary<uint, PendingInput>? playerInputs) ||
                !playerInputs.TryGetValue(frameIndex, out PendingInput input) ||
                input.SkillId <= 0)
            {
                continue;
            }

            _skillGraphRuntime.QueueSkillRequest(playerId, playerId, input.SkillId, frameIndex);
        }
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

    private readonly struct ConsumedInput
    {
        public ConsumedInput(Fixed64 dx, Fixed64 dy)
        {
            Dx = dx;
            Dy = dy;
        }

        public Fixed64 Dx { get; }
        public Fixed64 Dy { get; }
    }
}
