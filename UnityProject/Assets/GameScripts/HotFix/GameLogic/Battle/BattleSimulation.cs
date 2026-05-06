using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using Log = TEngine.Log;

namespace GameLogic
{
    public sealed class BattleSimulation
    {
        private const int PingIntervalFrames = 30;
        private const int PredictionBufferCapacity = 32;
        private const int InputHistoryCapacity = 128;
        private const int ConsistencyLogInterval = 300;
        private const float InitialRttEmaMs = 100f;
        private const float RttEmaAlpha = 0.2f;
        private const int MinLeadFrames = 3;
        private const int MaxLeadFrames = 8;
        // 多留 1 帧缓冲，吸收网络抖动和服务端帧边界调度误差。
        private const int JitterBufferFrames = 2;
        private static readonly float FixedDeltaMilliseconds = DeterminismRules.FixedDeltaTime * 1000f;

        private readonly BattleWorldState _worldState;
        private readonly Action<uint, uint, float, float> _onSendInput;
        private readonly Action<ulong> _onSendPing;
        private readonly GameShared.FrameSync.Core.IFrameSyncLogger _logger;
        private readonly HashSet<long> _stalePlayerIds = new HashSet<long>();
        private readonly Dictionary<uint, SelfPrediction> _selfPredictions =
            new Dictionary<uint, SelfPrediction>(PredictionBufferCapacity);
        private readonly Dictionary<uint, BufferedInput> _inputHistory =
            new Dictionary<uint, BufferedInput>(InputHistoryCapacity);

        private bool _isJoined;
        private bool _hasRttSample;
        private long _selfPlayerId;
        private uint _inputSeq;
        private uint _lastAppliedFrame;
        private uint _lastPredictedFrame;
        private uint _localFrame;
        private uint _leadFrames = MinLeadFrames;
        private float _rttEmaMs = InitialRttEmaMs;
        private int _pingCount;
        private int _checked;
        private int _hits;
        private int _misses;
        private int _skippedNoRecord;
        private int _skippedEvicted;
        private bool _hasPendingServerSnapshot;
        private BattleWorldSnapshot _pendingServerSnapshot;

        public BattleSimulation(
            BattleWorldState worldState,
            Action<uint, uint, float, float> onSendInput,
            Action<ulong> onSendPing,
            GameShared.FrameSync.Core.IFrameSyncLogger logger = null)
        {
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _onSendInput = onSendInput ?? throw new ArgumentNullException(nameof(onSendInput));
            _onSendPing = onSendPing ?? throw new ArgumentNullException(nameof(onSendPing));
            _logger = logger;
        }

        public bool IsJoined => _isJoined;
        public long SelfPlayerId => _selfPlayerId;
        public uint LeadFrames => _leadFrames;
        public uint LastAppliedFrame => _lastAppliedFrame;
        public uint LocalFrame => _localFrame;
        public uint InitialAlignedFrame => unchecked(_lastAppliedFrame + _leadFrames);
        public int ConsistencyChecked => _checked;
        public int ConsistencyHits => _hits;
        public int ConsistencyMisses => _misses;
        public int ConsistencySkippedNoRecord => _skippedNoRecord;
        public int ConsistencySkippedEvicted => _skippedEvicted;

        public void EnqueueServerSnapshot(BattleWorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.FrameIndex <= _lastAppliedFrame)
            {
                return;
            }

            if (_hasPendingServerSnapshot &&
                _pendingServerSnapshot != null &&
                snapshot.FrameIndex <= _pendingServerSnapshot.FrameIndex)
            {
                return;
            }

            _pendingServerSnapshot = snapshot;
            _hasPendingServerSnapshot = true;
        }

        public void ProcessPong(long rttMs)
        {
            if (rttMs < 0)
            {
                return;
            }

            if (!_hasRttSample)
            {
                _rttEmaMs = rttMs;
                _hasRttSample = true;
            }
            else
            {
                _rttEmaMs = (RttEmaAlpha * rttMs) + ((1.0f - RttEmaAlpha) * _rttEmaMs);
            }

            int leadFrames = JitterBufferFrames + (int)Math.Ceiling(_rttEmaMs / 2.0f / FixedDeltaMilliseconds);
            _leadFrames = (uint)Math.Clamp(leadFrames, MinLeadFrames, MaxLeadFrames);
        }

