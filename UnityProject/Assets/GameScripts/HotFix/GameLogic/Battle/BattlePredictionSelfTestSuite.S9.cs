using System;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Network;

namespace GameLogic
{
    public static partial class BattlePredictionSelfTestSuite
    {
        private static bool DisconnectedPlayerInputIsFrozenNotReused()
        {
            ReusablePlayerInput input = new ReusablePlayerInput();
            input.Record(Fixed64.One, Fixed64.Zero);
            if (!input.TryGet(out Fixed64 beforeDx, out _) || beforeDx != Fixed64.One)
            {
                return false;
            }

            input.SetSuppressed(true);
            input.Record(-Fixed64.One, Fixed64.Zero);
            return input.IsSuppressed &&
                   !input.HasValue &&
                   !input.TryGet(out Fixed64 dx, out Fixed64 dy) &&
                   dx == Fixed64.Zero &&
                   dy == Fixed64.Zero;
        }

        private static bool DisconnectedPlayerDoesNotTriggerKnockback()
        {
            ReusablePlayerInput input = new ReusablePlayerInput();
            input.Record(Fixed64.One, Fixed64.Zero);
            input.SetSuppressed(true);

            PlayerState disconnected = new PlayerState(1L, Fixed64.Zero, Fixed64.Zero);
            PlayerState other = new PlayerState(2L, (Fixed64)2, Fixed64.Zero);
            bool hasMovement = input.TryGet(out Fixed64 dx, out Fixed64 dy);
            return !hasMovement &&
                   dx == Fixed64.Zero &&
                   dy == Fixed64.Zero &&
                   disconnected.DashRemainingFrames == 0 &&
                   other.KnockbackRemainingFrames == 0 &&
                   !BuffSystem.HasBuff(disconnected, DashTuning.DashBuffId) &&
                   !BuffSystem.HasBuff(other, KnockbackTuning.KnockbackBuffId);
        }

        private static bool DisconnectedPlayerStillTicksBuffsAndStamina()
        {
            ReusablePlayerInput input = new ReusablePlayerInput();
            input.SetSuppressed(true);
            PlayerState state = new PlayerState(1L, Fixed64.Zero, Fixed64.Zero);
            if (!StaminaSystem.TryConsume(state, DashTuning.StaminaRegenAmount * 2))
            {
                return false;
            }

            const int duration = 5;
            long runtimeBuffId = BuffSystem.AddBuff(
                state,
                state.PlayerId,
                9001,
                duration,
                1,
                0u,
                BuffFlags.Duration | BuffFlags.Dispellable);
            int staminaBefore = state.Stamina;
            for (int frame = 1; frame <= duration; frame++)
            {
                BuffSystem.ApplyTick(state, (uint)frame);
                StaminaSystem.Tick(state);
            }

            return input.IsSuppressed &&
                   runtimeBuffId > 0L &&
                   !BuffSystem.HasBuff(state, 9001) &&
                   state.Stamina == staminaBefore + DashTuning.StaminaRegenAmount;
        }

        private static bool DisconnectedPlayerRemainsKnockbackTarget()
        {
            ReusablePlayerInput input = new ReusablePlayerInput();
            input.SetSuppressed(true);
            PlayerState state = new PlayerState(1L, Fixed64.Zero, Fixed64.Zero);
            long runtimeBuffId = S8AddDisplacementBuff(
                state,
                KnockbackTuning.KnockbackBuffId,
                KnockbackTuning.KnockbackSpeedFixed,
                Fixed64.Zero,
                KnockbackTuning.KnockbackFrames,
                1u);

            return input.IsSuppressed &&
                   runtimeBuffId > 0L &&
                   state.KnockbackRuntimeBuffId == runtimeBuffId &&
                   state.KnockbackRemainingFrames == KnockbackTuning.KnockbackFrames &&
                   state.KnockbackVelocityX == KnockbackTuning.KnockbackSpeedFixed;
        }

        private static bool DashInFlightCompletesDuringDisconnect()
        {
            ReusablePlayerInput input = new ReusablePlayerInput();
            PlayerState state = new PlayerState(1L, Fixed64.Zero, Fixed64.Zero);
            FrameSyncPhysicsWorld physics = new FrameSyncPhysicsWorld();
            physics.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            long runtimeBuffId = S8AddDisplacementBuff(
                state,
                DashTuning.DashBuffId,
                DashTuning.DashSpeedFixed,
                Fixed64.Zero,
                DashTuning.DashFrames,
                0u);
            input.SetSuppressed(true);

            for (uint frame = 0u; frame < DashTuning.DashFrames; frame++)
            {
                DisplacementSystem.Apply(state, physics);
                DisplacementSystem.Decay(state);
                physics.Step(DeterminismRules.FixedDeltaTimeFixed64);
                BuffSystem.ApplyTick(state, frame);
            }

            return runtimeBuffId > 0L &&
                   input.IsSuppressed &&
                   physics.TryGetBodySnapshot(1, out PhysicsBodySnapshot body) &&
                   body.PositionX > Fixed64.Zero &&
                   S8DashDisplacementIsClear(state) &&
                   !BuffSystem.HasBuff(state, DashTuning.DashBuffId);
        }

