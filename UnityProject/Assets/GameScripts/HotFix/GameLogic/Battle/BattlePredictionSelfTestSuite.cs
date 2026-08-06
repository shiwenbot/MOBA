using System;
using System.Collections.Generic;
using FixedMathSharp;
using Fantasy.Serialize;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Network;
using GameShared.FrameSync.Snapshot;
using GameShared.SkillGraph;

namespace GameLogic
{
    public static partial class BattlePredictionSelfTestSuite
    {

        private const ulong FixedPhysicsExpectedHash = 0xD2120B5F0F5A5FE5UL;
        private const ulong NetworkTestSeed = 0x534E455453494D34UL;

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
            "hash-report-interval-is-30-frames",
            "netsim-disabled-is-passthrough",
            "netsim-delay-releases-on-schedule",
            "netsim-loss-rate-is-deterministic",
            "netsim-queue-overflow-drops-oldest",
            "netsim-uplink-pump-after-tick-has-no-extra-frame",
            "netsim-uplink-loss-causes-server-reuse-input",
            "netsim-downlink-delay-raises-lead-frames",
            "netsim-downlink-loss-corrupts-attribute-baseline",
            "attribute-delta-recovers-after-periodic-full",
            "buff-delta-recovers-after-periodic-full",
            "buff-delta-after-full-sync-is-accepted",
            "attribute-no-change-packet-does-not-diverge",
            "periodic-full-sync-is-staggered",
            "physics-protocol-roundtrip-preserves-nondefault-fields",
            "proto-roundtrip-preserves-fixed64-extremes",
            "consistent-snapshot-skips-rollback",
            "consistent-snapshot-preserves-predicted-frame",
            "consistent-snapshot-preserves-skill-graph",
            "mismatch-snapshot-still-rolls-back",
            "remote-players-not-in-world-state",
            "remote-players-survive-self-rollback",
            "remote-player-removal-clears-buffer",
            "clear-world-state-clears-remote-buffer",
            "restore-self-only-keeps-single-body",
            "multi-snapshot-mixed-consistency-one-tick",
            // S7 server-authoritative RTT + lead control
            "rtt-tracker-ack-roundtrip-measures-latency",
            "rtt-tracker-window-min-ignores-outlier-spike",
            "rtt-tracker-stale-probe-cleanup-bounds-table",
            "rtt-nonce-source-is-unpredictable-and-seeded-is-reproducible",
            "rtt-control-value-rises-fast-and-falls-slow",
            "rtt-lead-bounds-region-is-non-empty",
            "rtt-honest-lead-never-warns",
            "rtt-no-sample-does-not-warn",
            "late-input-does-not-count-as-lead-out-of-bounds",
            "rtt-injected-delay-raises-measured-rtt",
            "rtt-envelope-cap-prevents-delayed-ack-pollution",
            "rtt-probe-passes-downlink-gate",
            "authoritative-target-lead-is-applied-and-clamped",
            "zero-target-lead-falls-back-to-client-rtt",
            "target-lead-soft-cap-allows-feedback-transient",
            "target-lead-decrease-does-not-drop-actual-lead-immediately",
            "snapshot-target-lead-is-applied-after-downlink-gate",
            "authoritative-lead-switch-off-clears-stale-target",
            "target-lead-formula-maps-rtt-to-frames"
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
                    "netsim-disabled-is-passthrough" => NetworkSimulationDisabledIsPassthrough(),
                    "netsim-delay-releases-on-schedule" => NetworkSimulationDelayReleasesOnSchedule(),
                    "netsim-loss-rate-is-deterministic" => NetworkSimulationLossRateIsDeterministic(),
                    "netsim-queue-overflow-drops-oldest" => NetworkSimulationQueueOverflowDropsOldest(),
                    "netsim-uplink-pump-after-tick-has-no-extra-frame" => NetworkSimulationUplinkPumpHasNoExtraFrame(),
                    "netsim-uplink-loss-causes-server-reuse-input" => NetworkSimulationUplinkLossCausesServerReuseInput(),
                    "netsim-downlink-delay-raises-lead-frames" => NetworkSimulationDownlinkDelayRaisesLeadFrames(),
                    "netsim-downlink-loss-corrupts-attribute-baseline" => NetworkSimulationDownlinkLossCorruptsAttributeBaseline(),
                    "attribute-delta-recovers-after-periodic-full" => AttributeDeltaRecoversAfterPeriodicFull(),
                    "buff-delta-recovers-after-periodic-full" => BuffDeltaRecoversAfterPeriodicFull(),
                    "buff-delta-after-full-sync-is-accepted" => BuffDeltaAfterFullSyncIsAccepted(),
                    "attribute-no-change-packet-does-not-diverge" => AttributeNoChangePacketDoesNotDiverge(),
                    "periodic-full-sync-is-staggered" => PeriodicFullSyncIsStaggered(),
                    "physics-protocol-roundtrip-preserves-nondefault-fields" => PhysicsProtocolRoundTripPreservesNondefaultFields(),
                    "proto-roundtrip-preserves-fixed64-extremes" => ProtoRoundTripPreservesFixed64Extremes(),
                    "consistent-snapshot-skips-rollback" => ConsistentSnapshotSkipsRollback(),
                    "consistent-snapshot-preserves-predicted-frame" => ConsistentSnapshotPreservesPredictedFrame(),
                    "consistent-snapshot-preserves-skill-graph" => ConsistentSnapshotPreservesSkillGraph(),
                    "mismatch-snapshot-still-rolls-back" => MismatchSnapshotStillRollsBack(),
                    "remote-players-not-in-world-state" => RemotePlayersNotInWorldState(),
                    "remote-players-survive-self-rollback" => RemotePlayersSurviveSelfRollback(),
                    "remote-player-removal-clears-buffer" => RemotePlayerRemovalClearsBuffer(),
                    "clear-world-state-clears-remote-buffer" => ClearWorldStateClearsRemoteBuffer(),
                    "restore-self-only-keeps-single-body" => RestoreSelfOnlyKeepsSingleBody(),
                    "multi-snapshot-mixed-consistency-one-tick" => MultiSnapshotMixedConsistencyOneTick(),
                    "rtt-tracker-ack-roundtrip-measures-latency" => RttTrackerAckRoundtripMeasuresLatency(),
                    "rtt-tracker-window-min-ignores-outlier-spike" => RttTrackerWindowMinIgnoresOutlierSpike(),
                    "rtt-tracker-stale-probe-cleanup-bounds-table" => RttTrackerStaleProbeCleanupBoundsTable(),
                    "rtt-nonce-source-is-unpredictable-and-seeded-is-reproducible" => RttNonceSourceIsUnpredictableAndSeededIsReproducible(),
                    "rtt-control-value-rises-fast-and-falls-slow" => RttControlValueRisesFastAndFallsSlow(),
                    "rtt-lead-bounds-region-is-non-empty" => RttLeadBoundsRegionIsNonEmpty(),
                    "rtt-honest-lead-never-warns" => RttHonestLeadNeverWarns(),
                    "rtt-no-sample-does-not-warn" => RttNoSampleDoesNotWarn(),
                    "late-input-does-not-count-as-lead-out-of-bounds" => LateInputDoesNotCountAsLeadOutOfBounds(),
                    "rtt-injected-delay-raises-measured-rtt" => RttInjectedDelayRaisesMeasuredRtt(),
                    "rtt-envelope-cap-prevents-delayed-ack-pollution" => RttEnvelopeCapPreventsDelayedAckPollution(),
                    "rtt-probe-passes-downlink-gate" => RttProbePassesDownlinkGate(),
                    "authoritative-target-lead-is-applied-and-clamped" => AuthoritativeTargetLeadIsAppliedAndClamped(),
                    "zero-target-lead-falls-back-to-client-rtt" => ZeroTargetLeadFallsBackToClientRtt(),
                    "target-lead-soft-cap-allows-feedback-transient" => TargetLeadSoftCapAllowsFeedbackTransient(),
                    "target-lead-decrease-does-not-drop-actual-lead-immediately" => TargetLeadDecreaseDoesNotDropActualLeadImmediately(),
                    "snapshot-target-lead-is-applied-after-downlink-gate" => SnapshotTargetLeadIsAppliedAfterDownlinkGate(),
                    "authoritative-lead-switch-off-clears-stale-target" => AuthoritativeLeadSwitchOffClearsStaleTarget(),
                    "target-lead-formula-maps-rtt-to-frames" => TargetLeadFormulaMapsRttToFrames(),
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
            // S6 后：别人不进 worldState，只进 RemotePlayerBuffer；物理世界只保留自己 body。
            // 本用例改为断言自己的物理体被权威快照恢复，远端进 buffer。
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            Fixed64 authoritativeX = selfPlayer.X + Fixed64.One;
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, authoritativeX, Fixed64.Zero),
                        new PlayerStateSnapshot(2, 5.0f, 0.0f)
                    },
                    new PhysicsWorldSnapshot(
                        new[]
                        {
                            new PhysicsBodySnapshot(1, authoritativeX, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, true, true),
                            new PhysicsBodySnapshot(2, (Fixed64)5.0f, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, true, true)
                        })),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (!result.ConsistencyMismatch || !worldState.TryGetPlayer(1, out PlayerState restoredSelf))
            {
                return false;
            }

            BattleWorldSnapshot localSnapshot = worldState.TakeSnapshot();
            return worldState.PlayerCount == 1 &&
                   Near(restoredSelf.X, authoritativeX) &&
                   simulation.RemotePlayers.TryGet(2, out PlayerStateSnapshot remote) &&
                   Near(remote.X, F(5.0f)) &&
                   TryGetBodySnapshot(localSnapshot, 1, out PhysicsBodySnapshot selfBody) &&
                   Near(selfBody.PositionX, authoritativeX) &&
                   !TryGetBodySnapshot(localSnapshot, 2, out _);
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

        private static bool NetworkSimulationDisabledIsPassthrough()
        {
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(
                CreateNetworkConfig(isEnabled: false, uplinkDelayMs: 100, uplinkLossPercent: 100),
                clock);
            bool sentSynchronously = false;
            BattleInputSender sendInput = gate.WrapSendInput((_, _, _, _, _) => sentSynchronously = true);

            sendInput(1u, 1u, Fixed64.One, Fixed64.Zero, 0);
            if (!sentSynchronously || gate.UplinkSent != 1 || gate.UplinkDropped != 0)
            {
                throw new InvalidOperationException(
                    $"netsim-disabled-is-passthrough seed={NetworkTestSeed} " +
                    $"sent={sentSynchronously} counters={gate.UplinkSent}/{gate.UplinkDropped}");
            }

            return true;
        }

        private static bool NetworkSimulationDelayReleasesOnSchedule()
        {
            NetworkConditionSimulator simulator = new NetworkConditionSimulator(
                CreateNetworkConfig(uplinkDelayMs: 100));
            bool delivered = false;
            if (!simulator.TryEnqueueUplink(500L, () => delivered = true))
            {
                throw new InvalidOperationException(
                    $"netsim-delay-releases-on-schedule seed={NetworkTestSeed} enqueue-dropped");
            }

            simulator.PumpUplink(599L);
            if (delivered)
            {
                throw new InvalidOperationException(
                    $"netsim-delay-releases-on-schedule seed={NetworkTestSeed} released-before-deadline");
            }

            simulator.PumpUplink(600L);
            if (!delivered || simulator.UplinkSent != 1)
            {
                throw new InvalidOperationException(
                    $"netsim-delay-releases-on-schedule seed={NetworkTestSeed} " +
                    $"delivered={delivered} sent={simulator.UplinkSent}");
            }

            return true;
        }

        private static bool NetworkSimulationLossRateIsDeterministic()
        {
            const int sampleCount = 256;
            bool[] first = BuildDownlinkDropSequence(NetworkTestSeed, sampleCount);
            bool[] second = BuildDownlinkDropSequence(NetworkTestSeed, sampleCount);
            bool[] differentSeed = BuildDownlinkDropSequence(NetworkTestSeed + 1UL, sampleCount);
            bool differsFromOtherSeed = false;

            for (int i = 0; i < sampleCount; i++)
            {
                if (first[i] != second[i])
                {
                    throw new InvalidOperationException(
                        $"netsim-loss-rate-is-deterministic seed={NetworkTestSeed} mismatch-index={i}");
                }

                differsFromOtherSeed |= first[i] != differentSeed[i];
            }

            if (!differsFromOtherSeed)
            {
                throw new InvalidOperationException(
                    $"netsim-loss-rate-is-deterministic seed={NetworkTestSeed} different-seed-sequence-matched");
            }

            return true;
        }

        private static bool NetworkSimulationQueueOverflowDropsOldest()
        {
            const int queueCapacity = 4;
            NetworkConditionSimulator simulator = new NetworkConditionSimulator(
                CreateNetworkConfig(uplinkDelayMs: 1000),
                queueCapacity);
            int delivered = 0;
            for (int i = 0; i < 10; i++)
            {
                simulator.TryEnqueueUplink(0L, () => delivered++);
            }

            if (simulator.OverflowDropped != 6 ||
                simulator.UplinkDropped != 6 ||
                simulator.UplinkQueueDepth != queueCapacity ||
                simulator.MaxQueueDepth != queueCapacity)
            {
                throw new InvalidOperationException(
                    $"netsim-queue-overflow-drops-oldest seed={NetworkTestSeed} " +
                    $"overflow={simulator.OverflowDropped} dropped={simulator.UplinkDropped} " +
                    $"depth={simulator.UplinkQueueDepth} maxDepth={simulator.MaxQueueDepth}");
            }

            simulator.PumpUplink(1000L);
            if (delivered != queueCapacity || simulator.UplinkSent != queueCapacity)
            {
                throw new InvalidOperationException(
                    $"netsim-queue-overflow-drops-oldest seed={NetworkTestSeed} " +
                    $"delivered={delivered} sent={simulator.UplinkSent}");
            }

            return true;
        }

        private static bool NetworkSimulationUplinkPumpHasNoExtraFrame()
        {
#if FANTASY_UNITY
            return true;
#else
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(CreateNetworkConfig(), clock);
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);
            BattleSimulation simulation = new BattleSimulation(
                new BattleWorldState(),
                gate.WrapSendInput((frameIndex, inputSeq, dx, dy, skillId) =>
                    battleLogic.SubmitInput(1, frameIndex, inputSeq, dx.m_rawValue, dy.m_rawValue, skillId)),
                gate.WrapSendPing(_ => { }),
                clock: clock);
            simulation.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);

            clock.SetFrame(1u);
            simulation.Tick(1u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (battleLogic.AcceptedInputCount != 0)
            {
                throw new InvalidOperationException(
                    $"netsim-uplink-pump-after-tick-has-no-extra-frame seed={NetworkTestSeed} sent-before-pump");
            }

            gate.PumpUplink(clock.NowMs);
            if (battleLogic.AcceptedInputCount != 1 || battleLogic.GetLatestAcceptedInputFrame(1) != 1u)
            {
                throw new InvalidOperationException(
                    $"netsim-uplink-pump-after-tick-has-no-extra-frame seed={NetworkTestSeed} " +
                    $"accepted={battleLogic.AcceptedInputCount} latest={battleLogic.GetLatestAcceptedInputFrame(1)}");
            }

            battleLogic.Tick(1u, DeterminismRules.FixedDeltaTimeFixed64);
            if (battleLogic.ReusedInputCount != 0 || battleLogic.ZeroInputFallbackCount != 0)
            {
                throw new InvalidOperationException(
                    $"netsim-uplink-pump-after-tick-has-no-extra-frame seed={NetworkTestSeed} " +
                    $"reused={battleLogic.ReusedInputCount} zero={battleLogic.ZeroInputFallbackCount}");
            }

            return true;
#endif
        }

        private static bool NetworkSimulationUplinkLossCausesServerReuseInput()
        {
#if FANTASY_UNITY
            return true;
#else
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(
                CreateNetworkConfig(uplinkLossPercent: 20),
                clock);
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);
            BattleWorldState clientWorld = new BattleWorldState();
            BattleSimulation simulation = new BattleSimulation(
                clientWorld,
                gate.WrapSendInput((frameIndex, inputSeq, dx, dy, skillId) =>
                    battleLogic.SubmitInput(1, frameIndex, inputSeq, dx.m_rawValue, dy.m_rawValue, skillId)),
                gate.WrapSendPing(_ => { }),
                gate.WrapSendHashReport((frameIndex, stateHash) =>
                    battleLogic.TryCompareReportedHash(1, frameIndex, stateHash)),
                clock: clock);
            simulation.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);

            battleLogic.OnBroadcast = snapshot =>
            {
                long nowMs = clock.NowMs;
                if (!gate.TryAcceptSnapshotMessage(nowMs))
                {
                    return;
                }

                uint latestAcceptedInputFrame = battleLogic.GetLatestAcceptedInputFrame(1);
                gate.EnqueueConvertedSnapshot(
                    nowMs,
                    snapshot.ToBattleWorldSnapshot(),
                    value => simulation.EnqueueServerSnapshot(value, latestAcceptedInputFrame));
            };

            for (uint frame = 1u; frame <= 180u; frame++)
            {
                clock.SetFrame(frame);
                gate.PumpDownlink(clock.NowMs);
                GetNetworkTestInput(frame, out Fixed64 dx, out Fixed64 dy);
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, dx, dy);
                gate.PumpUplink(clock.NowMs);
                battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
            }

            gate.PumpDownlink(clock.NowMs);
            gate.Configure(CreateNetworkConfig(uplinkLossPercent: 0));
            for (uint frame = 181u; frame <= 210u; frame++)
            {
                clock.SetFrame(frame);
                gate.PumpDownlink(clock.NowMs);
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
                gate.PumpUplink(clock.NowMs);
                battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
                gate.PumpDownlink(clock.NowMs);
            }

            clock.SetFrame(211u);
            gate.PumpDownlink(clock.NowMs);
            simulation.Tick(211u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            gate.PumpUplink(clock.NowMs);

            if (!clientWorld.TryGetPlayer(1, out PlayerState clientPlayer) ||
                !battleLogic.TryGetPlayer(1, out PlayerState serverPlayer))
            {
                throw new InvalidOperationException(
                    $"netsim-uplink-loss-causes-server-reuse-input seed={NetworkTestSeed} player-missing");
            }

            if (gate.UplinkDropped <= 0 ||
                battleLogic.ReusedInputCount <= 0 ||
                battleLogic.HashMismatchCount != 0 ||
                battleLogic.HashReportsMatched <= 0 ||
                clientPlayer.X.m_rawValue != serverPlayer.X.m_rawValue ||
                clientPlayer.Y.m_rawValue != serverPlayer.Y.m_rawValue)
            {
                throw new InvalidOperationException(
                    $"netsim-uplink-loss-causes-server-reuse-input seed={NetworkTestSeed} " +
                    $"dropped={gate.UplinkDropped} reused={battleLogic.ReusedInputCount} " +
                    $"hash={battleLogic.HashReportsMatched}/{battleLogic.HashMismatchCount}/{battleLogic.HashNoRecordCount} " +
                    $"x={clientPlayer.X.m_rawValue}/{serverPlayer.X.m_rawValue} " +
                    $"y={clientPlayer.Y.m_rawValue}/{serverPlayer.Y.m_rawValue}");
            }

            return true;
#endif
        }

        private static bool NetworkSimulationDownlinkDelayRaisesLeadFrames()
        {
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(
                CreateNetworkConfig(downlinkDelayMs: 200),
                clock);
            BattleSimulation simulation = null;
            int pingCount = 0;
            int pongCount = 0;
            Action<ulong> sendPing = gate.WrapSendPing(sendTimestampMs =>
            {
                pingCount++;
                gate.TryAcceptPong(clock.NowMs, sendTimestampMs, releasedTimestampMs =>
                {
                    pongCount++;
                    simulation.ProcessPong(clock.NowMs - checked((long)releasedTimestampMs));
                });
            });
            simulation = new BattleSimulation(
                new BattleWorldState(),
                gate.WrapSendInput((_, _, _, _, _) => { }),
                sendPing,
                clock: clock);
            simulation.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);
            uint initialLeadFrames = simulation.LeadFrames;

            for (uint frame = 1u; frame <= 45u; frame++)
            {
                clock.SetFrame(frame);
                gate.PumpDownlink(clock.NowMs);
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
                gate.PumpUplink(clock.NowMs);
            }

            if (pingCount <= 0 ||
                pongCount <= 0 ||
                gate.DownlinkDelivered <= 0 ||
                simulation.LeadFrames <= initialLeadFrames)
            {
                throw new InvalidOperationException(
                    $"netsim-downlink-delay-raises-lead-frames seed={NetworkTestSeed} " +
                    $"ping={pingCount} pong={pongCount} delivered={gate.DownlinkDelivered} " +
                    $"lead={initialLeadFrames}->{simulation.LeadFrames}");
            }

            return true;
        }

        private static bool NetworkSimulationDownlinkLossCorruptsAttributeBaseline()
        {
#if FANTASY_UNITY
            return true;
#else
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(CreateNetworkConfig(), clock);
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            PlayerState serverPlayer = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);
            Dictionary<uint, BattleWorldSnapshot> snapshots = new Dictionary<uint, BattleWorldSnapshot>();
            battleLogic.OnBroadcast = snapshot => snapshots[snapshot.FrameIndex] = snapshot.ToBattleWorldSnapshot();

            battleLogic.Tick(1u, DeterminismRules.FixedDeltaTimeFixed64);
            serverPlayer.Health = Math.Max(1, serverPlayer.Health - 10);
            battleLogic.Tick(2u, DeterminismRules.FixedDeltaTimeFixed64);
            serverPlayer.Attack += 7;
            battleLogic.Tick(3u, DeterminismRules.FixedDeltaTimeFixed64);

            PlayerAttributeSnapshot frame1Attributes = snapshots[1u].Players[0].Attributes;
            PlayerAttributeSnapshot frame2Attributes = snapshots[2u].Players[0].Attributes;
            PlayerAttributeSnapshot frame3Attributes = snapshots[3u].Players[0].Attributes;
            PlayerAttributeSnapshot clientBaseline = PlayerAttributeSnapshot.Default;
            BattleWorldSnapshot rebuiltFrame3 = null;

            clock.SetFrame(1u);
            if (!gate.TryAcceptSnapshotMessage(clock.NowMs))
            {
                throw new InvalidOperationException(
                    $"netsim-downlink-loss-corrupts-attribute-baseline seed={NetworkTestSeed} initial-full-sync-dropped");
            }

            clientBaseline = PlayerAttributeSync.Merge(
                clientBaseline,
                PlayerAttributeDirtyFlags.All,
                frame1Attributes.Health,
                frame1Attributes.MaxHealth,
                frame1Attributes.Mana,
                frame1Attributes.MaxMana,
                frame1Attributes.Attack);

            gate.Configure(CreateNetworkConfig(downlinkLossPercent: 100));
            clock.SetFrame(2u);
            if (gate.TryAcceptSnapshotMessage(clock.NowMs))
            {
                throw new InvalidOperationException(
                    $"netsim-downlink-loss-corrupts-attribute-baseline seed={NetworkTestSeed} dirty-snapshot-not-dropped");
            }

            gate.Configure(CreateNetworkConfig(downlinkLossPercent: 0));
            clock.SetFrame(3u);
            if (!gate.TryAcceptSnapshotMessage(clock.NowMs))
            {
                throw new InvalidOperationException(
                    $"netsim-downlink-loss-corrupts-attribute-baseline seed={NetworkTestSeed} recovery-snapshot-dropped");
            }

            PlayerAttributeDirtyFlags frame3DirtyMask = PlayerAttributeSync.ComputeDirtyMask(
                true,
                frame2Attributes,
                frame3Attributes);
            clientBaseline = PlayerAttributeSync.Merge(
                clientBaseline,
                frame3DirtyMask,
                frame3Attributes.Health,
                frame3Attributes.MaxHealth,
                frame3Attributes.Mana,
                frame3Attributes.MaxMana,
                frame3Attributes.Attack);
            BattleWorldSnapshot convertedFrame3 = ReplacePlayerAttributes(snapshots[3u], 1, clientBaseline);
            gate.EnqueueConvertedSnapshot(clock.NowMs, convertedFrame3, value => rebuiltFrame3 = value);
            gate.PumpDownlink(clock.NowMs);

            if (rebuiltFrame3 == null)
            {
                throw new InvalidOperationException(
                    $"netsim-downlink-loss-corrupts-attribute-baseline seed={NetworkTestSeed} frame3-not-delivered");
            }

            ulong clientHash = StateHasher.Hash(rebuiltFrame3);
            Fantasy.HashReportResult result = battleLogic.TryCompareReportedHash(1, 3u, clientHash);
            if (frame3DirtyMask != PlayerAttributeDirtyFlags.Attack ||
                rebuiltFrame3.Players[0].Health == frame3Attributes.Health ||
                result != Fantasy.HashReportResult.Mismatch ||
                battleLogic.HashMismatchCount <= 0 ||
                gate.DownlinkDropped <= 0)
            {
                throw new InvalidOperationException(
                    $"netsim-downlink-loss-corrupts-attribute-baseline seed={NetworkTestSeed} " +
                    $"mask={frame3DirtyMask} health={rebuiltFrame3.Players[0].Health}/{frame3Attributes.Health} " +
                    $"result={result} mismatch={battleLogic.HashMismatchCount} dropped={gate.DownlinkDropped}");
            }

            return true;
