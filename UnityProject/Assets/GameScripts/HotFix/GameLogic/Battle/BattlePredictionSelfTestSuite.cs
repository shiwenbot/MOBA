using System;
using System.Collections.Generic;
using FixedMathSharp;
using Fantasy.Serialize;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;

namespace GameLogic
{
    public static class BattlePredictionSelfTestSuite
    {
        private const ulong FixedPhysicsExpectedHash = 0xD2120B5F0F5A5FE5UL;

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
            "manual-rollback-replays-authoritative-history",
            "error-smoothing-decays-to-zero",
            "error-smoothing-large-error-snaps",
            "error-smoothing-does-not-affect-logic-state",
            "error-smoothing-overlapping-corrections-rebaseline",
            "error-smoothing-matching-snapshots-decay",
            "fixed-physics-bit-exact",
            "snapshot-raw-roundtrip-bit-exact",
            "hash-report-matches-reported-frame",
            "server-hash-history-detects-mismatch",
            "hash-report-interval-is-30-frames"
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
                    "input-normalization" => InputForwardingPreservesRaw(),
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
                    "error-smoothing-decays-to-zero" => ErrorSmoothingDecaysToZero(),
                    "error-smoothing-large-error-snaps" => ErrorSmoothingLargeErrorSnaps(),
                    "error-smoothing-does-not-affect-logic-state" => ErrorSmoothingDoesNotAffectLogicState(),
                    "error-smoothing-overlapping-corrections-rebaseline" => ErrorSmoothingOverlappingCorrectionsRebaseline(),
                    "error-smoothing-matching-snapshots-decay" => ErrorSmoothingMatchingSnapshotsDecay(),
                    "fixed-physics-bit-exact" => FixedPhysicsBitExact(),
                    "snapshot-raw-roundtrip-bit-exact" => SnapshotRawRoundTripBitExact(),
                    "hash-report-matches-reported-frame" => HashReportMatchesReportedFrame(),
                    "server-hash-history-detects-mismatch" => ServerHashHistoryDetectsMismatch(),
                    "hash-report-interval-is-30-frames" => HashReportIntervalIs30Frames(),
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

        private static bool InputForwardingPreservesRaw()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out SentInputRecorder recorder, out _);
            simulation.SetJoined(9, 60, 0.0f, 0.0f);
            simulation.Tick(61, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.One);

            return recorder.LastDxRaw == Fixed64.One.m_rawValue &&
                   recorder.LastDyRaw == Fixed64.One.m_rawValue;
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
                            new PhysicsBodySnapshot(1, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, true, true),
                            new PhysicsBodySnapshot(2, (Fixed64)5.0f, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, (Fixed64)0.0f, true, true)
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

        private static bool ErrorSmoothingDecaysToZero()
        {
            PredictionErrorSmoother smoother = new PredictionErrorSmoother();
            smoother.SetOffset(1.0f, -2.0f);
            smoother.Advance(PredictionErrorSmoother.SmoothingDurationSeconds + 0.01f);

            return smoother.OffsetX == 0.0f &&
                   smoother.OffsetY == 0.0f &&
                   smoother.RemainingSeconds == 0.0f;
        }

        private static bool ErrorSmoothingLargeErrorSnaps()
        {
            PredictionErrorSmoother smoother = new PredictionErrorSmoother();
            smoother.SetOffset(20.0f, 0.0f);

            return smoother.OffsetX == 0.0f &&
                   smoother.OffsetY == 0.0f &&
                   smoother.RemainingSeconds == 0.0f &&
                   smoother.LastCorrectionMagnitude > PredictionErrorSmoother.MaxSmoothingDistance;
        }

