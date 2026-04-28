using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;

namespace GameLogic
{
    public sealed class BattleSimulation
    {
        private const int PingIntervalFrames = 30;
        private const float InitialRttEmaMs = 100f;
        private const float RttEmaAlpha = 0.2f;
        private const int MaxLeadFrames = 8;
        private static readonly float FixedDeltaMilliseconds = DeterminismRules.FixedDeltaTime * 1000f;

        private readonly BattleWorldState _worldState;
        private readonly Action<uint, uint, float, float> _onSendInput;
        private readonly Action<ulong> _onSendPing;
        private readonly IFrameSyncLogger _logger;
        private readonly HashSet<long> _stalePlayerIds = new HashSet<long>();

        private bool _isJoined;
        private bool _hasRttSample;
        private long _selfPlayerId;
        private uint _inputSeq;
        private uint _lastAppliedFrame;
        private uint _lastSentFrameIndex;
        private uint _serverFrameOffset;
        private uint _leadFrames = 1;
        private float _rttEmaMs = InitialRttEmaMs;
        private int _pingCount;
        private bool _hasPendingServerSnapshot;
        private BattleWorldSnapshot _pendingServerSnapshot;

        public BattleSimulation(
            BattleWorldState worldState,
            Action<uint, uint, float, float> onSendInput,
            Action<ulong> onSendPing,
            IFrameSyncLogger logger = null)
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
        public uint ServerFrameOffset => _serverFrameOffset;

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

            int leadFrames = (int)Math.Ceiling(_rttEmaMs / 2.0f / FixedDeltaMilliseconds);
            _leadFrames = (uint)Math.Clamp(leadFrames, 1, MaxLeadFrames);
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

                bool snapshotApplied = ApplyPendingServerSnapshot(frameIndex, out int catchUpFrames);
                SendPingIfNeeded();
                NormalizeInput(ref dx, ref dy);

                uint predictedServerFrame = ToPredictedServerFrame(frameIndex);
                uint sendFrame = predictedServerFrame;
                uint minFrame = unchecked(_lastSentFrameIndex + 1);
                if (sendFrame < minFrame)
                {
                    sendFrame = minFrame;
                }

                _lastSentFrameIndex = sendFrame;
                _onSendInput(sendFrame, ++_inputSeq, dx, dy);

