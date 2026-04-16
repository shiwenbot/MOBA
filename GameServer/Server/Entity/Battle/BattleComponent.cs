using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using Fantasy.Network;

namespace Fantasy;

public sealed class BattleComponent : Entitas.Entity, ITickable
{
    private const int MaxFrameWindow = 10;
    private const uint StatsLogIntervalFrames = 300;
    private const int AcceptSampleCapacity = 300;
    private static readonly bool EnableHardReject = true; // Enabled after shadow validation passed.

    private readonly Dictionary<long, PlayerState> _statesByPlayerId = new();
    private readonly Dictionary<long, PlayerSession> _sessionsByPlayerId = new();
    private readonly Dictionary<long, long> _playerIdBySessionId = new();
    private readonly Dictionary<long, PendingInput> _pendingInputsByPlayerId = new();
    private readonly List<long> _playerIdBuffer = new();
    private readonly int[] _acceptAbsDeltaSamples = new int[AcceptSampleCapacity];
    private readonly Dictionary<long, int> _currentConsecutiveRejectByPlayerId = new();
    private readonly Dictionary<long, int> _maxConsecutiveRejectByPlayerId = new();

    private long _nextPlayerId = 1;
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

    public int Priority => 0;
    public uint LastFrameIndex { get; private set; }

    public PlayerState Join(Session session)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        CleanupDisconnectedPlayers();

        if (_playerIdBySessionId.TryGetValue(session.Id, out long existingPlayerId) &&
            _statesByPlayerId.TryGetValue(existingPlayerId, out PlayerState existingState))
        {
            if (_sessionsByPlayerId.TryGetValue(existingPlayerId, out PlayerSession existingPlayerSession))
            {
                existingPlayerSession.Session = session;
            }

            return existingState;
        }

        long playerId = _nextPlayerId++;
        (float spawnX, float spawnY) = GetSpawnPosition(_statesByPlayerId.Count);
        PlayerState newState = new PlayerState(playerId, spawnX, spawnY);

        _statesByPlayerId.Add(playerId, newState);
        _sessionsByPlayerId[playerId] = new PlayerSession(playerId, session);
        _playerIdBySessionId[session.Id] = playerId;

        return newState;
    }

    public void SubmitInput(Session session, C2B_PlayerInput input)
    {
        if (session == null || input == null)
        {
            return;
        }

        if (!_playerIdBySessionId.TryGetValue(session.Id, out long playerId))
        {
            return;
        }

        if (_pendingInputsByPlayerId.TryGetValue(playerId, out PendingInput pendingInput) &&
            !IsInputNewerOrEqual(input.InputSeq, pendingInput.InputSeq))
        {
            return;
        }

        int frameDelta = ToFrameDelta(input.FrameIndex, LastFrameIndex);
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

        _pendingInputsByPlayerId[playerId] = new PendingInput(input.FrameIndex, input.InputSeq, input.Dx, input.Dy);
    }

    public void Tick(uint frameIndex, float fixedDt)
    {
        DeterminismRules.AssertFixedDt(fixedDt);
        LastFrameIndex = frameIndex;

        CleanupDisconnectedPlayers();
        BuildSortedPlayerBuffer();

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState state))
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

            MoveSystem.Apply(state, dx, dy, fixedDt);
        }

        _pendingInputsByPlayerId.Clear();
        BroadcastSnapshot(frameIndex);
        TryLogFrameStats(frameIndex);
    }

    private void BroadcastSnapshot(uint frameIndex)
    {
        if (_sessionsByPlayerId.Count == 0)
        {
            return;
        }

        S2C_FrameSnapshot snapshot = new S2C_FrameSnapshot
        {
            FrameIndex = frameIndex
        };

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState state))
            {
                continue;
            }

            snapshot.Players.Add(new PlayerSnapshot
            {
                PlayerId = state.PlayerId,
                X = state.X,
                Y = state.Y
            });
        }

        _playerIdBuffer.Clear();

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            session.Send(snapshot);
        }

        Log.Debug($"[Battle] Frame={frameIndex}, Players={snapshot.Players.Count}, Broadcast");
    }

    private void CleanupDisconnectedPlayers()
    {
        _playerIdBuffer.Clear();

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                _playerIdBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];

            if (_sessionsByPlayerId.TryGetValue(playerId, out PlayerSession playerSession))
            {
                long sessionId = playerSession.SessionId;
                if (sessionId != 0)
                {
                    _playerIdBySessionId.Remove(sessionId);
                }
            }

            _sessionsByPlayerId.Remove(playerId);
            _statesByPlayerId.Remove(playerId);
            _pendingInputsByPlayerId.Remove(playerId);
            _currentConsecutiveRejectByPlayerId.Remove(playerId);
            _maxConsecutiveRejectByPlayerId.Remove(playerId);
        }
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

    private static (float x, float y) GetSpawnPosition(int playerCount)
    {
        const float spacing = 3.0f;
        int row = playerCount / 2;
        float x = (playerCount & 1) == 0 ? -spacing : spacing;
        float y = row * spacing;
        return (x, y);
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

        Log.Debug(
            $"[Battle][FrameWindow] Mode={mode}, Frame={frameIndex}, Window=+/-{MaxFrameWindow}, Accepted={_acceptedCount}, {rejectLabel}={_rejectedCount}, RejectRate={rejectRatePercent:F2}%," +
            $" Accept|delta|[min/max/avg/p95]={acceptMinText}/{acceptMaxText}/{acceptAvgText}/{acceptP95Text}," +
            $" RejectDelta[min/max/avg]={rejectMinText}/{rejectMaxText}/{rejectAvgText}," +
            $" Sign[+/-]={positiveRatePercent:F2}%/{negativeRatePercent:F2}%, MaxConsecutive{rejectLabel}={maxConsecutiveReject}, MaxConsecutivePlayer={playerId}");

        if (maxConsecutiveReject > 3)
        {
            Log.Warning(
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