        private static bool ErrorSmoothingDoesNotAffectLogicState()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            Fixed64 authoritativeX = F(2.0f);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            simulation.RecordRenderedSelfPosition(0.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, authoritativeX, Fixed64.Zero)
                    }),
                11);

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return worldState.TryGetPlayer(1, out PlayerState reconciledSelf) &&
                   reconciledSelf.X.m_rawValue == authoritativeX.m_rawValue &&
                   reconciledSelf.Y.m_rawValue == Fixed64.Zero.m_rawValue &&
                   simulation.LastPredictionCorrectionMagnitude > 0.0f &&
                   simulation.PredictionSmoothingRemainingSeconds > 0.0f;
        }

        private static bool ErrorSmoothingOverlappingCorrectionsRebaseline()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            Fixed64 firstAuthoritativeX = F(1.0f);
            Fixed64 secondAuthoritativeX = firstAuthoritativeX + F(0.5f);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.RecordRenderedSelfPosition(4.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, firstAuthoritativeX, Fixed64.Zero)
                    }),
                11);
            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            if (!worldState.TryGetPlayer(1, out PlayerState afterFirstCorrection))
            {
                return false;
            }

            simulation.AdvancePredictionErrorSmoothing(PredictionErrorSmoother.SmoothingDurationSeconds * 0.5f);
            float previousRenderedX = (float)afterFirstCorrection.X + simulation.RenderErrorOffsetX;
            float previousRenderedY = (float)afterFirstCorrection.Y + simulation.RenderErrorOffsetY;
            simulation.RecordRenderedSelfPosition(previousRenderedX, previousRenderedY);

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    12,
                    new[]
                    {
                        new PlayerStateSnapshot(1, secondAuthoritativeX, Fixed64.Zero)
                    }),
                12);
            simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            return NearlyEqual(
                       simulation.RenderErrorOffsetX,
                       previousRenderedX - (float)secondAuthoritativeX) &&
                   NearlyEqual(
                       simulation.RenderErrorOffsetY,
                       previousRenderedY);
        }

        private static bool ErrorSmoothingMatchingSnapshotsDecay()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            simulation.RecordRenderedSelfPosition(1.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, F(2.0f), Fixed64.Zero)
                    }),
                11);
            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            float initialOffset = simulation.RenderErrorOffsetX;
            simulation.AdvancePredictionErrorSmoothing(PredictionErrorSmoother.SmoothingDurationSeconds * 0.5f);
            simulation.RecordRenderedSelfPosition(
                2.0f + simulation.RenderErrorOffsetX,
                simulation.RenderErrorOffsetY);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    12,
                    new[]
                    {
                        new PlayerStateSnapshot(1, F(2.0f), Fixed64.Zero)
                    }),
                12);
            simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            return MathF.Abs(initialOffset) > 0.0f &&
                   MathF.Abs(simulation.RenderErrorOffsetX) < MathF.Abs(initialOffset) &&
                   simulation.PredictionSmoothingRemainingSeconds <= PredictionErrorSmoother.SmoothingDurationSeconds * 0.5f;
        }

        private static bool SnapshotRawRoundTripBitExact()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, F(0.1234567f), F(-0.7654321f));
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                throw new InvalidOperationException("snapshot-raw-roundtrip player missing before step");
            }

            for (uint frame = 1; frame <= 7; frame++)
            {
                MoveSystem.Apply(worldState, player, F(0.73f), F(-0.41f), DeterminismRules.FixedDeltaTimeFixed64);
                worldState.PhysicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
                MoveSystem.SyncFromPhysics(worldState, player);
            }

            BattleWorldSnapshot sourceSnapshot = worldState.TakeSnapshot().WithFrameIndex(7);
            PhysicsBodySnapshot body = sourceSnapshot.PhysicsSnapshot.Bodies[0];
            Fantasy.PlayerSnapshot wirePlayer = new Fantasy.PlayerSnapshot
            {
                PlayerId = player.PlayerId,
                XRaw = player.X.m_rawValue,
                YRaw = player.Y.m_rawValue,
                LatestAcceptedInputFrame = 7,
                LinearVelocityXRaw = body.LinearVelocityX.m_rawValue,
                LinearVelocityYRaw = body.LinearVelocityY.m_rawValue
            };
            Fantasy.S2C_FrameSnapshot wireSnapshot = new Fantasy.S2C_FrameSnapshot
            {
                FrameIndex = sourceSnapshot.FrameIndex
            };
            wireSnapshot.Players.Add(wirePlayer);

            EnsureProtoSerializer();
            byte[] payload = SerializerManager.ProtoBufHelper.Serialize(typeof(Fantasy.S2C_FrameSnapshot), wireSnapshot);
            Fantasy.S2C_FrameSnapshot roundTripped = (Fantasy.S2C_FrameSnapshot)SerializerManager.ProtoBufHelper.Deserialize(
                typeof(Fantasy.S2C_FrameSnapshot),
                payload);
            Fantasy.PlayerSnapshot decodedPlayer = roundTripped.Players[0];

            if (decodedPlayer.XRaw != wirePlayer.XRaw ||
                decodedPlayer.YRaw != wirePlayer.YRaw ||
                decodedPlayer.LinearVelocityXRaw != wirePlayer.LinearVelocityXRaw ||
                decodedPlayer.LinearVelocityYRaw != wirePlayer.LinearVelocityYRaw)
            {
                throw new InvalidOperationException(
                    $"snapshot-raw-roundtrip mismatch " +
                    $"x={wirePlayer.XRaw}/{decodedPlayer.XRaw} " +
                    $"y={wirePlayer.YRaw}/{decodedPlayer.YRaw} " +
                    $"vx={wirePlayer.LinearVelocityXRaw}/{decodedPlayer.LinearVelocityXRaw} " +
                    $"vy={wirePlayer.LinearVelocityYRaw}/{decodedPlayer.LinearVelocityYRaw}");
            }

            if (Fixed64.FromRaw(decodedPlayer.XRaw).m_rawValue != player.X.m_rawValue ||
                Fixed64.FromRaw(decodedPlayer.YRaw).m_rawValue != player.Y.m_rawValue)
            {
                throw new InvalidOperationException(
                    $"snapshot-raw-roundtrip decoded raw differs from source " +
                    $"x={player.X.m_rawValue}/{decodedPlayer.XRaw} " +
                    $"y={player.Y.m_rawValue}/{decodedPlayer.YRaw}");
            }

            Fantasy.C2B_JoinBattleResponse joinWire = new Fantasy.C2B_JoinBattleResponse
            {
                PlayerId = player.PlayerId,
                XRaw = player.X.m_rawValue,
                YRaw = player.Y.m_rawValue,
                ServerFrameIndex = sourceSnapshot.FrameIndex
            };
            byte[] joinPayload = SerializerManager.ProtoBufHelper.Serialize(
                typeof(Fantasy.C2B_JoinBattleResponse),
                joinWire);
            Fantasy.C2B_JoinBattleResponse joinRoundTripped =
                (Fantasy.C2B_JoinBattleResponse)SerializerManager.ProtoBufHelper.Deserialize(
                    typeof(Fantasy.C2B_JoinBattleResponse),
                    joinPayload);
            if (joinRoundTripped.XRaw != joinWire.XRaw ||
                joinRoundTripped.YRaw != joinWire.YRaw ||
                joinRoundTripped.ServerFrameIndex != joinWire.ServerFrameIndex)
            {
                throw new InvalidOperationException(
                    $"join-raw-roundtrip mismatch " +
                    $"x={joinWire.XRaw}/{joinRoundTripped.XRaw} " +
                    $"y={joinWire.YRaw}/{joinRoundTripped.YRaw} " +
                    $"frame={joinWire.ServerFrameIndex}/{joinRoundTripped.ServerFrameIndex}");
            }

            Fantasy.C2B_PlayerInput inputWire = new Fantasy.C2B_PlayerInput
            {
                FrameIndex = sourceSnapshot.FrameIndex + 1u,
                InputSeq = 9,
                DxRaw = 3037000499L,
                DyRaw = -3037000499L,
                SkillId = 0
            };
            byte[] inputPayload = SerializerManager.ProtoBufHelper.Serialize(
                typeof(Fantasy.C2B_PlayerInput),
                inputWire);
            Fantasy.C2B_PlayerInput inputRoundTripped =
                (Fantasy.C2B_PlayerInput)SerializerManager.ProtoBufHelper.Deserialize(
                    typeof(Fantasy.C2B_PlayerInput),
                    inputPayload);
            if (inputRoundTripped.DxRaw != inputWire.DxRaw ||
                inputRoundTripped.DyRaw != inputWire.DyRaw ||
                inputRoundTripped.FrameIndex != inputWire.FrameIndex ||
                inputRoundTripped.InputSeq != inputWire.InputSeq)
            {
                throw new InvalidOperationException(
                    $"input-raw-roundtrip mismatch " +
                    $"dx={inputWire.DxRaw}/{inputRoundTripped.DxRaw} " +
                    $"dy={inputWire.DyRaw}/{inputRoundTripped.DyRaw}");
            }

            return true;
        }

        private static bool HashReportMatchesReportedFrame()
        {
            BattleWorldState worldState = new BattleWorldState();
            SentInputRecorder inputRecorder = new SentInputRecorder();
            HashReportRecorder hashRecorder = new HashReportRecorder();
            BattleSimulation simulation = new BattleSimulation(
                worldState,
                (frameIndex, inputSeq, dx, dy, skillId) => inputRecorder.Record(
                    frameIndex,
                    inputSeq,
                    dx,
                    dy,
                    skillId),
                _ => { },
                hashRecorder.Record);
            simulation.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);

            Dictionary<uint, ulong> hashesByFrame = new Dictionary<uint, ulong>();
            for (uint frame = 1; frame <= 35; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
                BattleWorldSnapshot authoritativeSnapshot = worldState.TakeSnapshot().WithFrameIndex(frame);
                hashesByFrame[frame] = StateHasher.Hash(authoritativeSnapshot);
                simulation.EnqueueServerSnapshot(authoritativeSnapshot, frame);
            }

            if (hashRecorder.Reports.Count != 1)
            {
                throw new InvalidOperationException($"hash-report count={hashRecorder.Reports.Count}");
            }

            (uint frameIndex, ulong reportedHash) = hashRecorder.Reports[0];
            if (frameIndex != 29u)
            {
                throw new InvalidOperationException($"hash-report confirmed-frame={frameIndex}, expected=29");
            }

            if (!hashesByFrame.TryGetValue(frameIndex, out ulong expectedHash))
            {
                throw new InvalidOperationException($"hash-report frame={frameIndex}");
            }

            if (reportedHash != expectedHash)
            {
                throw new InvalidOperationException(
                    $"hash-report frame={frameIndex} " +
                    $"reported=0x{reportedHash:X16} expected=0x{expectedHash:X16}");
            }

            return true;
        }

        private static bool ServerHashHistoryDetectsMismatch()
        {
#if FANTASY_UNITY
            // The shared suite is linked into the Unity client project as well as the server.
            // BattleLogic is server-only; the full probe runs from the server build.
            return true;
#else
            List<string> warnings = new List<string>();
            Dictionary<uint, ulong> hashesByFrame = new Dictionary<uint, ulong>();
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic(logWarning: warnings.Add);
            battleLogic.OnBroadcast = snapshot =>
            {
                hashesByFrame[snapshot.FrameIndex] = StateHasher.Hash(snapshot.ToBattleWorldSnapshot());
            };
            battleLogic.JoinPlayer(1, F(0.25f), F(-0.5f));

            for (uint frame = 0; frame <= 5; frame++)
            {
                battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
            }

            ulong expectedHash = hashesByFrame[3];
            Fantasy.HashReportResult matched = battleLogic.TryCompareReportedHash(1, 3, expectedHash);
            Fantasy.HashReportResult mismatch = battleLogic.TryCompareReportedHash(1, 3, expectedHash ^ 0x1UL);
            Fantasy.HashReportResult noRecord = battleLogic.TryCompareReportedHash(1, 999, expectedHash);

            if (matched != Fantasy.HashReportResult.Matched ||
                mismatch != Fantasy.HashReportResult.Mismatch ||
                noRecord != Fantasy.HashReportResult.NoRecord)
            {
                throw new InvalidOperationException(
                    $"server-hash-history results={matched}/{mismatch}/{noRecord}");
            }

            if (battleLogic.HashReportsMatched != 1 ||
                battleLogic.HashMismatchCount != 1 ||
                battleLogic.HashNoRecordCount != 1)
            {
                throw new InvalidOperationException(
                    $"server-hash-history counts=" +
                    $"{battleLogic.HashReportsMatched}/{battleLogic.HashMismatchCount}/{battleLogic.HashNoRecordCount}");
            }

            if (warnings.Count != 2)
            {
                throw new InvalidOperationException(
                    $"server-hash-history warning-count={warnings.Count}");
            }

            string warning = warnings.Find(value => value.Contains("[Battle][HashMismatch]", StringComparison.Ordinal)) ?? string.Empty;
            if (!warning.Contains("player=1", StringComparison.Ordinal) ||
                !warning.Contains("frame=3", StringComparison.Ordinal) ||
                !warning.Contains($"authoritative=0x{expectedHash:X16}", StringComparison.Ordinal) ||
                !warning.Contains($"reported=0x{(expectedHash ^ 0x1UL):X16}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"server-hash-history warning={warning}");
            }

            string noRecordWarning = warnings.Find(value => value.Contains("[Battle][HashNoRecord]", StringComparison.Ordinal)) ?? string.Empty;
            if (!noRecordWarning.Contains("player=1", StringComparison.Ordinal) ||
                !noRecordWarning.Contains("frame=999", StringComparison.Ordinal) ||
                !noRecordWarning.Contains($"reported=0x{expectedHash:X16}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"server-hash-history no-record-warning={noRecordWarning}");
            }

            return true;
#endif
        }

        private static bool HashReportIntervalIs30Frames()
        {
            BattleWorldState worldState = new BattleWorldState();
            SentInputRecorder inputRecorder = new SentInputRecorder();
            HashReportRecorder hashRecorder = new HashReportRecorder();
            BattleSimulation simulation = new BattleSimulation(
                worldState,
                (frameIndex, inputSeq, dx, dy, skillId) => inputRecorder.Record(
                    frameIndex,
                    inputSeq,
                    dx,
                    dy,
                    skillId),
                _ => { },
                hashRecorder.Record);
            simulation.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);

            for (uint frame = 1; frame <= 100; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.One);
                BattleWorldSnapshot authoritativeSnapshot = worldState.TakeSnapshot().WithFrameIndex(frame);
                simulation.EnqueueServerSnapshot(authoritativeSnapshot, frame);
            }

            if (hashRecorder.Reports.Count != 3)
            {
                throw new InvalidOperationException($"hash-report-interval count={hashRecorder.Reports.Count}");
            }

            uint[] expectedFrames = { 29, 59, 89 };
            for (int i = 0; i < expectedFrames.Length; i++)
            {
                uint actualFrame = hashRecorder.Reports[i].FrameIndex;
                if (actualFrame != expectedFrames[i])
                {
                    throw new InvalidOperationException(
                        $"hash-report-interval index={i} expectedFrame={expectedFrames[i]} actualFrame={actualFrame}");
                }
            }

            return true;
        }

        private static void EnsureProtoSerializer()
        {
            if (SerializerManager.ProtoBufHelper == null)
            {
                SerializerManager.Initialize().GetAwaiter().GetResult();
            }

            if (SerializerManager.ProtoBufHelper == null)
            {
                throw new InvalidOperationException("ProtoBuf serializer is not initialized");
            }
        }

        private static bool FixedPhysicsBitExact()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, -(Fixed64)2, Fixed64.Zero);
            worldState.AddOrUpdatePlayer(2, (Fixed64)2, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState leftPlayer) ||
                !worldState.TryGetPlayer(2, out PlayerState rightPlayer))
            {
                return false;
            }

            for (uint frame = 0; frame < 180; frame++)
            {
                GetFixedPhysicsTestInput(frame, out Fixed64 leftDx, out Fixed64 leftDy, out Fixed64 rightDx, out Fixed64 rightDy);
                MoveSystem.Apply(worldState, leftPlayer, leftDx, leftDy, DeterminismRules.FixedDeltaTimeFixed64);
                MoveSystem.Apply(worldState, rightPlayer, rightDx, rightDy, DeterminismRules.FixedDeltaTimeFixed64);
                if (frame == 90)
                {
                    worldState.PhysicsWorld.ApplyBodyImpulse(
                        1,
                        (Fixed64)3 / (Fixed64)4,
                        Fixed64.One / (Fixed64)4);
                }

                worldState.PhysicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
                MoveSystem.SyncFromPhysics(worldState, leftPlayer);
                MoveSystem.SyncFromPhysics(worldState, rightPlayer);
            }

            ulong actualHash = StateHasher.Hash(worldState.TakeSnapshot());
            if (actualHash == FixedPhysicsExpectedHash)
            {
                return true;
            }

            throw new InvalidOperationException($"FixedPhysicsBitExact actual=0x{actualHash:X16}");
        }

        private static void GetFixedPhysicsTestInput(
            uint frame,
            out Fixed64 leftDx,
            out Fixed64 leftDy,
            out Fixed64 rightDx,
            out Fixed64 rightDy)
        {
            switch ((frame / 30u) % 4u)
            {
                case 0u:
                    leftDx = Fixed64.One;
                    leftDy = Fixed64.Zero;
                    rightDx = -Fixed64.One;
                    rightDy = Fixed64.Zero;
                    return;
                case 1u:
                    leftDx = Fixed64.Zero;
                    leftDy = Fixed64.One;
                    rightDx = Fixed64.Zero;
                    rightDy = -Fixed64.One;
                    return;
                case 2u:
                    leftDx = -Fixed64.One;
                    leftDy = Fixed64.Zero;
                    rightDx = Fixed64.One;
                    rightDy = Fixed64.Zero;
                    return;
                default:
                    leftDx = Fixed64.Zero;
                    leftDy = -Fixed64.One;
                    rightDx = Fixed64.Zero;
                    rightDy = Fixed64.One;
                    return;
            }
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

        private static bool NearlyEqual(float actual, float expected)
        {
            return MathF.Abs(actual - expected) < 0.0001f;
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
            public long LastDxRaw { get; private set; }
            public long LastDyRaw { get; private set; }

            public void Record(uint frameIndex, uint inputSeq, Fixed64 dx, Fixed64 dy, int skillId)
            {
                LastFrameIndex = frameIndex;
                LastDx = (float)dx;
                LastDy = (float)dy;
                LastDxRaw = dx.m_rawValue;
                LastDyRaw = dy.m_rawValue;
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

        private sealed class HashReportRecorder
        {
            public List<(uint FrameIndex, ulong StateHash)> Reports { get; } =
                new List<(uint FrameIndex, ulong StateHash)>();

            public void Record(uint frameIndex, ulong stateHash)
            {
                Reports.Add((frameIndex, stateHash));
            }
        }
    }
}