                // TODO v0.3c: persist (frameIndex, dx, dy) into an input history buffer.
                return new TickResult(snapshotApplied, _lastAppliedFrame, catchUpFrames);
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, $"BattleSimulation.Tick failed. Frame={frameIndex}.");
                throw;
            }
        }

        public void SetJoined(long playerId, uint serverFrame, uint localFrame, float x, float y)
        {
            ClearWorldState();

            _selfPlayerId = playerId;
            _lastAppliedFrame = serverFrame;
            _serverFrameOffset = unchecked(serverFrame - localFrame);
            _lastSentFrameIndex = 0;
            _inputSeq = 0;
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
            try
            {
                if (!PureLogicRoundTrip())
                {
                    failedCase = "pure-logic-roundtrip";
                    return false;
                }

                if (!RttLeadFrameClamp())
                {
                    failedCase = "rtt-leadframe-clamp";
                    return false;
                }

                if (!PredictedFrameOffset())
                {
                    failedCase = "predicted-frame-offset";
                    return false;
                }

                if (!InputNormalization())
                {
                    failedCase = "input-normalization";
                    return false;
                }
            }
            catch (Exception exception)
            {
                failedCase = $"{exception.GetType().Name}:{exception.Message}";
                return false;
            }

            failedCase = string.Empty;
            return true;
        }

        private bool ApplyPendingServerSnapshot(uint localFrame, out int catchUpFrames)
        {
            catchUpFrames = 0;
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

            _lastAppliedFrame = snapshot.FrameIndex;
            ApplySnapshotToWorldState(snapshot);

            uint predictedServerFrame = ToPredictedServerFrame(localFrame);
            uint targetServerFrame = unchecked(snapshot.FrameIndex + _leadFrames);
            int frameDeltaToTarget = unchecked((int)(targetServerFrame - predictedServerFrame));
            _serverFrameOffset = unchecked(targetServerFrame - localFrame);
            catchUpFrames = frameDeltaToTarget > 0 ? frameDeltaToTarget : 0;
            return true;
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

        private uint ToPredictedServerFrame(uint localFrame)
        {
            return unchecked(localFrame + _serverFrameOffset);
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

        private static bool PureLogicRoundTrip()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = new BattleSimulation(
                worldState,
                static (_, _, _, _) => { },
                static _ => { });

            simulation.SetJoined(1, 30, 20, 1.0f, 2.0f);
            simulation.EnqueueServerSnapshot(new BattleWorldSnapshot(
                31,
                new[]
                {
                    new PlayerStateSnapshot(1, 10.0f, 20.0f),
                    new PlayerStateSnapshot(2, -3.5f, 4.5f)
                }));

            TickResult result = simulation.Tick(20, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            if (!result.SnapshotApplied || result.LastAppliedFrame != 31 || worldState.PlayerCount != 2)
            {
                return false;
            }

            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            if (!worldState.TryGetPlayer(2, out PlayerState otherPlayer))
            {
                return false;
            }

            return Math.Abs(selfPlayer.X - 10.0f) < 0.0001f &&
                   Math.Abs(selfPlayer.Y - 20.0f) < 0.0001f &&
                   Math.Abs(otherPlayer.X + 3.5f) < 0.0001f &&
                   Math.Abs(otherPlayer.Y - 4.5f) < 0.0001f;
        }

        private static bool RttLeadFrameClamp()
        {
            BattleSimulation lowLatencySimulation = new BattleSimulation(
                new BattleWorldState(),
                static (_, _, _, _) => { },
                static _ => { });
            lowLatencySimulation.ProcessPong(1);
            if (lowLatencySimulation.LeadFrames != 1)
            {
                return false;
            }

            BattleSimulation highLatencySimulation = new BattleSimulation(
                new BattleWorldState(),
                static (_, _, _, _) => { },
                static _ => { });
            highLatencySimulation.ProcessPong(10000);
            return highLatencySimulation.LeadFrames == MaxLeadFrames;
        }

        private static bool PredictedFrameOffset()
        {
            BattleSimulation simulation = new BattleSimulation(
                new BattleWorldState(),
                static (_, _, _, _) => { },
                static _ => { });
            simulation.SetJoined(7, 120, 100, 0.0f, 0.0f);
            return simulation.ToPredictedServerFrame(105) == 125;
        }

        private static bool InputNormalization()
        {
            float capturedDx = 0.0f;
            float capturedDy = 0.0f;
            BattleSimulation simulation = new BattleSimulation(
                new BattleWorldState(),
                (_, _, dx, dy) =>
                {
                    capturedDx = dx;
                    capturedDy = dy;
                },
                static _ => { });

            simulation.SetJoined(9, 60, 50, 0.0f, 0.0f);
            simulation.Tick(50, DeterminismRules.FixedDeltaTime, 1.0f, 1.0f);

            float sqrMagnitude = (capturedDx * capturedDx) + (capturedDy * capturedDy);
            return sqrMagnitude <= 1.0001f && sqrMagnitude >= 0.9990f;
        }
    }

    public readonly struct TickResult
    {
        public TickResult(bool snapshotApplied, uint lastAppliedFrame, int catchUpFrames)
        {
            SnapshotApplied = snapshotApplied;
            LastAppliedFrame = lastAppliedFrame;
            CatchUpFrames = catchUpFrames;
        }

        public bool SnapshotApplied { get; }
        public uint LastAppliedFrame { get; }
        public int CatchUpFrames { get; }
    }
}
