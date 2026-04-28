using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;

namespace Fantasy;

public sealed class BattleLogic
{
    private const int MaxFrameWindow = 10;
    private const uint StatsLogIntervalFrames = 300;
    private const int AcceptSampleCapacity = 300;
    private static readonly bool EnableHardReject = true; // Enabled after shadow validation passed.

    private readonly Dictionary<long, PlayerState> _statesByPlayerId = new();
    private readonly Dictionary<long, PendingInput> _pendingInputsByPlayerId = new();
    private readonly List<long> _playerIdBuffer = new();
    private readonly int[] _acceptAbsDeltaSamples = new int[AcceptSampleCapacity];
    private readonly Dictionary<long, int> _currentConsecutiveRejectByPlayerId = new();
    private readonly Dictionary<long, int> _maxConsecutiveRejectByPlayerId = new();
    private readonly Action<string>? _logDebug;
    private readonly Action<string>? _logWarning;

    private int _acceptedCount;
    private int _rejectedCount;
    private int _positiveDeltaCount;
    private int _negativeDeltaCount;
    private long _acceptAbsDeltaSum;
    private int _acceptAbsDeltaMin = int.MaxValue;
    private int _acceptAbsDeltaMax = int.MinValue;
    private int _acceptSampleWriteIndex;
    private int _acceptSampleCount;
    private long _rejectDeltaSum;
    private int _rejectDeltaMin = int.MaxValue;
    private int _rejectDeltaMax = int.MinValue;

    public BattleLogic(Action<string>? logDebug = null, Action<string>? logWarning = null)
    {
        _logDebug = logDebug;
        _logWarning = logWarning;
    }

    public uint LastFrameIndex { get; private set; }
    public Action<TestSnapshot>? OnBroadcast { get; set; }

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
        _currentConsecutiveRejectByPlayerId.Remove(playerId);
        _maxConsecutiveRejectByPlayerId.Remove(playerId);
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

    public void SubmitInput(long playerId, uint frameIndex, uint inputSeq, float dx, float dy)
    {
        if (!_statesByPlayerId.ContainsKey(playerId))
        {
            return;
        }

        if (_pendingInputsByPlayerId.TryGetValue(playerId, out PendingInput pendingInput) &&
            !IsInputNewerOrEqual(inputSeq, pendingInput.InputSeq))
        {
            return;
        }

        int frameDelta = ToFrameDelta(frameIndex, LastFrameIndex);
        RecordDeltaDirection(frameDelta);

        bool rejectedByWindow = IsFrameDeltaRejected(frameDelta);
        if (rejectedByWindow)
        {
            RecordRejectedInput(playerId, frameDelta);
            if (EnableHardReject)
            {
                return;
            }
        }
        else
        {
            RecordAcceptedInput(playerId, frameDelta);
        }

        _pendingInputsByPlayerId[playerId] = new PendingInput(frameIndex, inputSeq, dx, dy);
    }

    public void Tick(uint frameIndex, float fixedDt)
    {
        DeterminismRules.AssertFixedDt(fixedDt);
        LastFrameIndex = frameIndex;

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
            if (_pendingInputsByPlayerId.TryGetValue(playerId, out PendingInput input))
            {
                dx = input.Dx;
                dy = input.Dy;
            }

            MoveSystem.Apply(state!, dx, dy, fixedDt);
        }

        _pendingInputsByPlayerId.Clear();

        if (OnBroadcast != null)
        {
            OnBroadcast(BuildTestSnapshot(frameIndex));
        }

        TryLogFrameStats(frameIndex);
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

    private static int ToFrameDelta(uint inputFrameIndex, uint authoritativeFrameIndex)
    {
        uint rawDelta = unchecked(inputFrameIndex - authoritativeFrameIndex);
        return unchecked((int)rawDelta);
    }

    private static bool IsFrameDeltaRejected(int frameDelta)
    {
        return Math.Abs((long)frameDelta) > MaxFrameWindow;
    }

    private static int ToAbsFrameDelta(int frameDelta)
    {
        long absDelta = Math.Abs((long)frameDelta);
        return absDelta > int.MaxValue ? int.MaxValue : (int)absDelta;
    }

    private void RecordDeltaDirection(int frameDelta)
    {
        if (frameDelta > 0)
        {
            _positiveDeltaCount++;
            return;
        }

        if (frameDelta < 0)
        {
            _negativeDeltaCount++;
        }
    }

    private void RecordAcceptedInput(long playerId, int frameDelta)
    {
        int absFrameDelta = ToAbsFrameDelta(frameDelta);

        _acceptedCount++;
        _acceptAbsDeltaSum += absFrameDelta;
        if (absFrameDelta < _acceptAbsDeltaMin)
        {
            _acceptAbsDeltaMin = absFrameDelta;
        }

        if (absFrameDelta > _acceptAbsDeltaMax)
        {
            _acceptAbsDeltaMax = absFrameDelta;
        }

        _acceptAbsDeltaSamples[_acceptSampleWriteIndex] = absFrameDelta;
        _acceptSampleWriteIndex = (_acceptSampleWriteIndex + 1) % _acceptAbsDeltaSamples.Length;
        if (_acceptSampleCount < _acceptAbsDeltaSamples.Length)
        {
            _acceptSampleCount++;
        }

        _currentConsecutiveRejectByPlayerId[playerId] = 0;
    }