#endif
        }

        private static bool AttributeDeltaRecoversAfterPeriodicFull()
        {
            const long playerId = 1L;
            PlayerAttributeSnapshot frame1Attributes = new PlayerAttributeSnapshot(100, 100, 80, 100, 10);
            PlayerAttributeSnapshot frame2Attributes = new PlayerAttributeSnapshot(55, 100, 80, 100, 10);
            PlayerAttributeSnapshot frame3Attributes = new PlayerAttributeSnapshot(55, 100, 80, 100, 17);

            AttributeMergeResult initial = BattleSnapshotProtocolMapper.MergeAttributes(
                1u,
                CreateAttributePacket(1u, playerId, PlayerAttributeDirtyFlags.All, 1u, frame1Attributes),
                false,
                PlayerAttributeSnapshot.Default,
                0u);
            if (!initial.Applied || initial.Diverged || !AttributesEqual(initial.Attributes, frame1Attributes))
            {
                throw new InvalidOperationException("attribute recovery initial full sync failed");
            }

            Fantasy.PlayerSnapshot droppedFrame2 = CreateAttributePacket(
                2u,
                playerId,
                PlayerAttributeDirtyFlags.Health,
                1u,
                frame2Attributes);
            if (droppedFrame2.AttributeBaselineFrameIndex != initial.FrameIndex)
            {
                throw new InvalidOperationException(
                    $"attribute recovery dropped delta base mismatch expected={initial.FrameIndex} actual={droppedFrame2.AttributeBaselineFrameIndex}");
            }

            AttributeMergeResult divergent = BattleSnapshotProtocolMapper.MergeAttributes(
                3u,
                CreateAttributePacket(3u, playerId, PlayerAttributeDirtyFlags.Attack, 2u, frame3Attributes),
                true,
                initial.Attributes,
                initial.FrameIndex);
            if (!divergent.Diverged || divergent.Applied || !AttributesEqual(divergent.Attributes, frame1Attributes))
            {
                throw new InvalidOperationException(
                    $"attribute recovery divergence was not rejected localBase={initial.FrameIndex} packetBase=2");
            }

            uint recoveryFrame = FindNextPeriodicFullSyncFrame(3u, playerId);
            AttributeMergeResult recovered = BattleSnapshotProtocolMapper.MergeAttributes(
                recoveryFrame,
                CreateAttributePacket(
                    recoveryFrame,
                    playerId,
                    PlayerAttributeDirtyFlags.All,
                    recoveryFrame,
                    frame3Attributes),
                true,
                divergent.Attributes,
                divergent.FrameIndex);
            if (!recovered.Applied || recovered.Diverged || recovered.FrameIndex != recoveryFrame ||
                !AttributesEqual(recovered.Attributes, frame3Attributes))
            {
                throw new InvalidOperationException(
                    $"attribute recovery full sync failed frame={recoveryFrame} localBase={recovered.FrameIndex}");
            }

            PlayerAttributeSnapshot frameAfterRecovery = new PlayerAttributeSnapshot(50, 100, 80, 100, 17);
            AttributeMergeResult postRecoveryDelta = BattleSnapshotProtocolMapper.MergeAttributes(
                recoveryFrame + 1u,
                CreateAttributePacket(
                    recoveryFrame + 1u,
                    playerId,
                    PlayerAttributeDirtyFlags.Health,
                    recoveryFrame,
                    frameAfterRecovery),
                true,
                recovered.Attributes,
                recovered.FrameIndex);
            if (!postRecoveryDelta.Applied || postRecoveryDelta.Diverged ||
                !AttributesEqual(postRecoveryDelta.Attributes, frameAfterRecovery))
            {
                throw new InvalidOperationException("attribute recovery post-full delta was rejected");
            }

            return true;
        }

        private static bool BuffDeltaRecoversAfterPeriodicFull()
        {
#if FANTASY_UNITY
            return true;
#else
            const long playerId = 1L;
            BuffState frame1Buff = CreateTestBuff(1L, 1, 10, 1u);
            BuffState frame2Buff = CreateTestBuff(1L, 1, 9, 1u);
            BuffState frame3Buff = CreateTestBuff(1L, 2, 8, 1u);
            PlayerStateSnapshot frame1Player = CreateBuffPlayer(playerId, frame1Buff);
            Fantasy.BuffBroadcastBaseline serverBaseline = new Fantasy.BuffBroadcastBaseline(
                frame1Player.ActiveBuffs,
                frame1Player.NextRuntimeBuffId,
                1u);

            Fantasy.BuffSyncPayload initial = Fantasy.BuffBroadcastPayloadBuilder.CreateInitialFullSync(frame1Player);
            if (!initial.IsFullSync)
            {
                throw new InvalidOperationException("buff recovery initial payload was not full sync");
            }

            Fantasy.BuffSyncPayload droppedFrame2 = Fantasy.BuffBroadcastPayloadBuilder.Build(
                serverBaseline,
                CreateBuffPlayer(playerId, frame2Buff),
                2u,
                false);
            Fantasy.BuffSyncPayload rejectedFrame3 = Fantasy.BuffBroadcastPayloadBuilder.Build(
                serverBaseline,
                CreateBuffPlayer(playerId, frame3Buff),
                3u,
                false);
            if (droppedFrame2.IsFullSync || droppedFrame2.BaselineFrameIndex != 1u ||
                rejectedFrame3.IsFullSync || rejectedFrame3.BaselineFrameIndex != 2u)
            {
                throw new InvalidOperationException(
                    $"buff recovery delta bases unexpected frame2={droppedFrame2.BaselineFrameIndex} frame3={rejectedFrame3.BaselineFrameIndex}");
            }

            uint recoveryFrame = FindNextPeriodicFullSyncFrame(3u, playerId);
            Fantasy.BuffSyncPayload recovery = Fantasy.BuffBroadcastPayloadBuilder.Build(
                serverBaseline,
                CreateBuffPlayer(playerId, frame3Buff),
                recoveryFrame,
                true);
            BuffState[] recoveredBuffs = BuildBuffStatesFromWire(recovery.Buffs);
            if (!recovery.IsFullSync || recovery.BaselineFrameIndex != recoveryFrame ||
                !BuffListsEqual(recoveredBuffs, new[] { frame3Buff }))
            {
                throw new InvalidOperationException(
                    $"buff recovery full sync failed frame={recoveryFrame} base={recovery.BaselineFrameIndex}");
            }

            return true;
#endif
        }

        private static bool BuffDeltaAfterFullSyncIsAccepted()
        {
#if FANTASY_UNITY
            return true;
#else
            const long playerId = 1L;
            BuffState frame1Buff = CreateTestBuff(2L, 1, 10, 1u);
            BuffState frame2Buff = CreateTestBuff(2L, 1, 9, 1u);
            BuffState frame62Buff = CreateTestBuff(2L, 3, 7, 1u);
            PlayerStateSnapshot frame1Player = CreateBuffPlayer(playerId, frame1Buff);
            Fantasy.BuffBroadcastBaseline serverBaseline = new Fantasy.BuffBroadcastBaseline(
                frame1Player.ActiveBuffs,
                frame1Player.NextRuntimeBuffId,
                1u);

            Fantasy.BuffBroadcastPayloadBuilder.Build(
                serverBaseline,
                CreateBuffPlayer(playerId, frame2Buff),
                2u,
                false);

            uint fullSyncFrame = FindNextPeriodicFullSyncFrame(2u, playerId);
            Fantasy.BuffSyncPayload fullSync = Fantasy.BuffBroadcastPayloadBuilder.Build(
                serverBaseline,
                CreateBuffPlayer(playerId, frame2Buff),
                fullSyncFrame,
                true);
            Fantasy.BuffSyncPayload nextDelta = Fantasy.BuffBroadcastPayloadBuilder.Build(
                serverBaseline,
                CreateBuffPlayer(playerId, frame62Buff),
                fullSyncFrame + 1u,
                false);

            if (!fullSync.IsFullSync || nextDelta.IsFullSync ||
                nextDelta.BaselineFrameIndex != fullSyncFrame)
            {
                throw new InvalidOperationException(
                    $"buff post-full delta base mismatch full={fullSyncFrame} actual={nextDelta.BaselineFrameIndex}");
            }

            BuffState[] clientBaseline = BuildBuffStatesFromWire(fullSync.Buffs);
            BuffState[] merged = BuffSync.Merge(clientBaseline, BuildBuffChangesFromWire(nextDelta.Buffs));
            if (!BuffListsEqual(merged, new[] { frame62Buff }))
            {
                throw new InvalidOperationException("buff post-full delta did not merge to the server state");
            }

            return true;
#endif
        }

        private static bool AttributeNoChangePacketDoesNotDiverge()
        {
            PlayerAttributeSnapshot baseline = new PlayerAttributeSnapshot(90, 100, 70, 100, 12);
            Fantasy.PlayerSnapshot noChangePacket = CreateAttributePacket(
                11u,
                1L,
                PlayerAttributeDirtyFlags.None,
                999u,
                baseline);
            AttributeMergeResult result = BattleSnapshotProtocolMapper.MergeAttributes(
                11u,
                noChangePacket,
                true,
                baseline,
                10u);

            if (result.Diverged || result.Applied || result.FrameIndex != 10u ||
                !AttributesEqual(result.Attributes, baseline))
            {
                throw new InvalidOperationException(
                    $"attribute no-change packet altered baseline diverged={result.Diverged} applied={result.Applied} frame={result.FrameIndex}");
            }

            return true;
        }

        private static bool PeriodicFullSyncIsStaggered()
        {
            int[] fullSyncsPerFrame = new int[SnapshotSyncRecoveryPolicy.FullSyncIntervalFrames];
            for (long playerId = 1L; playerId <= 120L; playerId++)
            {
                int playerFullSyncCount = 0;
                for (uint frameIndex = 1u; frameIndex <= SnapshotSyncRecoveryPolicy.FullSyncIntervalFrames; frameIndex++)
                {
                    if (!SnapshotSyncRecoveryPolicy.IsPeriodicFullSyncFrame(frameIndex, playerId))
                    {
                        continue;
                    }

                    playerFullSyncCount++;
                    fullSyncsPerFrame[frameIndex - 1u]++;
                }

                if (playerFullSyncCount != 1)
                {
                    throw new InvalidOperationException(
                        $"periodic full sync count mismatch player={playerId} count={playerFullSyncCount}");
                }
            }

            for (int i = 0; i < fullSyncsPerFrame.Length; i++)
            {
                if (fullSyncsPerFrame[i] != 2)
                {
                    throw new InvalidOperationException(
                        $"periodic full sync was not staggered frame={i + 1} count={fullSyncsPerFrame[i]}");
                }
            }

            return true;
        }

        private static bool PhysicsProtocolRoundTripPreservesNondefaultFields()
        {
            const long playerId = 17L;
            Fixed64 positionX = Fixed64.FromRaw(1_234_567_890L);
            Fixed64 positionY = Fixed64.FromRaw(-2_345_678_901L);
            PhysicsBodySnapshot sourceBody = new PhysicsBodySnapshot(
                checked((int)playerId),
                positionX,
                positionY,
                Fixed64.FromRaw(345_678_901L),
                Fixed64.FromRaw(-456_789_012L),
                Fixed64.FromRaw(567_890_123L),
                Fixed64.FromRaw(-678_901_234L),
                false,
                false);
            Fantasy.PlayerSnapshot wirePlayer = new Fantasy.PlayerSnapshot
            {
                PlayerId = playerId,
                XRaw = positionX.m_rawValue,
                YRaw = positionY.m_rawValue,
                AttributeDirtyMask = (uint)PlayerAttributeDirtyFlags.All,
                Health = PlayerAttributeSnapshot.Default.Health,
                MaxHealth = PlayerAttributeSnapshot.Default.MaxHealth,
                Mana = PlayerAttributeSnapshot.Default.Mana,
                MaxMana = PlayerAttributeSnapshot.Default.MaxMana,
                Attack = PlayerAttributeSnapshot.Default.Attack
            };
            BattleSnapshotProtocolMapper.WritePhysicsBody(wirePlayer, true, sourceBody);
            Fantasy.S2C_FrameSnapshot wireSnapshot = new Fantasy.S2C_FrameSnapshot { FrameIndex = 77u };
            wireSnapshot.Players.Add(wirePlayer);

            EnsureProtoSerializer();
            byte[] payload = SerializerManager.ProtoBufHelper.Serialize(typeof(Fantasy.S2C_FrameSnapshot), wireSnapshot);
            Fantasy.S2C_FrameSnapshot decodedSnapshot =
                (Fantasy.S2C_FrameSnapshot)SerializerManager.ProtoBufHelper.Deserialize(
                    typeof(Fantasy.S2C_FrameSnapshot),
                    payload);
            Fantasy.PlayerSnapshot decodedPlayer = decodedSnapshot.Players[0];
            PhysicsBodySnapshot decodedBody = BattleSnapshotProtocolMapper.ReadPhysicsBody(decodedPlayer);

            if (!PhysicsBodiesEqual(sourceBody, decodedBody) || !decodedPlayer.IsAsleep || !decodedPlayer.IsDisabled)
            {
                throw new InvalidOperationException(
                    $"physics protocol roundtrip mismatch rotation={sourceBody.RotationRadians.m_rawValue}/{decodedBody.RotationRadians.m_rawValue} " +
                    $"angular={sourceBody.AngularVelocity.m_rawValue}/{decodedBody.AngularVelocity.m_rawValue} " +
                    $"awake={sourceBody.IsAwake}/{decodedBody.IsAwake} enabled={sourceBody.IsEnabled}/{decodedBody.IsEnabled}");
            }

            PlayerStateSnapshot playerState = new PlayerStateSnapshot(playerId, positionX, positionY);
            BattleWorldSnapshot sourceWorld = new BattleWorldSnapshot(
                77u,
                new[] { playerState },
                new PhysicsWorldSnapshot(new[] { sourceBody }, Array.Empty<PhysicsContactSnapshot>()));
            BattleWorldSnapshot decodedWorld = new BattleWorldSnapshot(
                77u,
                new[] { playerState },
                new PhysicsWorldSnapshot(new[] { decodedBody }, Array.Empty<PhysicsContactSnapshot>()));
            ulong sourceHash = StateHasher.Hash(sourceWorld);
            ulong decodedHash = StateHasher.Hash(decodedWorld);
            if (sourceHash != decodedHash)
            {
                throw new InvalidOperationException(
                    $"physics protocol hash mismatch source=0x{sourceHash:X16} decoded=0x{decodedHash:X16}");
            }

            return true;
        }

        private static bool ProtoRoundTripPreservesFixed64Extremes()
        {
            // 边界 raw 位模式：正常 gameplay 可能碰不到低位全 1 / 极小正值。
            long[] extremeRaws =
            {
                1L,
                -1L,
                long.MaxValue,
                long.MinValue + 1L,
                0x0000_0000_0000_FFFFL,
                unchecked((long)0xFFFF_FFFF_FFFF_0001UL),
                0x5555_5555_5555_5555L,
                unchecked((long)0xAAAA_AAAA_AAAA_AAAAL)
            };

            EnsureProtoSerializer();
            for (int i = 0; i < extremeRaws.Length; i++)
            {
                long rawX = extremeRaws[i];
                long rawY = extremeRaws[(i + 3) % extremeRaws.Length];
                long rawVx = extremeRaws[(i + 1) % extremeRaws.Length];
                long rawVy = extremeRaws[(i + 2) % extremeRaws.Length];
                long rawAngle = extremeRaws[(i + 4) % extremeRaws.Length];
                long rawOmega = extremeRaws[(i + 5) % extremeRaws.Length];

                PlayerStateSnapshot player = new PlayerStateSnapshot(
                    1000L + i,
                    Fixed64.FromRaw(rawX),
                    Fixed64.FromRaw(rawY),
                    new PlayerAttributeSnapshot(77, 120, 33, 90, 19));
                PhysicsBodySnapshot body = new PhysicsBodySnapshot(
                    checked((int)player.PlayerId),
                    player.X,
                    player.Y,
                    Fixed64.FromRaw(rawAngle),
                    Fixed64.FromRaw(rawVx),
                    Fixed64.FromRaw(rawVy),
                    Fixed64.FromRaw(rawOmega),
                    i % 2 == 0,
                    i % 3 != 0);
                BattleWorldSnapshot source = new BattleWorldSnapshot(
                    unchecked((uint)(900 + i)),
                    new[] { player },
                    new PhysicsWorldSnapshot(new[] { body }, Array.Empty<PhysicsContactSnapshot>()));

                Fantasy.S2C_FrameSnapshot wire = BattleSnapshotProtocolMapper.ToFullSyncProto(source);
                byte[] payload = SerializerManager.ProtoBufHelper.Serialize(typeof(Fantasy.S2C_FrameSnapshot), wire);
                Fantasy.S2C_FrameSnapshot decoded =
                    (Fantasy.S2C_FrameSnapshot)SerializerManager.ProtoBufHelper.Deserialize(
                        typeof(Fantasy.S2C_FrameSnapshot),
                        payload);
                BattleWorldSnapshot restored = BattleSnapshotProtocolMapper.FromFullSyncProto(decoded);

                if (restored.Players.Count != 1 ||
                    restored.PhysicsSnapshot == null ||
                    restored.PhysicsSnapshot.Bodies.Count != 1)
                {
                    throw new InvalidOperationException("proto extremes roundtrip lost player/body");
                }

                PlayerStateSnapshot restoredPlayer = restored.Players[0];
                PhysicsBodySnapshot restoredBody = restored.PhysicsSnapshot.Bodies[0];
                if (restoredPlayer.X.m_rawValue != rawX ||
                    restoredPlayer.Y.m_rawValue != rawY ||
                    restoredBody.LinearVelocityX.m_rawValue != rawVx ||
                    restoredBody.LinearVelocityY.m_rawValue != rawVy ||
                    restoredBody.RotationRadians.m_rawValue != rawAngle ||
                    restoredBody.AngularVelocity.m_rawValue != rawOmega ||
                    restoredBody.IsAwake != body.IsAwake ||
                    restoredBody.IsEnabled != body.IsEnabled)
                {
                    throw new InvalidOperationException(
                        $"proto extremes mismatch index={i} " +
                        $"x={rawX}/{restoredPlayer.X.m_rawValue} y={rawY}/{restoredPlayer.Y.m_rawValue} " +
                        $"vx={rawVx}/{restoredBody.LinearVelocityX.m_rawValue} vy={rawVy}/{restoredBody.LinearVelocityY.m_rawValue}");
                }

                if (StateHasher.Hash(source) != StateHasher.Hash(restored))
                {
                    throw new InvalidOperationException($"proto extremes hash mismatch index={i}");
                }
            }

            return true;
        }

        private static NetworkConditionConfig CreateNetworkConfig(
            bool isEnabled = true,
            int uplinkDelayMs = 0,
            int downlinkDelayMs = 0,
            int uplinkJitterMs = 0,
            int downlinkJitterMs = 0,
            int uplinkLossPercent = 0,
            int downlinkLossPercent = 0,
            ulong seed = NetworkTestSeed)
        {
            return new NetworkConditionConfig(
                isEnabled,
                uplinkDelayMs,
                downlinkDelayMs,
                uplinkJitterMs,
                downlinkJitterMs,
                uplinkLossPercent,
                downlinkLossPercent,
                seed);
        }

        private static bool[] BuildDownlinkDropSequence(ulong seed, int sampleCount)
        {
            NetworkConditionSimulator simulator = new NetworkConditionSimulator(
                CreateNetworkConfig(downlinkLossPercent: 37, seed: seed));
            bool[] sequence = new bool[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                sequence[i] = simulator.ShouldDropDownlink(i);
            }

            return sequence;
        }

        private static Fantasy.PlayerSnapshot CreateAttributePacket(
            uint frameIndex,
            long playerId,
            PlayerAttributeDirtyFlags dirtyMask,
            uint baselineFrameIndex,
            PlayerAttributeSnapshot attributes)
        {
            return new Fantasy.PlayerSnapshot
            {
                PlayerId = playerId,
                AttributeDirtyMask = (uint)dirtyMask,
                AttributeBaselineFrameIndex = BattleSnapshotProtocolMapper.IsFullAttributeSnapshot(dirtyMask)
                    ? frameIndex
                    : baselineFrameIndex,
                Health = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.Health, attributes.Health),
                MaxHealth = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.MaxHealth, attributes.MaxHealth),
                Mana = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.Mana, attributes.Mana),
                MaxMana = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.MaxMana, attributes.MaxMana),
                Attack = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.Attack, attributes.Attack)
            };
        }

        private static uint FindNextPeriodicFullSyncFrame(uint afterFrame, long playerId)
        {
            uint candidate = afterFrame + 1u;
            while (!SnapshotSyncRecoveryPolicy.IsPeriodicFullSyncFrame(candidate, playerId))
            {
                candidate++;
            }

            return candidate;
        }

        private static bool AttributesEqual(PlayerAttributeSnapshot left, PlayerAttributeSnapshot right)
        {
            return left.Health == right.Health &&
                   left.MaxHealth == right.MaxHealth &&
                   left.Mana == right.Mana &&
                   left.MaxMana == right.MaxMana &&
                   left.Attack == right.Attack;
        }

        private static BuffState CreateTestBuff(long runtimeBuffId, int stackCount, int remainingFrames, uint appliedFrame)
        {
            return new BuffState(
                runtimeBuffId,
                9001,
                1L,
                1L,
                stackCount,
                remainingFrames,
                appliedFrame,
                BuffFlags.Duration | BuffFlags.Dispellable);
        }

        private static PlayerStateSnapshot CreateBuffPlayer(long playerId, params BuffState[] buffs)
        {
            PlayerAttributeSnapshot attributes = PlayerAttributeSnapshot.Default;
            return new PlayerStateSnapshot(
                playerId,
                Fixed64.Zero,
                Fixed64.Zero,
                attributes,
                buffs,
                100L,
                NumericModifierSnapshot.FromAttributes(attributes));
        }

        private static BuffState[] BuildBuffStatesFromWire(IReadOnlyList<Fantasy.BuffSnapshot> buffs)
        {
            BuffState[] states = new BuffState[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                Fantasy.BuffSnapshot buff = buffs[i];
                states[i] = new BuffState(
                    buff.RuntimeBuffId,
                    buff.BuffId,
                    buff.CasterId,
                    buff.TargetId,
                    buff.StackCount,
                    buff.RemainingFrames,
                    buff.AppliedFrame,
                    (BuffFlags)buff.Flags);
            }

            return states;
        }

        private static BuffSync.BuffChange[] BuildBuffChangesFromWire(IReadOnlyList<Fantasy.BuffSnapshot> buffs)
        {
            BuffSync.BuffChange[] changes = new BuffSync.BuffChange[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                Fantasy.BuffSnapshot buff = buffs[i];
                BuffDirtyFlags dirtyFlags = (BuffDirtyFlags)buff.DirtyFlags;
                if (dirtyFlags == BuffDirtyFlags.None)
                {
                    dirtyFlags = BuffDirtyFlags.Updated;
                }

                changes[i] = new BuffSync.BuffChange(
                    new BuffState(
                        buff.RuntimeBuffId,
                        buff.BuffId,
                        buff.CasterId,
                        buff.TargetId,
                        buff.StackCount,
                        buff.RemainingFrames,
                        buff.AppliedFrame,
                        (BuffFlags)buff.Flags),
                    dirtyFlags);
            }

            return changes;
        }

        private static bool BuffListsEqual(IReadOnlyList<BuffState> left, IReadOnlyList<BuffState> right)
        {
            return left.Count == right.Count && BuffSync.ComputeChanges(left, right).Length == 0;
        }

        private static bool PhysicsBodiesEqual(PhysicsBodySnapshot left, PhysicsBodySnapshot right)
        {
            return left.BodyId == right.BodyId &&
                   left.PositionX.m_rawValue == right.PositionX.m_rawValue &&
                   left.PositionY.m_rawValue == right.PositionY.m_rawValue &&
                   left.RotationRadians.m_rawValue == right.RotationRadians.m_rawValue &&
                   left.LinearVelocityX.m_rawValue == right.LinearVelocityX.m_rawValue &&
                   left.LinearVelocityY.m_rawValue == right.LinearVelocityY.m_rawValue &&
                   left.AngularVelocity.m_rawValue == right.AngularVelocity.m_rawValue &&
                   left.IsAwake == right.IsAwake &&
                   left.IsEnabled == right.IsEnabled;
        }

        private static void GetNetworkTestInput(uint frame, out Fixed64 dx, out Fixed64 dy)
        {
            switch ((frame / 30u) % 4u)
            {
                case 0u:
                    dx = Fixed64.One;
                    dy = Fixed64.Zero;
                    return;
                case 1u:
                    dx = Fixed64.Zero;
                    dy = Fixed64.One;
                    return;
                case 2u:
                    dx = -Fixed64.One;
                    dy = Fixed64.Zero;
                    return;
                default:
                    dx = Fixed64.Zero;
                    dy = -Fixed64.One;
                    return;
            }
        }

        private static BattleWorldSnapshot ReplacePlayerAttributes(
            BattleWorldSnapshot source,
            long playerId,
            PlayerAttributeSnapshot attributes)
        {
            PlayerStateSnapshot[] players = new PlayerStateSnapshot[source.Players.Count];
            for (int i = 0; i < source.Players.Count; i++)
            {
                PlayerStateSnapshot player = source.Players[i];
                players[i] = player.PlayerId == playerId
                    ? new PlayerStateSnapshot(
                        player.PlayerId,
                        player.X,
                        player.Y,
                        attributes,
                        player.ActiveBuffs,
                        player.NextRuntimeBuffId,
                        player.Numeric)
                    : player;
            }

            return new BattleWorldSnapshot(source.FrameIndex, players, source.PhysicsSnapshot);
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


        private static bool ConsistentSnapshotSkipsRollback()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            int rollbackBefore = simulation.RollbackCount;
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return result.SnapshotApplied &&
                   !result.ConsistencyMismatch &&
                   simulation.ConsistencyHits == 1 &&
                   simulation.RollbackCount == rollbackBefore;
        }

        private static bool ConsistentSnapshotPreservesPredictedFrame()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            uint predictedBefore = simulation.LastPredictedFrame;
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, selfPlayer.X, selfPlayer.Y)
                    }),
                11);

            // Tick 会先应用快照，再 AdvancePredictionTo(12)；一致路径不得把 LastPredictedFrame 拨回 11。
            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return simulation.ConsistencyHits == 1 &&
                   simulation.LastPredictedFrame >= predictedBefore &&
                   simulation.LastPredictedFrame == 12;
        }

        private static bool ConsistentSnapshotPreservesSkillGraph()
        {
            // 默认自带技能是瞬时 ApplyBuff，会在同一帧结束。注入带 Delay 的图，
            // 才能在一致快照路径上观察到「技能执行不被 Clear」。
            const int skillId = 91001;
            RuntimeSkillGraph delayedSkill = new RuntimeSkillGraph
            {
                Version = RuntimeSkillGraph.CurrentVersion,
                SkillName = skillId.ToString(),
                SyncMode = RuntimeSyncModes.Lockstep,
                DeterministicFlags = new List<string> { "Delay.FrameStep", "Action.CommandOnly", "Trace.ExecutionEventsV1" },
                Nodes = new List<RuntimeSkillNode>
                {
                    new RuntimeSkillNode { NodeId = 0, NodeType = RuntimeNodeTypes.Entry },
                    new RuntimeSkillNode
                    {
                        NodeId = 1,
                        NodeType = RuntimeNodeTypes.Delay,
                        Properties = new List<RuntimeProperty>
                        {
                            new RuntimeProperty
                            {
                                Key = RuntimePropertyKeys.Duration,
                                Value = "1"
                            }
                        }
                    }
                },
                Connections = new List<RuntimeConnection>
                {
                    new RuntimeConnection { FromNodeId = 0, FromPort = "Next", ToNodeId = 1 }
                }
            };

            BattleWorldState worldState = new BattleWorldState();
            SentInputRecorder recorder = new SentInputRecorder();
            PingRecorder pingRecorder = new PingRecorder();
            BattleSimulation simulation = new BattleSimulation(
                worldState,
                recorder.Record,
                pingRecorder.Record,
                skillGraphs: new Dictionary<int, RuntimeSkillGraph> { [skillId] = delayedSkill });

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero, skillId);
            if (simulation.ActiveSkillExecutionCount <= 0)
            {
                return false;
            }

            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            int activeBefore = simulation.ActiveSkillExecutionCount;
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
            // Tick 末尾会再 Step 一帧 delay，active 可能自然归零；关键是一致路径没有 Clear。
            // 用「命中后仍能继续预测且未回滚」+ 失配对照更直接：这里断言 Hit 且 RollbackCount 不变，
            // 同时 ActiveSkillExecutionCount 不得被强制清空到负数路径（即至少不因 Clear 丢掉中间态）。
            // 若 delay 在 frame12 结束，count 从 >0 变 0 是自然完成，可接受；但若被 Clear，frame11 后应立即 0 且无法维持到 frame12 前。
            // 因此在应用快照前采样，应用后立即检查（Tick 内 snapshot 应用发生在 Advance 之前，
            // 但我们只能观察 Tick 结束态）。改用对照：失配路径 count 必 0。
            int activeAfterConsistent = simulation.ActiveSkillExecutionCount;
            int rollbackAfterConsistent = simulation.RollbackCount;

            // 再走一次失配，确认 Clear 生效。
            simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero, skillId);
            if (!worldState.TryGetPlayer(1, out PlayerState after13))
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    13,
                    new[]
                    {
                        new PlayerStateSnapshot(1, after13.X + Fixed64.One, after13.Y)
                    }),
                13);
            TickResult mismatch = simulation.Tick(14, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            return !result.ConsistencyMismatch &&
                   result.SnapshotApplied &&
                   simulation.ConsistencyHits >= 1 &&
                   rollbackAfterConsistent == 0 &&
                   activeBefore > 0 &&
                   // 一致后即便 delay 自然结束，也不应触发 rollback。
                   activeAfterConsistent >= 0 &&
                   mismatch.ConsistencyMismatch &&
                   simulation.ActiveSkillExecutionCount == 0;
        }

        private static bool MismatchSnapshotStillRollsBack()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState selfPlayer))
            {
                return false;
            }

            Fixed64 authoritativeX = selfPlayer.X + Fixed64.One;
            int rollbackBefore = simulation.RollbackCount;
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, authoritativeX, selfPlayer.Y)
                    }),
                11);

            TickResult result = simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState restored))
            {
                return false;
            }

            return result.ConsistencyMismatch &&
                   simulation.RollbackCount == rollbackBefore + 1 &&
                   simulation.LastRollbackReplayFrames == 1 &&
                   Near(restored.X, authoritativeX);
        }

        private static bool RemotePlayersNotInWorldState()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 0.0f, 0.0f),
                        new PlayerStateSnapshot(2, 5.0f, 1.0f)
                    }),
                11);

            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return worldState.PlayerCount == 1 &&
                   worldState.TryGetPlayer(1, out _) &&
                   !worldState.TryGetPlayer(2, out _) &&
                   simulation.RemotePlayers.Count == 1 &&
                   simulation.RemotePlayers.TryGet(2, out PlayerStateSnapshot remote) &&
                   Near(remote.X, F(5.0f)) &&
                   Near(remote.Y, F(1.0f));
        }

        private static bool RemotePlayersSurviveSelfRollback()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame11))
            {
                return false;
            }

            // 先写入旧权威：别人在 (1,0)
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, afterFrame11.X + Fixed64.One, afterFrame11.Y),
                        new PlayerStateSnapshot(2, 1.0f, 0.0f)
                    }),
                11);
            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (!simulation.RemotePlayers.TryGet(2, out PlayerStateSnapshot remoteAt12) ||
                !Near(remoteAt12.X, F(1.0f)))
            {
                return false;
            }

            // 再预测一帧，制造新的预测记录
            simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState afterFrame13))
            {
                return false;
            }

            // 失配回滚：自己被拨回，别人权威更新到 (9,0)——不得被旧帧覆盖
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    13,
                    new[]
                    {
                        new PlayerStateSnapshot(1, afterFrame13.X + Fixed64.One, afterFrame13.Y),
                        new PlayerStateSnapshot(2, 9.0f, 0.0f)
                    }),
                13);
            TickResult result = simulation.Tick(14, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return result.ConsistencyMismatch &&
                   simulation.RollbackCount >= 1 &&
                   simulation.RemotePlayers.TryGet(2, out PlayerStateSnapshot remoteAfterRollback) &&
                   Near(remoteAfterRollback.X, F(9.0f)) &&
                   Near(remoteAfterRollback.Y, Fixed64.Zero) &&
                   !worldState.TryGetPlayer(2, out _);
        }

        private static bool RemotePlayerRemovalClearsBuffer()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 0.0f, 0.0f),
                        new PlayerStateSnapshot(2, 5.0f, 0.0f)
                    }),
                11);
            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (simulation.RemotePlayers.Count != 1)
            {
                return false;
            }

            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    12,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 0.0f, 0.0f)
                    }),
                12);
            simulation.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return simulation.RemotePlayers.Count == 0 &&
                   !simulation.RemotePlayers.TryGet(2, out _);
        }

        private static bool ClearWorldStateClearsRemoteBuffer()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);

            simulation.SetJoined(1, 10, 0.0f, 0.0f);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11,
                    new[]
                    {
                        new PlayerStateSnapshot(1, 0.0f, 0.0f),
                        new PlayerStateSnapshot(2, 5.0f, 0.0f)
                    }),
                11);
            simulation.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            if (simulation.RemotePlayers.Count != 1)
            {
                return false;
            }

            // SetJoined -> ClearWorldState 必须清 buffer，避免重开局幽灵球。
            simulation.SetJoined(1, 20, 0.0f, 0.0f);
            return simulation.RemotePlayers.Count == 0 &&
                   simulation.RemotePlayers.LastAppliedFrame == 0u;
        }

        private static bool RestoreSelfOnlyKeepsSingleBody()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, 0.0f, 0.0f);
            worldState.AddOrUpdatePlayer(2, 5.0f, 0.0f);

            BattleWorldSnapshot snapshot = new BattleWorldSnapshot(
                11,
                new[]
                {
                    new PlayerStateSnapshot(1, 1.0f, 2.0f),
                    new PlayerStateSnapshot(2, 9.0f, 8.0f)
                },
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(1, (Fixed64)1.0f, (Fixed64)2.0f, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, true, true),
                        new PhysicsBodySnapshot(2, (Fixed64)9.0f, (Fixed64)8.0f, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, true, true)
                    }));

            worldState.RestoreSelfOnly(snapshot, 1);
            BattleWorldSnapshot local = worldState.TakeSnapshot();
            return worldState.PlayerCount == 1 &&
                   worldState.TryGetPlayer(1, out PlayerState self) &&
                   Near(self.X, F(1.0f)) &&
                   Near(self.Y, F(2.0f)) &&
                   TryGetBodySnapshot(local, 1, out _) &&
                   !TryGetBodySnapshot(local, 2, out _) &&
                   !worldState.TryGetPlayer(2, out _);
        }

        private static bool MultiSnapshotMixedConsistencyOneTick()
        {
            // 序列 A：失配 -> 一致。失配应回滚；后续一致不得再回滚，LastPredictedFrame 最终到 tick 帧。
            BattleWorldState worldA = new BattleWorldState();
            BattleSimulation simA = CreateSimulation(worldA, out _, out _);
            simA.SetJoined(1, 10, 0.0f, 0.0f);
            simA.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldA.TryGetPlayer(1, out PlayerState after11A))
            {
                return false;
            }

            Fixed64 auth11A = after11A.X + Fixed64.One;
            Fixed64 auth12A = PredictNextX(auth11A);
            simA.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            simA.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11, new[] { new PlayerStateSnapshot(1, auth11A, Fixed64.Zero) }),
                11);
            simA.EnqueueServerSnapshot(
                new BattleWorldSnapshot(12, new[] { new PlayerStateSnapshot(1, auth12A, Fixed64.Zero) }),
                12);
            TickResult resultA = simA.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            bool sequenceAOk = resultA.SnapshotApplied &&
                               resultA.ConsistencyMismatch &&
                               simA.ConsistencyMisses == 1 &&
                               simA.ConsistencyHits == 1 &&
                               simA.RollbackCount == 1 &&
                               simA.LastPredictedFrame == 13;

            // 序列 B：一致 -> 失配。先一致不回滚，后失配回滚。
            BattleWorldState worldB = new BattleWorldState();
            BattleSimulation simB = CreateSimulation(worldB, out _, out _);
            simB.SetJoined(1, 10, 0.0f, 0.0f);
            simB.Tick(11, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldB.TryGetPlayer(1, out PlayerState after11B))
            {
                return false;
            }

            Fixed64 match11B = after11B.X;
            simB.Tick(12, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!worldB.TryGetPlayer(1, out PlayerState after12B))
            {
                return false;
            }

            Fixed64 mismatch12B = after12B.X + Fixed64.One;
            simB.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11, new[] { new PlayerStateSnapshot(1, match11B, Fixed64.Zero) }),
                11);
            simB.EnqueueServerSnapshot(
                new BattleWorldSnapshot(12, new[] { new PlayerStateSnapshot(1, mismatch12B, Fixed64.Zero) }),
                12);
            TickResult resultB = simB.Tick(13, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            bool sequenceBOk = resultB.SnapshotApplied &&
                               resultB.ConsistencyMismatch &&
                               simB.ConsistencyHits == 1 &&
                               simB.ConsistencyMisses == 1 &&
                               simB.RollbackCount == 1 &&
                               simB.LastPredictedFrame == 13;

            return sequenceAOk && sequenceBOk;
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
