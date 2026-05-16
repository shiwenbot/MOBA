using System;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;

namespace GameLogic
{
    public static class BattlePredictionSelfTestSuite
    {
        private static readonly string[] AllCaseNames =
        {
            "join-aligns-global-frame",
            "frame-index-is-global",
            "input-normalization",
            "prediction-moves-self-player",
            "catchup-target-is-auth-plus-lead",
            "queued-snapshots-apply-in-order",
            "consistency-hit",
            "consistency-miss",
            "prediction-buffer-uses-global-frame",
            "skipped-no-record",
            "eviction-does-not-crash",
            "accepted-input-feedback-raises-lead",
            "rollback-replays-before-next-consistency-check",
            "manual-rollback-replays-authoritative-history"
        };

        public static bool Run(out string failedCase)
        {
            for (int i = 0; i < AllCaseNames.Length; i++)
            {
                if (!RunCase(AllCaseNames[i], out failedCase))
                {
                    return false;
                }
            }

            failedCase = string.Empty;
            return true;
        }

        public static bool RunCase(string caseName, out string failedCase)
        {
            try
            {
                string normalizedCaseName = caseName?.Trim().ToLowerInvariant() ?? string.Empty;
                bool passed = normalizedCaseName switch
                {
                    "join-aligns-global-frame" => JoinAlignsGlobalFrame(),
                    "frame-index-is-global" => FrameIndexIsGlobal(),
                    "input-normalization" => InputNormalization(),
                    "prediction-moves-self-player" => PredictionMovesSelfPlayer(),
                    "catchup-target-is-auth-plus-lead" => CatchUpTargetIsAuthPlusLead(),
                    "queued-snapshots-apply-in-order" => QueuedSnapshotsApplyInOrder(),
                    "consistency-hit" => ConsistencyHit(),
                    "consistency-miss" => ConsistencyMiss(),
                    "prediction-buffer-uses-global-frame" => PredictionBufferUsesGlobalFrame(),
                    "skipped-no-record" => SkippedNoRecord(),
                    "eviction-does-not-crash" => EvictionDoesNotCrash(),
                    "accepted-input-feedback-raises-lead" => AcceptedInputFeedbackRaisesLead(),
                    "rollback-replays-before-next-consistency-check" => RollbackReplaysBeforeNextConsistencyCheck(),
                    "manual-rollback-replays-authoritative-history" => ManualRollbackReplaysAuthoritativeHistory(),
                    _ => throw new ArgumentException($"Unknown prediction self test case: {caseName}", nameof(caseName))
                };

                failedCase = passed ? string.Empty : normalizedCaseName;
                return passed;
            }
            catch (Exception exception)
            {
                failedCase = $"{exception.GetType().Name}:{exception.Message}";
                return false;
            }
        }

        private static bool JoinAlignsGlobalFrame()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);
            simulation.SetJoined(7, 120, 0.0f, 0.0f);
            return simulation.InitialAlignedFrame == simulation.LastAppliedFrame + simulation.LeadFrames &&
                   simulation.LastAppliedFrame == 120;
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
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    20,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 5.0f, 0.0f)
                    }),
                20);

            TickResult result = simulation.Tick(11, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            int expectedCatchUpFrames = (int)(20 + simulation.LeadFrames - 11);
            uint expectedTargetFrameExclusive = unchecked(20 + simulation.LeadFrames + 1u);
            return result.SnapshotApplied &&
                   result.CatchUpFrames == expectedCatchUpFrames &&
                   result.TargetFrameExclusive == expectedTargetFrameExclusive;
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

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return !result.ConsistencyMismatch &&
                   simulation.ConsistencyChecked == 1 &&
                   simulation.ConsistencyHits == 1 &&
                   simulation.ConsistencyMisses == 0;
        }

        private static bool QueuedSnapshotsApplyInOrder()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame11))
            {
                return false;
            }

            float frame11X = afterFrame11.X;
            float frame11Y = afterFrame11.Y;

            simulation.Tick(12, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame12))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, frame11X, frame11Y)
                    }),
                11);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    12,
                    new[]
                    {
                        new PlayerStateSnapshot(1, afterFrame12.X, afterFrame12.Y)
                    }),
                12);

            TickResult result = simulation.Tick(13, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return result.SnapshotApplied &&
                   !result.ConsistencyMismatch &&
                   simulation.LastAppliedFrame == 12 &&
                   simulation.ConsistencyChecked == 2 &&
                   simulation.ConsistencyHits == 2;
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

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X + 1.0f, selfPlayer.Y)
                    }),
                11);

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

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y)
                    }),
                11);

            TickResult result = simulation.Tick(99, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return !result.ConsistencyMismatch && simulation.ConsistencyHits == 1;
        }

        private static bool SkippedNoRecord()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 5.0f, 0.0f)
                    }),
                11);

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

        private static bool AcceptedInputFeedbackRaisesLead()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);
            simulation.SetJoined(1, 10, 0.0f, 0.0f);

            uint leadBefore = simulation.LeadFrames;
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    20,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 0.0f, 0.0f)
                    }),
                20);

            TickResult result = simulation.Tick(11, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            return result.SnapshotApplied &&
                   simulation.LastServerBufferedInputFrames == 0 &&
                   simulation.LeadFrames > leadBefore;
        }

        private static bool RollbackReplaysBeforeNextConsistencyCheck()
        {
            float step = DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTime;
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            simulation.Tick(12, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, step * 2.0f, 0.0f)
                    }),
                11);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    12,
                    new[]
                    {
                        new PlayerStateSnapshot(1, step * 3.0f, 0.0f)
                    }),
                12);

            TickResult result = simulation.Tick(13, DeterminismRules.FixedDeltaTime, 0.0f, 0.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            return result.SnapshotApplied &&
                   result.ConsistencyMismatch &&
                   simulation.ConsistencyChecked == 2 &&
                   simulation.ConsistencyHits == 1 &&
                   simulation.ConsistencyMisses == 1 &&
                   simulation.RollbackCount == 1 &&
                   simulation.LastRollbackReplayFrames == 1 &&
                   Math.Abs(selfPlayer.X - (step * 3.0f)) < 0.0001f;
        }

        private static bool ManualRollbackReplaysAuthoritativeHistory()
        {
            float step = DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTime;
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, step * 2.0f, 0.0f)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTime, 1.0f, 0.0f);
            if (!result.ConsistencyMismatch)
            {
                return false;
            }

            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            selfPlayer.X = 99.0f;
            selfPlayer.Y = 99.0f;

            simulation.RollBack(11);

            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            return simulation.RollbackCount == 2 &&
                   simulation.LastRollbackReplayFrames == 1 &&
                   Math.Abs(restoredPlayer.X - (step * 3.0f)) < 0.0001f &&
                   Math.Abs(restoredPlayer.Y) < 0.0001f;
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