        private static bool NoStaleInputReuseOnFirstFrameAfterUnsuppress()
        {
            ReusablePlayerInput input = new ReusablePlayerInput();
            input.Record(Fixed64.One, Fixed64.Zero);
            input.SetSuppressed(true);
            input.SetSuppressed(false);
            return !input.IsSuppressed &&
                   !input.HasValue &&
                   !input.TryGet(out Fixed64 dx, out Fixed64 dy) &&
                   dx == Fixed64.Zero &&
                   dy == Fixed64.Zero;
        }

        private static bool AttributeBaselineIsPerObserver()
        {
            ObserverTargetBaselineMap<S9AttributeBaseline> baselines =
                new ObserverTargetBaselineMap<S9AttributeBaseline>();
            S9AttributeBaseline first = new S9AttributeBaseline(PlayerAttributeSnapshot.Default, 10u);
            S9AttributeBaseline second = new S9AttributeBaseline(
                new PlayerAttributeSnapshot(80, 100, 100, 100, 10, 100, 100),
                20u);
            baselines.Set(101L, 1L, first);
            baselines.Set(202L, 1L, second);

            return baselines.Count == 2 &&
                   baselines.TryGet(101L, 1L, out S9AttributeBaseline actualFirst) &&
                   baselines.TryGet(202L, 1L, out S9AttributeBaseline actualSecond) &&
                   ReferenceEquals(first, actualFirst) &&
                   ReferenceEquals(second, actualSecond);
        }

        private static bool NewObserverGetsFullAttributesForAllPlayers()
        {
            ObserverTargetBaselineMap<S9AttributeBaseline> baselines =
                new ObserverTargetBaselineMap<S9AttributeBaseline>();
            PlayerAttributeSnapshot attributes = PlayerAttributeSnapshot.Default;
            baselines.Set(101L, 1L, new S9AttributeBaseline(attributes, 10u));
            baselines.Set(101L, 2L, new S9AttributeBaseline(attributes, 10u));

            bool firstHasBaseline = baselines.TryGet(202L, 1L, out _);
            bool secondHasBaseline = baselines.TryGet(202L, 2L, out _);
            return PlayerAttributeSync.ComputeDirtyMask(firstHasBaseline, default, attributes) ==
                       PlayerAttributeDirtyFlags.All &&
                   PlayerAttributeSync.ComputeDirtyMask(secondHasBaseline, default, attributes) ==
                       PlayerAttributeDirtyFlags.All;
        }

        private static bool ExistingObserversUnaffectedByNewObserverFullSync()
        {
            ObserverTargetBaselineMap<S9AttributeBaseline> baselines =
                new ObserverTargetBaselineMap<S9AttributeBaseline>();
            S9AttributeBaseline existing = new S9AttributeBaseline(PlayerAttributeSnapshot.Default, 10u);
            baselines.Set(101L, 1L, existing);
            baselines.Set(202L, 1L, new S9AttributeBaseline(PlayerAttributeSnapshot.Default, 20u));

            return baselines.TryGet(101L, 1L, out S9AttributeBaseline actual) &&
                   ReferenceEquals(existing, actual) &&
                   actual.FrameIndex == 10u;
        }

        private static bool AttributeFullSyncNotDependentOnBuffBaselineAbsence()
        {
            ObserverTargetBaselineMap<S9AttributeBaseline> attributes =
                new ObserverTargetBaselineMap<S9AttributeBaseline>();
            ObserverTargetBaselineMap<S9BuffBaseline> buffs =
                new ObserverTargetBaselineMap<S9BuffBaseline>();
            buffs.Set(202L, 1L, new S9BuffBaseline(Array.Empty<BuffState>(), 10u));

            bool hasAttributeBaseline = attributes.TryGet(202L, 1L, out _);
            bool hasBuffBaseline = buffs.TryGet(202L, 1L, out _);
            return hasBuffBaseline &&
                   !hasAttributeBaseline &&
                   PlayerAttributeSync.ComputeDirtyMask(
                       hasAttributeBaseline,
                       default,
                       PlayerAttributeSnapshot.Default) == PlayerAttributeDirtyFlags.All;
        }