    private void RecordRejectedInput(long playerId, int frameDelta)
    {
        _rejectedCount++;
        _rejectDeltaSum += frameDelta;
        if (frameDelta < _rejectDeltaMin)
        {
            _rejectDeltaMin = frameDelta;
        }

        if (frameDelta > _rejectDeltaMax)
        {
            _rejectDeltaMax = frameDelta;
        }

        int currentConsecutiveReject = 1;
        if (_currentConsecutiveRejectByPlayerId.TryGetValue(playerId, out int current))
        {
            currentConsecutiveReject = current + 1;
        }

        _currentConsecutiveRejectByPlayerId[playerId] = currentConsecutiveReject;

        if (!_maxConsecutiveRejectByPlayerId.TryGetValue(playerId, out int maxConsecutiveReject) ||
            currentConsecutiveReject > maxConsecutiveReject)
        {
            _maxConsecutiveRejectByPlayerId[playerId] = currentConsecutiveReject;
        }
    }

    private void TryLogFrameStats(uint frameIndex)
    {
        if (frameIndex == 0 || frameIndex % StatsLogIntervalFrames != 0)
        {
            return;
        }

        int total = _acceptedCount + _rejectedCount;
        string mode = EnableHardReject ? "HardReject" : "Shadow";
        string rejectLabel = EnableHardReject ? "Rejected" : "WouldReject";

        double rejectRatePercent = total > 0 ? _rejectedCount * 100.0 / total : 0.0;
        double positiveRatePercent = total > 0 ? _positiveDeltaCount * 100.0 / total : 0.0;
        double negativeRatePercent = total > 0 ? _negativeDeltaCount * 100.0 / total : 0.0;

        string acceptMinText = _acceptedCount > 0 ? _acceptAbsDeltaMin.ToString() : "n/a";
        string acceptMaxText = _acceptedCount > 0 ? _acceptAbsDeltaMax.ToString() : "n/a";
        string acceptAvgText = _acceptedCount > 0 ? (_acceptAbsDeltaSum / (double)_acceptedCount).ToString("F2") : "n/a";
        string acceptP95Text = TryGetAcceptAbsDeltaP95(out int acceptP95) ? acceptP95.ToString() : "n/a";

        string rejectMinText = _rejectedCount > 0 ? _rejectDeltaMin.ToString() : "n/a";
        string rejectMaxText = _rejectedCount > 0 ? _rejectDeltaMax.ToString() : "n/a";
        string rejectAvgText = _rejectedCount > 0 ? (_rejectDeltaSum / (double)_rejectedCount).ToString("F2") : "n/a";

        GetMaxConsecutiveReject(out long playerId, out int maxConsecutiveReject);

        _logDebug?.Invoke(
            $"[Battle][FrameWindow] Mode={mode}, Frame={frameIndex}, Window=+/-{MaxFrameWindow}, Accepted={_acceptedCount}, {rejectLabel}={_rejectedCount}, RejectRate={rejectRatePercent:F2}%," +
            $" Accept|delta|[min/max/avg/p95]={acceptMinText}/{acceptMaxText}/{acceptAvgText}/{acceptP95Text}," +
            $" RejectDelta[min/max/avg]={rejectMinText}/{rejectMaxText}/{rejectAvgText}," +
            $" Sign[+/-]={positiveRatePercent:F2}%/{negativeRatePercent:F2}%, MaxConsecutive{rejectLabel}={maxConsecutiveReject}, MaxConsecutivePlayer={playerId}");

        if (maxConsecutiveReject > 3)
        {
            _logWarning?.Invoke(
                $"[Battle][FrameWindow] Consecutive {rejectLabel} exceeded threshold. Frame={frameIndex}, PlayerId={playerId}, Count={maxConsecutiveReject}");
        }

        ResetFrameStats();
    }

    private bool TryGetAcceptAbsDeltaP95(out int p95)
    {
        if (_acceptSampleCount == 0)
        {
            p95 = 0;
            return false;
        }

        int[] samples = new int[_acceptSampleCount];
        Array.Copy(_acceptAbsDeltaSamples, samples, _acceptSampleCount);
        Array.Sort(samples);

        int percentileIndex = Math.Max(0, (int)Math.Ceiling(samples.Length * 0.95d) - 1);
        p95 = samples[percentileIndex];
        return true;
    }

    private void GetMaxConsecutiveReject(out long playerId, out int maxConsecutiveReject)
    {
        playerId = 0;
        maxConsecutiveReject = 0;

        foreach (KeyValuePair<long, int> pair in _maxConsecutiveRejectByPlayerId)
        {
            if (pair.Value <= maxConsecutiveReject)
            {
                continue;
            }

            playerId = pair.Key;
            maxConsecutiveReject = pair.Value;
        }
    }

    private void ResetFrameStats()
    {
        _acceptedCount = 0;
        _rejectedCount = 0;
        _positiveDeltaCount = 0;
        _negativeDeltaCount = 0;

        _acceptAbsDeltaSum = 0;
        _acceptAbsDeltaMin = int.MaxValue;
        _acceptAbsDeltaMax = int.MinValue;
        _acceptSampleWriteIndex = 0;
        _acceptSampleCount = 0;

        _rejectDeltaSum = 0;
        _rejectDeltaMin = int.MaxValue;
        _rejectDeltaMax = int.MinValue;

        _currentConsecutiveRejectByPlayerId.Clear();
        _maxConsecutiveRejectByPlayerId.Clear();
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
}
