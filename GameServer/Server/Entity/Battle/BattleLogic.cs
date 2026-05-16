using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;

namespace Fantasy;

public sealed class BattleLogic
{
    private readonly Dictionary<long, PlayerState> _statesByPlayerId = new();
    private readonly Dictionary<long, Dictionary<uint, PendingInput>> _pendingInputsByPlayerId = new();
    private readonly Dictionary<long, ConsumedInput> _lastConsumedInputByPlayerId = new();
    private readonly Dictionary<long, SubmittedInput> _lastSubmittedInputByPlayerId = new();
    private readonly Dictionary<long, uint> _latestAcceptedInputFrameByPlayerId = new();
    private readonly List<long> _playerIdBuffer = new();
    private readonly Action<string>? _logDebug;
    private readonly Action<string>? _logWarning;
    private bool _hasProcessedFrame;

    public BattleLogic(Action<string>? logDebug = null, Action<string>? logWarning = null)
    {
        _logDebug = logDebug;
        _logWarning = logWarning;
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
        if (_statesByPlayerId.TryGetValue(playerId, out PlayerState? existingState))
        {
            return existingState!;
        }

        PlayerState newState = new PlayerState(playerId, x, y);
        _statesByPlayerId.Add(playerId, newState);
        return newState;
    }

    public bool RemovePlayer(long playerId)
    {
        _pendingInputsByPlayerId.Remove(playerId);
        _lastConsumedInputByPlayerId.Remove(playerId);
        _lastSubmittedInputByPlayerId.Remove(playerId);
        _latestAcceptedInputFrameByPlayerId.Remove(playerId);
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

    public void SubmitInput(long playerId, uint frameIndex, uint inputSeq, float dx, float dy)
    {
        if (!_statesByPlayerId.ContainsKey(playerId))
        {
            return;
        }

        bool isInputEdge = IsSubmittedInputEdge(playerId, dx, dy);
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

        playerInputs[frameIndex] = new PendingInput(frameIndex, inputSeq, dx, dy);
        _lastSubmittedInputByPlayerId[playerId] = new SubmittedInput(frameIndex, inputSeq, dx, dy);
        if (!_latestAcceptedInputFrameByPlayerId.TryGetValue(playerId, out uint latestAcceptedFrame) ||
            frameIndex >= latestAcceptedFrame)
        {
            _latestAcceptedInputFrameByPlayerId[playerId] = frameIndex;
        }
        AcceptedInputCount++;
    }

    public void Tick(uint frameIndex, float fixedDt)
    {
        DeterminismRules.AssertFixedDt(fixedDt);
        LastFrameIndex = frameIndex;
        _hasProcessedFrame = true;

        BuildSortedPlayerBuffer();

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState? state))
            {
                continue;
            }

            float dx = 0.0f;
            float dy = 0.0f;
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
                        ? $"({lastConsumedInput.Dx:F3},{lastConsumedInput.Dy:F3})"
                        : "(none)";
                    _logDebug?.Invoke(
                        $"[Battle][ConsumeInputEdge] frame={frameIndex} player={playerId} input={previousInput}->({dx:F3},{dy:F3})");
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

            MoveSystem.Apply(state!, dx, dy, fixedDt);
            if (!consumedCurrentFrameInput && _logDebug != null)
            {
                _logDebug($"[Battle][ReuseInput] Frame={frameIndex}, Player={playerId}, Dx={dx:F3}, Dy={dy:F3}");
            }
        }

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
        PlayerStateSnapshot[] players = BuildPlayerSnapshots();
        return new TestSnapshot(frameIndex, players);
    }

    private BattleWorldSnapshot BuildBattleWorldSnapshot(uint frameIndex)
    {
        PlayerStateSnapshot[] players = BuildPlayerSnapshots();
        return new BattleWorldSnapshot(frameIndex, players);
    }

    private PlayerStateSnapshot[] BuildPlayerSnapshots()
    {
        BuildSortedPlayerBuffer();
        PlayerStateSnapshot[] players = new PlayerStateSnapshot[_playerIdBuffer.Count];

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            PlayerState state = _statesByPlayerId[playerId];
            players[i] = new PlayerStateSnapshot(state.PlayerId, state.X, state.Y);
        }

        return players;
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

    private static bool IsInputNewerOrEqual(uint incomingSeq, uint cachedSeq)
    {
        return incomingSeq == cachedSeq || unchecked(incomingSeq - cachedSeq) < 0x80000000;
    }

    private static bool IsFutureFrameRejected(uint inputFrameIndex, uint maxAcceptedFrame)
    {
        return unchecked(inputFrameIndex - maxAcceptedFrame) < 0x80000000 && inputFrameIndex > maxAcceptedFrame;
    }

    private bool IsSubmittedInputEdge(long playerId, float dx, float dy)
    {
        if (!_lastSubmittedInputByPlayerId.TryGetValue(playerId, out SubmittedInput lastSubmittedInput))
        {
            return true;
        }

        return !AreInputsEqual(dx, dy, lastSubmittedInput.Dx, lastSubmittedInput.Dy);
    }

    private static bool AreInputsEqual(float leftDx, float leftDy, float rightDx, float rightDy)
    {
        return leftDx == rightDx && leftDy == rightDy;
    }

    private readonly struct PendingInput
    {
        public PendingInput(uint frameIndex, uint inputSeq, float dx, float dy)
        {
            FrameIndex = frameIndex;
            InputSeq = inputSeq;
            Dx = dx;
            Dy = dy;
        }

        public uint FrameIndex { get; }
        public uint InputSeq { get; }
        public float Dx { get; }
        public float Dy { get; }
    }

    private readonly struct SubmittedInput
    {
        public SubmittedInput(uint frameIndex, uint inputSeq, float dx, float dy)
        {
            FrameIndex = frameIndex;
            InputSeq = inputSeq;
            Dx = dx;
            Dy = dy;
        }

        public uint FrameIndex { get; }
        public uint InputSeq { get; }
        public float Dx { get; }
        public float Dy { get; }
    }

    private readonly struct ConsumedInput
    {
        public ConsumedInput(float dx, float dy)
        {
            Dx = dx;
            Dy = dy;
        }

        public float Dx { get; }
        public float Dy { get; }
    }
}