        private static bool AttributeBaselineFrameAdvancesPerObserverOnUnchangedFrames()
        {
            ObserverTargetBaselineMap<S9AttributeBaseline> baselines =
                new ObserverTargetBaselineMap<S9AttributeBaseline>();
            PlayerAttributeSnapshot unchanged = PlayerAttributeSnapshot.Default;
            baselines.Set(101L, 1L, new S9AttributeBaseline(unchanged, 10u));

            bool newObserverHasBaseline = baselines.TryGet(202L, 1L, out _);
            if (PlayerAttributeSync.ComputeDirtyMask(newObserverHasBaseline, default, unchanged) !=
                PlayerAttributeDirtyFlags.All)
            {
                return false;
            }

            baselines.Set(202L, 1L, new S9AttributeBaseline(unchanged, 20u));
            return baselines.TryGet(101L, 1L, out S9AttributeBaseline first) &&
                   baselines.TryGet(202L, 1L, out S9AttributeBaseline second) &&
                   first.FrameIndex == 10u &&
                   second.FrameIndex == 20u;
        }

        private static bool ReconnectBuffBaselineIsFreshForNewSession()
        {
            ObserverTargetBaselineMap<S9BuffBaseline> baselines =
                new ObserverTargetBaselineMap<S9BuffBaseline>();
            baselines.Set(
                101L,
                1L,
                new S9BuffBaseline(new[] { CreateTestBuff(1L, 1, 10, 1u) }, 10u));

            return baselines.TryGet(101L, 1L, out _) &&
                   !baselines.TryGet(202L, 1L, out _);
        }

        private static bool ReconnectFullSyncRestoresBuffsAndStamina()
        {
            BattleWorldState world = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(world, out _, out _);
            simulation.SetJoined(1L, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.SetJoined(1L, 20u, Fixed64.Zero, Fixed64.Zero, true);

            PlayerAttributeSnapshot attributes = new PlayerAttributeSnapshot(90, 100, 70, 100, 12, 37, 100);
            BuffState buff = CreateTestBuff(77L, 2, 19, 18u);
            PlayerStateSnapshot authoritative = new PlayerStateSnapshot(
                1L,
                (Fixed64)4,
                (Fixed64)(-3),
                attributes,
                new[] { buff },
                78L,
                NumericModifierSnapshot.FromAttributes(attributes));
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(21u, new[] { authoritative }),
                21u,
                true);
            simulation.Tick(21u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.One);

            return !simulation.AwaitingFullSnapshot &&
                   world.TryGetPlayer(1L, out PlayerState restored) &&
                   restored.Stamina == 37 &&
                   restored.NextRuntimeBuffId == 78L &&
                   BuffListsEqual(restored.ActiveBuffs, new[] { buff });
        }

        private static bool ClientRejoinRebuildsFromAuthoritativeState()
        {
            BattleWorldState world = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(world, out SentInputRecorder recorder, out _);
            simulation.SetJoined(1L, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            int inputCountBeforeReconnect = recorder.Count;

            simulation.SetJoined(1L, 20u, (Fixed64)99, (Fixed64)99, true);
            PlayerStateSnapshot authoritative = new PlayerStateSnapshot(1L, (Fixed64)7, (Fixed64)(-2));
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(21u, new[] { authoritative }),
                21u,
                true);
            simulation.Tick(21u, DeterminismRules.FixedDeltaTimeFixed64, -Fixed64.One, Fixed64.One);

            return recorder.Count == inputCountBeforeReconnect &&
                   world.TryGetPlayer(1L, out PlayerState restored) &&
                   restored.X == (Fixed64)7 &&
                   restored.Y == (Fixed64)(-2) &&
                   simulation.LastAppliedFrame == 21u;
        }

        private static bool SuccessfulAckResetsConsecutiveProbeTimeouts()
        {
            ServerRttTracker tracker = new ServerRttTracker();
            tracker.RecordProbeSent(1UL, 0L);
            tracker.CleanupStale(10L, 5L);
            tracker.RecordProbeSent(2UL, 10L);
            tracker.CleanupStale(20L, 5L);
            if (tracker.ConsecutiveTimedOutProbeCount != 2)
            {
                return false;
            }

            tracker.RecordProbeSent(3UL, 20L);
            return tracker.TryRecordAck(3UL, 21L, out long rttMs) &&
                   rttMs == 1L &&
                   tracker.TimedOutProbeCount == 2 &&
                   tracker.ConsecutiveTimedOutProbeCount == 0;
        }

