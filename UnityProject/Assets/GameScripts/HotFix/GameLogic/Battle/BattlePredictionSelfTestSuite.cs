using System;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;

namespace GameLogic
{
    public static class BattlePredictionSelfTestSuite
    {
        public static bool Run(out string failedCase)
        {
            try
            {
                if (!JoinAlignsGlobalFrame())
                {
                    failedCase = "join-aligns-global-frame";
                    return false;
                }

                if (!FrameIndexIsGlobal())
                {
                    failedCase = "frame-index-is-global";
                    return false;
                }

                if (!InputNormalization())
                {
                    failedCase = "input-normalization";
                    return false;
                }

                if (!PredictionMovesSelfPlayer())
                {
                    failedCase = "prediction-moves-self-player";
                    return false;
                }

                if (!CatchUpTargetIsAuthPlusLead())
                {
                    failedCase = "catchup-target-is-auth-plus-lead";
                    return false;
                }

                if (!ConsistencyHit())
                {
                    failedCase = "consistency-hit";
                    return false;
                }

                if (!ConsistencyMiss())
                {
                    failedCase = "consistency-miss";
                    return false;
                }

                if (!PredictionBufferUsesGlobalFrame())
                {
                    failedCase = "prediction-buffer-uses-global-frame";
                    return false;
                }

                if (!SkippedNoRecord())
                {
                    failedCase = "skipped-no-record";
                    return false;
                }

                if (!EvictionDoesNotCrash())
                {
                    failedCase = "eviction-does-not-crash";
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

        private static bool JoinAlignsGlobalFrame()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);
            simulation.SetJoined(7, 120, 0.0f, 0.0f);
            return simulation.InitialAlignedFrame == 123 && simulation.LastAppliedFrame == 120;
        }

        private static bool FrameIndexIsGlobal()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out SentInputRecorder recorder, out _);
            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            return recorder.LastFrameIndex == 11;
        }

        private static bool InputNormalization()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out SentInputRecorder recorder, out _);
            simulation.SetJoined(9, 60, 0.0f, 0.0f);
            simulation.Tick(61, DeterminismRules.FixedDeltaTime, 1.0f, 1.0f);

            float sqrMagnitude = (recorder.LastDx * recorder.LastDx) + (recorder.LastDy * recorder.LastDy);
            return sqrMagnitude <= 1.0001f && sqrMagnitude >= 0.9990f;
        }

        private static bool PredictionMovesSelfPlayer()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            return worldState.TryGetPlayer(1, out PlayerState selfPlayer) && selfPlayer.X > 0.0f;
        }

        private static bool CatchUpTargetIsAuthPlusLead()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.ProcessPong(120);
            simulation.EnqueueServerSnapshot(new BattleWorldSnapshot(
                20,
                new[]
                {
                    new PlayerStateSnapshot(1, 5.0f, 0.0f)
                }));

            TickResult result = simulation.Tick(11, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            int expectedCatchUpFrames = (int)(20 + simulation.LeadFrames - 11);
            return result.SnapshotApplied && result.CatchUpFrames == expectedCatchUpFrames;
        }

        private static bool ConsistencyHit()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(new BattleWorldSnapshot(
                11,
                new[]
                {
                    new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y)
                }));

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return !result.ConsistencyMismatch &&
                   simulation.ConsistencyChecked == 1 &&
                   simulation.ConsistencyHits == 1 &&
                   simulation.ConsistencyMisses == 0;
        }

        private static bool ConsistencyMiss()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(new BattleWorldSnapshot(
                11,
                new[]
                {
                    new PlayerStateSnapshot(1, selfPlayer.X + 1.0f, selfPlayer.Y)
                }));

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return result.ConsistencyMismatch &&
                   simulation.ConsistencyChecked == 1 &&
                   simulation.ConsistencyHits == 0 &&
                   simulation.ConsistencyMisses == 1;
        }

        private static bool PredictionBufferUsesGlobalFrame()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(new BattleWorldSnapshot(
                11,
                new[]
                {
                    new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y)
                }));

            TickResult result = simulation.Tick(99, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return !result.ConsistencyMismatch && simulation.ConsistencyHits == 1;
        }

        private static bool SkippedNoRecord()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.EnqueueServerSnapshot(new BattleWorldSnapshot(
                11,
                new[]
                {
                    new PlayerStateSnapshot(1, 5.0f, 0.0f)
                }));

            simulation.Tick(12, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return simulation.ConsistencySkippedNoRecord == 1 && simulation.ConsistencyChecked == 0;
        }

        private static bool EvictionDoesNotCrash()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            for (uint frame = 11; frame <= 43; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            }

            return simulation.ConsistencySkippedEvicted >= 1;
        }

        private static BattleSimulation CreateSimulation(
            BattleWorldState worldState,
            out SentInputRecorder recorder,
            out PingRecorder pingRecorder)
        {
            recorder = new SentInputRecorder();
            pingRecorder = new PingRecorder();
            return new BattleSimulation(
                worldState,
                recorder.Record,
                pingRecorder.Record);
        }

        private sealed class SentInputRecorder
        {
            public uint LastFrameIndex { get; private set; }
            public float LastDx { get; private set; }
            public float LastDy { get; private set; }

            public void Record(uint frameIndex, uint inputSeq, float dx, float dy)
            {
                LastFrameIndex = frameIndex;
                LastDx = dx;
                LastDy = dy;
            }
        }

        private sealed class PingRecorder
        {
            public ulong LastTimestamp { get; private set; }

            public void Record(ulong sendTimestampMs)
            {
                LastTimestamp = sendTimestampMs;
            }
        }
    }
}
