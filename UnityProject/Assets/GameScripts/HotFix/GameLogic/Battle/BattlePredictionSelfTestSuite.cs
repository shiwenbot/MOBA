using System;
using FixedMathSharp;
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
            "room-boundary-clamps-player",
            "catchup-target-is-auth-plus-lead",
            "queued-snapshots-apply-in-order",
            "consistency-hit",
            "consistency-miss",
            "prediction-buffer-uses-global-frame",
            "skipped-no-record",
            "eviction-does-not-crash",
            "accepted-input-feedback-raises-lead",
            "authoritative-snapshot-restores-physics-world",
            "authoritative-snapshot-restores-player-attributes",
            "authoritative-snapshot-restores-player-buffs",
            "predicted-buff-consistency-hit",
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
                    "room-boundary-clamps-player" => RoomBoundaryClampsPlayer(),
                    "catchup-target-is-auth-plus-lead" => CatchUpTargetIsAuthPlusLead(),
                    "queued-snapshots-apply-in-order" => QueuedSnapshotsApplyInOrder(),
                    "consistency-hit" => ConsistencyHit(),
                    "consistency-miss" => ConsistencyMiss(),
                    "prediction-buffer-uses-global-frame" => PredictionBufferUsesGlobalFrame(),
                    "skipped-no-record" => SkippedNoRecord(),
                    "eviction-does-not-crash" => EvictionDoesNotCrash(),
                    "accepted-input-feedback-raises-lead" => AcceptedInputFeedbackRaisesLead(),
                    "authoritative-snapshot-restores-physics-world" => AuthoritativeSnapshotRestoresPhysicsWorld(),
                    "authoritative-snapshot-restores-player-attributes" => AuthoritativeSnapshotRestoresPlayerAttributes(),
                    "authoritative-snapshot-restores-player-buffs" => AuthoritativeSnapshotRestoresPlayerBuffs(),
                    "predicted-buff-consistency-hit" => PredictedBuffConsistencyHit(),
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
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            return recorder.LastFrameIndex == 11;
        }

        private static bool InputNormalization()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out SentInputRecorder recorder, out _);
            simulation.SetJoined(9, 60, 0.0f, 0.0f);
            simulation.Tick(61, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.One);

            float sqrMagnitude = (recorder.LastDx * recorder.LastDx) + (recorder.LastDy * recorder.LastDy);
            return sqrMagnitude <= 1.0001f && sqrMagnitude >= 0.9990f;
        }

        private static bool PredictionMovesSelfPlayer()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            return worldState.TryGetPlayer(1, out PlayerState selfPlayer) && selfPlayer.X > Fixed64.Zero;
        }

        private static bool RoomBoundaryClampsPlayer()
        {
            FrameSyncPhysicsWorld physicsWorld = new FrameSyncPhysicsWorld();
            Fixed64 fixedDt = DeterminismRules.FixedDeltaTimeFixed64;
            physicsWorld.EnsureBody(1, GameplayRoomSettings.PlayerMaxX, Fixed64.Zero);
            physicsWorld.SetBodyMovementInput(1, Fixed64.One, Fixed64.Zero);
            physicsWorld.Step(fixedDt);

            return physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot snapshot) &&
                   snapshot.PositionX.m_rawValue == GameplayRoomSettings.PlayerMaxX.m_rawValue &&
                   snapshot.LinearVelocityX.m_rawValue == Fixed64.Zero.m_rawValue;
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

            TickResult result = simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
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
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
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

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
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
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame11))
            {
                return false;
            }

            Fixed64 frame11X = afterFrame11.X;
            Fixed64 frame11Y = afterFrame11.Y;

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
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

            TickResult result = simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
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
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X + Fixed64.One, selfPlayer.Y)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
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
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
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

            TickResult result = simulation.Tick(99, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
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

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return simulation.ConsistencySkippedNoRecord == 1 && simulation.ConsistencyChecked == 0;
        }

        private static bool EvictionDoesNotCrash()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            for (uint frame = 11; frame <= 43; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
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

            TickResult result = simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return result.SnapshotApplied &&
                   simulation.LastServerBufferedInputFrames == 0 &&
                   simulation.LeadFrames > leadBefore;
        }

        private static bool AuthoritativeSnapshotRestoresPhysicsWorld()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            worldState.AddOrUpdatePlayer(2, 5.0f, 0.0f);
            if (!worldState.TryGetPlayer(2, out PlayerState remotePlayer))
            {
                return false;
            }

            MoveSystem.Apply(worldState, remotePlayer, Fixed64.One, Fixed64.Zero, DeterminismRules.FixedDeltaTimeFixed64);
            worldState.PhysicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
            MoveSystem.SyncFromPhysics(worldState, remotePlayer);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 0.0f, 0.0f),
                        new PlayerStateSnapshot(2, 5.0f, 0.0f)
                    },
                    new PhysicsWorldSnapshot(
                        new[]
                        {
                            new PhysicsBodySnapshot(1, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, true, true),
                            new PhysicsBodySnapshot(2, 5.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, true, true)
                        })),
                11);

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            if (!worldState.TryGetPlayer(2, out PlayerState restoredRemotePlayer))
            {
                return false;
            }

            BattleWorldSnapshot localSnapshot = worldState.TakeSnapshot();
            return Near(restoredRemotePlayer.X, F(5.0f)) &&
                   Near(restoredRemotePlayer.Y, Fixed64.Zero) &&
                   TryGetBodySnapshot(localSnapshot, 2, out PhysicsBodySnapshot remoteBody) &&
                   Near(remoteBody.PositionX, F(5.0f)) &&
                   Near(remoteBody.PositionY, Fixed64.Zero) &&
                   Near(remoteBody.LinearVelocityX, Fixed64.Zero) &&
                   Near(remoteBody.LinearVelocityY, Fixed64.Zero);
        }

        private static bool AuthoritativeSnapshotRestoresPlayerAttributes()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            PlayerAttributeSnapshot authoritativeAttributes = new PlayerAttributeSnapshot(80, 120, 35, 60, 22);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y, authoritativeAttributes)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            return result.ConsistencyMismatch &&
                   restoredPlayer.Health == authoritativeAttributes.Health &&
                   restoredPlayer.MaxHealth == authoritativeAttributes.MaxHealth &&
                   restoredPlayer.Mana == authoritativeAttributes.Mana &&
                   restoredPlayer.MaxMana == authoritativeAttributes.MaxMana &&
                   restoredPlayer.Attack == authoritativeAttributes.Attack;
        }

        private static bool AuthoritativeSnapshotRestoresPlayerBuffs()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            PlayerAttributeSnapshot baseAttributes = new PlayerAttributeSnapshot(100, 100, 40, 100, 10);
            NumericModifierSnapshot numeric = new NumericModifierSnapshot(
                baseAttributes,
                new[]
                {
                    new NumericModifier(1, ModifierValueType.Flat, AttributeKind.Attack, 5)
                });
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(
                            1,
                            0.0f,
                            0.0f,
                            new PlayerAttributeSnapshot(100, 100, 40, 100, 15),
                            new[]
                            {
                                new BuffState(1, 5001, 7, 1, 1, 3, 11, BuffFlags.Duration)
                            },
                            2,
                            numeric)
                    }),
                11);

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            return restoredPlayer.ActiveBuffs.Count == 1 &&
                   restoredPlayer.ActiveBuffs[0].BuffId == 5001 &&
                   restoredPlayer.Numeric.Count == 1 &&
                   restoredPlayer.Attack == 15 &&
                   restoredPlayer.NextRuntimeBuffId == 2;
        }

        private static bool PredictedBuffConsistencyHit()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.EnqueueApplyBuff(new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = 7001,
                DurationFrames = 5,
                StackCount = 1,
                FrameIndex = 11,
                Flags = BuffFlags.Duration
            });
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(
                            1,
                            selfPlayer.X,
                            selfPlayer.Y,
                            selfPlayer.CaptureAttributeSnapshot(),
                            selfPlayer.ActiveBuffs,
                            selfPlayer.NextRuntimeBuffId,
                            selfPlayer.Numeric.CaptureSnapshot())
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return !result.ConsistencyMismatch &&
                   simulation.ConsistencyChecked == 1 &&
                   simulation.ConsistencyHits == 1 &&
                   simulation.ConsistencyMisses == 0;
        }

        private static bool RollbackReplaysBeforeNextConsistencyCheck()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame11))
            {
                return false;
            }

            Fixed64 authoritativeFrame11X = PredictNextX(afterFrame11.X);
            Fixed64 authoritativeFrame12X = PredictNextX(authoritativeFrame11X);

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, authoritativeFrame11X, Fixed64.Zero)
                    }),
                11);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    12,
                    new[]
                    {
                        new PlayerStateSnapshot(1, authoritativeFrame12X, Fixed64.Zero)
                    }),
                12);

            TickResult result = simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
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
                   Near(selfPlayer.X, authoritativeFrame12X);
        }

        private static bool ManualRollbackReplaysAuthoritativeHistory()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame11))
            {
                return false;
            }

            Fixed64 authoritativeFrame11X = PredictNextX(afterFrame11.X);
            Fixed64 expectedRestoredX = PredictNextX(authoritativeFrame11X);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, authoritativeFrame11X, Fixed64.Zero)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!result.ConsistencyMismatch)
            {
                return false;
            }

            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            selfPlayer.X = F(99.0f);
            selfPlayer.Y = F(99.0f);

            simulation.RollBack(11);

            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            return simulation.RollbackCount == 2 &&
                   simulation.LastRollbackReplayFrames == 1 &&
                   Near(restoredPlayer.X, expectedRestoredX) &&
                   Near(restoredPlayer.Y, Fixed64.Zero);
        }

        private static bool TryGetBodySnapshot(
            BattleWorldSnapshot snapshot,
            int bodyId,
            out PhysicsBodySnapshot bodySnapshot)
        {
            PhysicsWorldSnapshot physicsSnapshot = snapshot.PhysicsSnapshot;
            if (physicsSnapshot != null)
            {
                for (int i = 0; i < physicsSnapshot.Bodies.Count; i++)
                {
                    PhysicsBodySnapshot candidate = physicsSnapshot.Bodies[i];
                    if (candidate.BodyId == bodyId)
                    {
                        bodySnapshot = candidate;
                        return true;
                    }
                }
            }

            bodySnapshot = default;
            return false;
        }

        private static Fixed64 F(float value)
        {
            return (Fixed64)value;
        }

        private static Fixed64 PredictNextX(Fixed64 startX)
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, startX, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return startX;
            }

            MoveSystem.Apply(worldState, player, Fixed64.One, Fixed64.Zero, DeterminismRules.FixedDeltaTimeFixed64);
            worldState.PhysicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
            MoveSystem.SyncFromPhysics(worldState, player);
            return player.X;
        }

        private static bool Near(Fixed64 actual, Fixed64 expected)
        {
            return FixedMath.Abs(actual - expected) < F(0.0001f);
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