        private static bool ReconnectPreservesCumulativeDiagnostics()
        {
            BattleWorldState world = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(world, out _, out _);
            simulation.SetJoined(1L, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            if (!world.TryGetPlayer(1L, out PlayerState self))
            {
                return false;
            }

            PlayerAttributeSnapshot changedAttributes = new PlayerAttributeSnapshot(
                self.Health,
                self.MaxHealth,
                self.Mana,
                self.MaxMana,
                self.Attack,
                self.Stamina - 1,
                self.MaxStamina);
            BattleWorldSnapshot predictedWorld = S8CaptureWorld(world, 11u);
            simulation.EnqueueServerSnapshot(
                ReplacePlayerAttributes(predictedWorld, 1L, changedAttributes),
                11u);
            simulation.Tick(12u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            int mismatchBefore = simulation.StateMismatchCount;
            int rollbackBefore = simulation.RollbackCount;
            if (mismatchBefore <= 0 || rollbackBefore <= 0)
            {
                return false;
            }

            simulation.SetJoined(1L, 20u, Fixed64.Zero, Fixed64.Zero, true);
            return simulation.StateMismatchCount == mismatchBefore &&
                   simulation.RollbackCount == rollbackBefore &&
                   simulation.StateMismatchCountAtReconnect == mismatchBefore &&
                   simulation.RollbackCountAtReconnect == rollbackBefore;
        }

        private static bool NoGameplayInputWhileAwaitingFullSnapshot()
        {
            BattleWorldState world = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(world, out SentInputRecorder recorder, out _);
            simulation.SetJoined(1L, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.SetJoined(1L, 20u, Fixed64.Zero, Fixed64.Zero, true);
            for (uint frame = 21u; frame <= 25u; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.One);
            }

            return simulation.AwaitingFullSnapshot &&
                   recorder.Count == 0 &&
                   world.TryGetPlayer(1L, out PlayerState state) &&
                   state.X == Fixed64.Zero &&
                   state.Y == Fixed64.Zero;
        }

        private static bool AwaitingFullSnapshotTimesOutIntoRetry()
        {
            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);
            simulation.SetJoined(1L, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.SetJoined(1L, 20u, Fixed64.Zero, Fixed64.Zero, true);
            for (int i = 0; i < BattleSimulation.ReconnectFullSnapshotTimeoutFrames; i++)
            {
                simulation.Tick(
                    unchecked(21u + (uint)i),
                    DeterminismRules.FixedDeltaTimeFixed64,
                    Fixed64.One,
                    Fixed64.Zero);
            }

            return simulation.AwaitingFullSnapshot &&
                   simulation.FullSnapshotTimedOut &&
                   simulation.AwaitingFullSnapshotFrames == BattleSimulation.ReconnectFullSnapshotTimeoutFrames;
        }

        private static bool DivergedMergeDoesNotClearAwaitingFlag()
        {
            PlayerAttributeSnapshot local = PlayerAttributeSnapshot.Default;
            PlayerAttributeSnapshot changed = new PlayerAttributeSnapshot(50, 100, 100, 100, 10, 100, 100);
            AttributeMergeResult merge = BattleSnapshotProtocolMapper.MergeAttributes(
                11u,
                CreateAttributePacket(11u, 1L, PlayerAttributeDirtyFlags.Health, 9u, changed),
                true,
                local,
                10u);
            if (!merge.Diverged || merge.Applied)
            {
                return false;
            }

            BattleSimulation simulation = CreateSimulation(new BattleWorldState(), out _, out _);
            simulation.SetJoined(1L, 10u, Fixed64.Zero, Fixed64.Zero, true);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11u, new[] { new PlayerStateSnapshot(1L, Fixed64.One, Fixed64.Zero) }),
                11u,
                isRecoveryFullSnapshot: false);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            return simulation.AwaitingFullSnapshot && simulation.LastAppliedFrame == 10u;
        }

        private sealed class S9AttributeBaseline
        {
            public S9AttributeBaseline(PlayerAttributeSnapshot attributes, uint frameIndex)
            {
                Attributes = attributes;
                FrameIndex = frameIndex;
            }

            public PlayerAttributeSnapshot Attributes { get; }
            public uint FrameIndex { get; }
        }

        private sealed class S9BuffBaseline
        {
            public S9BuffBaseline(BuffState[] buffs, uint frameIndex)
            {
                Buffs = buffs;
                FrameIndex = frameIndex;
            }

            public BuffState[] Buffs { get; }
            public uint FrameIndex { get; }
        }
    }
}