        public TickResult Tick(uint frameIndex, float fixedDt, float dx, float dy)
        {
            if (!_isJoined)
            {
                return default;
            }

            try
            {
                DeterminismRules.AssertFixedDt(fixedDt);
                DeterminismRules.AssertFinite(dx, nameof(dx));
                DeterminismRules.AssertFinite(dy, nameof(dy));

                _localFrame = frameIndex;
                NormalizeInput(ref dx, ref dy);

                SaveInputHistory(frameIndex, dx, dy);
                SendPingIfNeeded();
                _onSendInput(frameIndex, ++_inputSeq, dx, dy);

                bool snapshotApplied = ApplyPendingServerSnapshot(out int catchUpFrames, out bool consistencyMismatch);
                AdvancePredictionTo(frameIndex, fixedDt);

                return new TickResult(snapshotApplied, _lastAppliedFrame, catchUpFrames, consistencyMismatch);
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, $"BattleSimulation.Tick failed. Frame={frameIndex}.");
                throw;
            }
        }

        public void SetJoined(long playerId, uint serverFrame, float x, float y)
        {
            ClearWorldState();

            _selfPlayerId = playerId;
            _lastAppliedFrame = serverFrame;
            _lastPredictedFrame = serverFrame;
            _localFrame = serverFrame;
            _inputSeq = 0;
            _leadFrames = MinLeadFrames;
            _rttEmaMs = InitialRttEmaMs;
            _hasRttSample = false;
            _pingCount = 0;
            _checked = 0;
            _hits = 0;
            _misses = 0;
            _skippedNoRecord = 0;
            _skippedEvicted = 0;
            _selfPredictions.Clear();
            _inputHistory.Clear();
            _hasPendingServerSnapshot = false;
            _pendingServerSnapshot = null;
            _isJoined = true;

            _worldState.AddOrUpdatePlayer(playerId, x, y);
        }

        public void RollBack(uint targetFrame)
        {
            // TODO v0.3c: restore simulation metadata together with SnapshotManager.RollBack.
        }

        public static bool RunSelfTest(out string failedCase)
        {
            return BattlePredictionSelfTestSuite.Run(out failedCase);
        }

        private bool ApplyPendingServerSnapshot(out int catchUpFrames, out bool consistencyMismatch)
        {
            catchUpFrames = 0;
            consistencyMismatch = false;
            if (!_hasPendingServerSnapshot || _pendingServerSnapshot == null)
            {
                return false;
            }

            BattleWorldSnapshot snapshot = _pendingServerSnapshot;
            _hasPendingServerSnapshot = false;
            _pendingServerSnapshot = null;

            if (snapshot.FrameIndex <= _lastAppliedFrame)
            {
                return false;
            }

            consistencyMismatch = CheckConsistency(snapshot);
            ApplyAuthoritativeSnapshot(snapshot);

            uint targetLocalFrame = unchecked(snapshot.FrameIndex + _leadFrames);
            int frameDeltaToTarget = unchecked((int)(targetLocalFrame - _localFrame));
            catchUpFrames = frameDeltaToTarget > 0 ? frameDeltaToTarget : 0;
            return true;
        }

        private bool CheckConsistency(BattleWorldSnapshot snapshot)
        {
            bool hasAuthoritativeSelf = TryGetAuthoritativeSelf(snapshot, out PlayerStateSnapshot authoritativeSelf);
            bool hasPrediction = _selfPredictions.TryGetValue(snapshot.FrameIndex, out SelfPrediction prediction);

            if (hasPrediction)
            {
                _selfPredictions.Remove(snapshot.FrameIndex);
            }

            if (!hasAuthoritativeSelf || !hasPrediction)
            {
                _skippedNoRecord++;
                return false;
            }

            _checked++;
            if (prediction.X == authoritativeSelf.X && prediction.Y == authoritativeSelf.Y)
            {
                _hits++;
            }
            else
            {
                _misses++;
                float deltaX = authoritativeSelf.X - prediction.X;
                float deltaY = authoritativeSelf.Y - prediction.Y;
                Log.Warning(
                    $"[Consistency] MISMATCH frame={snapshot.FrameIndex} pred=({prediction.X},{prediction.Y}) auth=({authoritativeSelf.X},{authoritativeSelf.Y}) delta=({deltaX:F4},{deltaY:F4})");
            }

            if (_checked > 0 && (_checked % ConsistencyLogInterval) == 0)
            {
                float rate = (float)_hits / _checked;
                Log.Info(
                    $"[Consistency] HitRate={rate:P1} hit={_hits} miss={_misses} noRecord={_skippedNoRecord} evicted={_skippedEvicted}");
            }

            return prediction.X != authoritativeSelf.X || prediction.Y != authoritativeSelf.Y;
        }

        private bool TryGetAuthoritativeSelf(BattleWorldSnapshot snapshot, out PlayerStateSnapshot selfSnapshot)
        {
            IReadOnlyList<PlayerStateSnapshot> players = snapshot.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                if (player.PlayerId == _selfPlayerId)
                {
                    selfSnapshot = player;
                    return true;
                }
            }

            selfSnapshot = default;
            return false;
        }

        private void ApplySnapshotToWorldState(BattleWorldSnapshot snapshot)
        {
            _stalePlayerIds.Clear();
            foreach (PlayerState player in _worldState.Players)
            {
                _stalePlayerIds.Add(player.PlayerId);
            }

            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerStateSnapshot player = snapshot.Players[i];
                _worldState.AddOrUpdatePlayer(player.PlayerId, player.X, player.Y);
                _stalePlayerIds.Remove(player.PlayerId);
            }

            foreach (long stalePlayerId in _stalePlayerIds)
            {
                _worldState.RemovePlayer(stalePlayerId);
            }

            _stalePlayerIds.Clear();
        }

        private void ApplyAuthoritativeSnapshot(BattleWorldSnapshot snapshot)
        {
            _lastAppliedFrame = snapshot.FrameIndex;
            ApplySnapshotToWorldState(snapshot);
            _lastPredictedFrame = snapshot.FrameIndex;
            RemoveConfirmedInputHistory(snapshot.FrameIndex);
        }

        private void SendPingIfNeeded()
        {
            _pingCount++;
            if (_pingCount < PingIntervalFrames)
            {
                return;
            }

            _pingCount = 0;
            _onSendPing((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        private void AdvancePredictionTo(uint targetFrame, float fixedDt)
        {
            int frameCount = unchecked((int)(targetFrame - _lastPredictedFrame));
            if (frameCount <= 0)
            {
                return;
            }

            if (!_worldState.TryGetPlayer(_selfPlayerId, out _))
            {
                return;
            }

            for (int i = 1; i <= frameCount; i++)
            {
                uint replayFrame = unchecked(_lastPredictedFrame + (uint)i);
                BufferedInput input = _inputHistory.TryGetValue(replayFrame, out BufferedInput bufferedInput)
                    ? bufferedInput
                    : default;
                ApplyLocalPrediction(replayFrame, input.Dx, input.Dy, fixedDt);
            }

            _lastPredictedFrame = targetFrame;
        }

        private void SaveInputHistory(uint frameIndex, float dx, float dy)
        {
            if (!_inputHistory.ContainsKey(frameIndex) && _inputHistory.Count >= InputHistoryCapacity)
            {
                uint oldest = uint.MaxValue;
                foreach (uint key in _inputHistory.Keys)
                {
                    if (key < oldest)
                    {
                        oldest = key;
                    }
                }

                if (oldest != uint.MaxValue)
                {
                    _inputHistory.Remove(oldest);
                }
            }

            _inputHistory[frameIndex] = new BufferedInput(dx, dy);
        }

        private void RemoveConfirmedInputHistory(uint confirmedFrame)
        {
            if (_inputHistory.Count == 0)
            {
                return;
            }

            List<uint> framesToRemove = null;
            foreach (uint frame in _inputHistory.Keys)
            {
                if (frame <= confirmedFrame)
                {
                    framesToRemove ??= new List<uint>();
                    framesToRemove.Add(frame);
                }
            }

            if (framesToRemove == null)
            {
                return;
            }

            for (int i = 0; i < framesToRemove.Count; i++)
            {
                _inputHistory.Remove(framesToRemove[i]);
            }
        }

        private void ApplyLocalPrediction(uint frameIndex, float dx, float dy, float fixedDt)
        {
            if (!_worldState.TryGetPlayer(_selfPlayerId, out PlayerState selfPlayer))
            {
                return;
            }

            MoveSystem.Apply(selfPlayer, dx, dy, fixedDt);
            SaveSelfPrediction(frameIndex, selfPlayer.X, selfPlayer.Y);
        }

        private void SaveSelfPrediction(uint frameIndex, float x, float y)
        {
            if (!_selfPredictions.ContainsKey(frameIndex) && _selfPredictions.Count >= PredictionBufferCapacity)
            {
                uint oldest = uint.MaxValue;
                foreach (uint key in _selfPredictions.Keys)
                {
                    if (key < oldest)
                    {
                        oldest = key;
                    }
                }

                if (oldest != uint.MaxValue)
                {
                    _selfPredictions.Remove(oldest);
                    _skippedEvicted++;
                }
            }

            _selfPredictions[frameIndex] = new SelfPrediction(x, y);
        }

        private void ClearWorldState()
        {
            _stalePlayerIds.Clear();
            foreach (PlayerState player in _worldState.Players)
            {
                _stalePlayerIds.Add(player.PlayerId);
            }

            foreach (long playerId in _stalePlayerIds)
            {
                _worldState.RemovePlayer(playerId);
            }

            _stalePlayerIds.Clear();
        }

        private static void NormalizeInput(ref float dx, ref float dy)
        {
            float sqrMagnitude = (dx * dx) + (dy * dy);
            if (sqrMagnitude <= 1.0f)
            {
                return;
            }

            float inverseMagnitude = 1.0f / MathF.Sqrt(sqrMagnitude);
            dx *= inverseMagnitude;
            dy *= inverseMagnitude;
        }
    }

    internal readonly struct SelfPrediction
    {
        public SelfPrediction(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
    }

    internal readonly struct BufferedInput
    {
        public BufferedInput(float dx, float dy)
        {
            Dx = dx;
            Dy = dy;
        }

        public float Dx { get; }
        public float Dy { get; }
    }

    public readonly struct TickResult
    {
        public TickResult(bool snapshotApplied, uint lastAppliedFrame, int catchUpFrames, bool consistencyMismatch = false)
        {
            SnapshotApplied = snapshotApplied;
            LastAppliedFrame = lastAppliedFrame;
            CatchUpFrames = catchUpFrames;
            ConsistencyMismatch = consistencyMismatch;
        }

        public bool SnapshotApplied { get; }
        public uint LastAppliedFrame { get; }
        public int CatchUpFrames { get; }
        public bool ConsistencyMismatch { get; }
    }
}
