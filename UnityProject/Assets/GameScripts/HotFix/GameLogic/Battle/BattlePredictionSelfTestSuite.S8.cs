using System;
using System.Collections.Generic;
using FixedMathSharp;
using Fantasy.Serialize;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.InputBuffering;
using GameShared.SkillGraph;

namespace GameLogic
{
    public static partial class BattlePredictionSelfTestSuite
    {
        private static bool DisplacementEffectAppliesAndDecays()
        {
            PlayerState state = new PlayerState(1, Fixed64.Zero, Fixed64.Zero);
            try
            {
                BuffSystem.AddBuff(
                    state,
                    new ApplyBuffCommand
                    {
                        CasterId = 1,
                        TargetId = 1,
                        BuffId = DashTuning.DashBuffId,
                        DurationFrames = DashTuning.DashFrames,
                        StackCount = 1,
                        Flags = BuffFlags.Duration | BuffFlags.Dispellable
                    },
                    new DefaultBuffConfigProvider());
                return false;
            }
            catch (InvalidOperationException)
            {
                if (state.ActiveBuffs.Count != 0 || !S8DashDisplacementIsClear(state))
                {
                    return false;
                }
            }

            FrameSyncPhysicsWorld physicsWorld = new FrameSyncPhysicsWorld();
            physicsWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            long runtimeBuffId = S8AddDisplacementBuff(
                state,
                DashTuning.DashBuffId,
                (Fixed64)3,
                (Fixed64)4,
                DashTuning.DashFrames,
                0u);

            if (runtimeBuffId <= 0 ||
                state.DashRuntimeBuffId != runtimeBuffId ||
                state.DashRemainingFrames != DashTuning.DashFrames ||
                state.DashVelocityX != (Fixed64)3 ||
                state.DashVelocityY != (Fixed64)4)
            {
                return false;
            }

            Fixed64 expectedX = Fixed64.Zero;
            Fixed64 expectedY = Fixed64.Zero;
            for (int frame = 0; frame < DashTuning.DashFrames; frame++)
            {
                DisplacementSystem.Apply(state, physicsWorld);
                DisplacementSystem.Decay(state);
                physicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
                expectedX += (Fixed64)3 * DeterminismRules.FixedDeltaTimeFixed64;
                expectedY += (Fixed64)4 * DeterminismRules.FixedDeltaTimeFixed64;

                if (!physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot body) ||
                    body.PositionX != expectedX ||
                    body.PositionY != expectedY ||
                    state.DashRemainingFrames != DashTuning.DashFrames - frame - 1)
                {
                    return false;
                }
            }

            return state.DashVelocityX == Fixed64.Zero &&
                   state.DashVelocityY == Fixed64.Zero &&
                   state.DashRuntimeBuffId == 0;
        }

        private static bool DisplacementClearedOnBuffExpiry()
        {
            PlayerState state = new PlayerState(1, Fixed64.Zero, Fixed64.Zero);
            long manualRuntimeId = S8AddDisplacementBuff(
                state,
                DashTuning.DashBuffId,
                DashTuning.DashSpeedFixed,
                Fixed64.Zero,
                DashTuning.DashFrames,
                0u);
            if (BuffSystem.RemoveBuff(state, manualRuntimeId, 0) != 1 || !S8DashDisplacementIsClear(state))
            {
                return false;
            }

            long expiringRuntimeId = S8AddDisplacementBuff(
                state,
                DashTuning.DashBuffId,
                DashTuning.DashSpeedFixed,
                Fixed64.Zero,
                1,
                1u);
            if (expiringRuntimeId <= 0 || BuffSystem.ApplyTick(state, 1u) != 1 || !S8DashDisplacementIsClear(state))
            {
                return false;
            }

            FrameSyncPhysicsWorld physicsWorld = new FrameSyncPhysicsWorld();
            physicsWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            DisplacementSystem.Apply(state, physicsWorld);
            physicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
            return physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot body) &&
                   body.PositionX == Fixed64.Zero &&
                   body.PositionY == Fixed64.Zero;
        }

        private static bool SkillGraphExecutionReplaysAfterRollback()
        {
            const int skillId = 9401;
            const int buffId = 9402;
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = new BattleSimulation(
                worldState,
                (_, _, _, _, _) => { },
                _ => { },
                skillGraphs: new Dictionary<int, RuntimeSkillGraph>
                {
                    [skillId] = S8CreateDelayThenBuffGraph(skillId, buffId, 6)
                });
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero, skillId);

            PlayerStateSnapshot captured = worldState.TakeSnapshot().Players[0];
            if (!captured.SkillExecutions.TryGetValue(1, out ActiveSkillExecutionSnapshot before) ||
                !before.RunnerSnapshot.DelayRemainingFrames.TryGetValue(1, out int beforeRemaining))
            {
                return false;
            }

            PlayerStateSnapshot authoritative = S8CopyPlayer(captured, x: captured.X + F(0.25f));
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11u, new[] { authoritative }),
                11u);
            TickResult result = simulation.Tick(
                12u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);

            if (!result.ConsistencyMismatch || simulation.ActiveSkillExecutionCount != 1 ||
                !worldState.TryGetPlayer(1, out PlayerState restored) ||
                !restored.SkillExecutions.TryGetValue(1, out ActiveSkillExecutionSnapshot after) ||
                !after.RunnerSnapshot.DelayRemainingFrames.TryGetValue(1, out int afterRemaining))
            {
                return false;
            }

            return after.SkillId == skillId &&
                   after.TargetId == 1 &&
                   afterRemaining == beforeRemaining - 1;
        }

        private static bool SkillGraphRestoreResumesCorrectGraph()
        {
            const int skillA = 9411;
            const int skillB = 9412;
            const int buffA = 9421;
            const int buffB = 9422;
            Dictionary<int, RuntimeSkillGraph> graphs = new Dictionary<int, RuntimeSkillGraph>
            {
                [skillA] = S8CreateDelayThenBuffGraph(skillA, buffA, 2),
                [skillB] = S8CreateDelayThenBuffGraph(skillB, buffB, 2)
            };
            S8BuffCommandSink sourceSink = new S8BuffCommandSink();
            BattleSkillGraphRuntime source = new BattleSkillGraphRuntime(
                sourceSink,
                graphs,
                new DelegateSkillRuntimeServices((_, _) => true));
            source.QueueSkillRequest(1, 101, skillA, 0u);
            source.QueueSkillRequest(2, 202, skillB, 0u);
            source.Step(0u);
            Dictionary<long, ActiveSkillExecutionSnapshot> snapshots = source.CaptureExecutions();
            if (snapshots.Count != 2 || snapshots[1].SkillId != skillA || snapshots[2].SkillId != skillB)
            {
                return false;
            }

            S8BuffCommandSink restoredSink = new S8BuffCommandSink();
            BattleSkillGraphRuntime restored = new BattleSkillGraphRuntime(
                restoredSink,
                graphs,
                new DelegateSkillRuntimeServices((_, _) => true));
            restored.RestoreExecutions(snapshots);
            for (uint frame = 1; frame <= 4; frame++)
            {
                restored.Step(frame);
            }

            return restoredSink.ContainsApply(101, buffA) &&
                   restoredSink.ContainsApply(202, buffB) &&
                   !restoredSink.ContainsApply(101, buffB) &&
                   !restoredSink.ContainsApply(202, buffA);
        }

        private static bool DashBuffLatchesDirectionAtStart()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, F(0.6f), F(0.8f), DashTuning.DashSkillId);
            if (!worldState.TryGetPlayer(1, out PlayerState state))
            {
                return false;
            }

            Fixed64 latchedX = state.DashVelocityX;
            Fixed64 latchedY = state.DashVelocityY;
            long runtimeBuffId = state.DashRuntimeBuffId;
            simulation.Tick(12u, DeterminismRules.FixedDeltaTimeFixed64, -Fixed64.One, Fixed64.Zero);
            return Near(latchedX, F(10.8f)) &&
                   Near(latchedY, F(14.4f)) &&
                   state.DashVelocityX == latchedX &&
                   state.DashVelocityY == latchedY &&
                   state.DashRuntimeBuffId == runtimeBuffId;
        }

        private static bool DashConsumesStaminaAndEntersRecover()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero, DashTuning.DashSkillId);
            if (!worldState.TryGetPlayer(1, out PlayerState state) ||
                state.Stamina != state.MaxStamina - DashTuning.DashStaminaCost ||
                !BuffSystem.HasBuff(state, DashTuning.DashBuffId))
            {
                return false;
            }

            bool enteredRecover = false;
            uint recoverStartFrame = 0u;
            for (uint frame = 12u; frame < 12u + DashTuning.DashFrames + 3u; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
                if (BuffSystem.HasBuff(state, DashTuning.RecoverBuffId))
                {
                    enteredRecover = true;
                    recoverStartFrame = frame;
                    break;
                }
            }

            if (!enteredRecover ||
                BuffSystem.HasBuff(state, DashTuning.DashBuffId) ||
                !S8DashDisplacementIsClear(state))
            {
                return false;
            }

            int staminaAtRecoverStart = state.Stamina;
            for (uint offset = 1u; offset <= DashTuning.StaminaRegenIntervalFrames; offset++)
            {
                simulation.Tick(
                    recoverStartFrame + offset,
                    DeterminismRules.FixedDeltaTimeFixed64,
                    Fixed64.Zero,
                    Fixed64.Zero);
            }

            return BuffSystem.HasBuff(state, DashTuning.RecoverBuffId) &&
                   state.Stamina == staminaAtRecoverStart + DashTuning.StaminaRegenAmount;
        }

        private static bool DashBlockedDuringRecover()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState state))
            {
                return false;
            }

            BuffSystem.AddBuff(
                state,
                new ApplyBuffCommand
                {
                    CasterId = 1,
                    TargetId = 1,
                    BuffId = DashTuning.RecoverBuffId,
                    DurationFrames = DashTuning.RecoverFrames,
                    StackCount = 1,
                    FrameIndex = 10u,
                    Flags = BuffFlags.Duration | BuffFlags.Dispellable
                },
                new DefaultBuffConfigProvider());
            int staminaBefore = state.Stamina;
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero, DashTuning.DashSkillId);
            return state.Stamina == staminaBefore &&
                   !BuffSystem.HasBuff(state, DashTuning.DashBuffId) &&
                   simulation.ActiveSkillExecutionCount == 0;
        }

        private static bool DashBlockedWhileDashing()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero, DashTuning.DashSkillId);
            if (!worldState.TryGetPlayer(1, out PlayerState state))
            {
                return false;
            }

            long runtimeBuffId = state.DashRuntimeBuffId;
            int remainingFrames = state.DashRemainingFrames;
            Fixed64 velocityX = state.DashVelocityX;
            int stamina = state.Stamina;
            simulation.Tick(12u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.One, DashTuning.DashSkillId);
            return state.DashRuntimeBuffId == runtimeBuffId &&
                   state.DashRemainingFrames == remainingFrames - 1 &&
                   state.DashVelocityX == velocityX &&
                   state.DashVelocityY == Fixed64.Zero &&
                   state.Stamina == stamina &&
                   simulation.ActiveSkillExecutionCount == 1;
        }

        private static bool DashBlockedWhenStaminaInsufficient()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState state))
            {
                return false;
            }

            try
            {
                StaminaSystem.TryConsume(state, -1);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            state.Stamina = DashTuning.DashStaminaCost - 1;
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero, DashTuning.DashSkillId);
            return state.Stamina == DashTuning.DashStaminaCost - 1 &&
                   !BuffSystem.HasBuff(state, DashTuning.DashBuffId) &&
                   simulation.ActiveSkillExecutionCount == 0;
        }

        private static bool DashZeroInputUsesDefaultDirection()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero, DashTuning.DashSkillId);
            return worldState.TryGetPlayer(1, out PlayerState state) &&
                   state.DashVelocityX == DashTuning.DashSpeedFixed &&
                   state.DashVelocityY == Fixed64.Zero;
        }

        private static bool DashInputSurvivesReplay()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot frame11 = worldState.TakeSnapshot().Players[0];
            simulation.Tick(12u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.One, DashTuning.DashSkillId);
            simulation.Tick(13u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            PlayerStateSnapshot authoritative = S8CopyPlayer(frame11, x: frame11.X + F(0.2f));
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11u, new[] { authoritative }),
                11u);
            TickResult result = simulation.Tick(
                14u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);

            return result.ConsistencyMismatch &&
                   worldState.TryGetPlayer(1, out PlayerState state) &&
                   BuffSystem.HasBuff(state, DashTuning.DashBuffId) &&
                   state.DashVelocityX == Fixed64.Zero &&
                   state.DashVelocityY == DashTuning.DashSpeedFixed &&
                   state.Stamina < state.MaxStamina;
        }

        private static bool StaminaRegenCounterRestoresOnRollback()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState state))
            {
                return false;
            }

            state.Stamina = 50;
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot frame11 = worldState.TakeSnapshot().Players[0];
            simulation.Tick(12u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(13u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);

            PlayerStateSnapshot authoritative = S8CopyPlayer(
                frame11,
                x: frame11.X + F(0.1f),
                staminaRegenCounterFrames: 0);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11u, new[] { authoritative }),
                11u);
            TickResult result = simulation.Tick(
                14u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);
            return result.ConsistencyMismatch &&
                   state.Stamina == 51 &&
                   state.StaminaRegenCounterFrames == 0;
        }

        private static bool DashInputBufferedAcrossRenderFrames()
        {
            InputBuffer<int, int> inputBuffer = new InputBuffer<int, int>();
            inputBuffer.Record(1, DashTuning.DashSkillId, 2);
            inputBuffer.TickDecay();
            return inputBuffer.TryConsume(1, out int skillId) &&
                   skillId == DashTuning.DashSkillId;
        }

        private static bool DashHoldDoesNotAutofire()
        {
            InputBuffer<int, int> inputBuffer = new InputBuffer<int, int>();
            inputBuffer.Record(1, DashTuning.DashSkillId, 2);
            bool first = inputBuffer.TryConsume(1, out int firstSkillId);
            for (int frame = 0; frame < DashTuning.DashFrames + DashTuning.RecoverFrames + 1; frame++)
            {
                inputBuffer.TickDecay();
            }

            bool second = inputBuffer.TryConsume(1, out _);
            return first && firstSkillId == DashTuning.DashSkillId && !second;
        }

        private static bool KnockbackTriggersOnlyOnNewContact()
        {
#if FANTASY_UNITY
            return true;
#else
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            S8JoinCollisionPair(battleLogic, -F(0.75f), F(0.75f));
            S8SubmitDash(battleLogic, 1, 0u, Fixed64.One, Fixed64.Zero);
            battleLogic.Tick(0u, DeterminismRules.FixedDeltaTimeFixed64);
            if (battleLogic.KnockbackTriggerCount != 1)
            {
                return false;
            }

            for (uint frame = 1u; frame <= 5u; frame++)
            {
                battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
            }

            if (battleLogic.KnockbackTriggerCount != 1)
            {
                return false;
            }

            battleLogic.RemovePlayer(1);
            battleLogic.RemovePlayer(2);
            S8JoinCollisionPair(battleLogic, -F(0.75f), F(0.75f));
            S8SubmitDash(battleLogic, 1, 6u, Fixed64.One, Fixed64.Zero);
            battleLogic.Tick(6u, DeterminismRules.FixedDeltaTimeFixed64);
            return battleLogic.KnockbackTriggerCount == 2;
#endif
        }

        private static bool DashStartsWhileAlreadyInContact()
        {
#if FANTASY_UNITY
            return true;
#else
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            S8JoinCollisionPair(battleLogic, -F(0.45f), F(0.45f));
            battleLogic.Tick(0u, DeterminismRules.FixedDeltaTimeFixed64);
            if (battleLogic.KnockbackTriggerCount != 0)
            {
                return false;
            }

            S8SubmitDash(battleLogic, 1, 1u, Fixed64.One, Fixed64.Zero);
            battleLogic.Tick(1u, DeterminismRules.FixedDeltaTimeFixed64);
            battleLogic.Tick(2u, DeterminismRules.FixedDeltaTimeFixed64);
            return battleLogic.KnockbackTriggerCount == 1;
#endif
        }

        private static bool DashCollisionKnocksBackPassivePlayer()
        {
#if FANTASY_UNITY
            return true;
#else
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            S8JoinCollisionPair(battleLogic, -F(0.75f), F(0.75f));
            S8SubmitDash(battleLogic, 1, 0u, Fixed64.One, Fixed64.Zero);
            battleLogic.Tick(0u, DeterminismRules.FixedDeltaTimeFixed64);
            if (!battleLogic.TryGetPlayer(1, out PlayerState attacker) ||
                !battleLogic.TryGetPlayer(2, out PlayerState passive))
            {
                return false;
            }

            return battleLogic.KnockbackTriggerCount == 1 &&
                   S8DashDisplacementIsClear(attacker) &&
                   !BuffSystem.HasBuff(attacker, DashTuning.DashBuffId) &&
                   BuffSystem.HasBuff(attacker, DashTuning.RecoverBuffId) &&
                   passive.KnockbackRemainingFrames == KnockbackTuning.KnockbackFrames &&
                   passive.KnockbackVelocityX > Fixed64.Zero &&
                   BuffSystem.HasBuff(passive, KnockbackTuning.KnockbackBuffId);
#endif
        }

        private static bool DualDashCollisionKnocksBackBoth()
        {
#if FANTASY_UNITY
            return true;
#else
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            S8JoinCollisionPair(battleLogic, -F(0.85f), F(0.85f));
            S8SubmitDash(battleLogic, 1, 0u, Fixed64.One, Fixed64.Zero);
            S8SubmitDash(battleLogic, 2, 0u, -Fixed64.One, Fixed64.Zero);
            battleLogic.Tick(0u, DeterminismRules.FixedDeltaTimeFixed64);
            if (!battleLogic.TryGetPlayer(1, out PlayerState left) ||
                !battleLogic.TryGetPlayer(2, out PlayerState right))
            {
                return false;
            }

            bool passed = battleLogic.KnockbackTriggerCount == 1 &&
                          S8DashDisplacementIsClear(left) &&
                          S8DashDisplacementIsClear(right) &&
                          left.KnockbackVelocityX < Fixed64.Zero &&
                          right.KnockbackVelocityX > Fixed64.Zero &&
                          left.KnockbackRemainingFrames == KnockbackTuning.KnockbackFrames &&
                          right.KnockbackRemainingFrames == KnockbackTuning.KnockbackFrames &&
                          BuffSystem.HasBuff(left, DashTuning.RecoverBuffId) &&
                          BuffSystem.HasBuff(right, DashTuning.RecoverBuffId);
            if (!passed)
            {
                throw new InvalidOperationException(
                    $"dual-dash trigger={battleLogic.KnockbackTriggerCount} " +
                    $"leftDash={left.DashRemainingFrames}/{left.DashRuntimeBuffId} " +
                    $"rightDash={right.DashRemainingFrames}/{right.DashRuntimeBuffId} " +
                    $"leftKb={left.KnockbackVelocityX}/{left.KnockbackRemainingFrames} " +
                    $"rightKb={right.KnockbackVelocityX}/{right.KnockbackRemainingFrames} " +
                    $"recover={BuffSystem.HasBuff(left, DashTuning.RecoverBuffId)}/{BuffSystem.HasBuff(right, DashTuning.RecoverBuffId)}");
            }

            return true;
#endif
        }

        private static bool NonDashCollisionNoKnockback()
        {
#if FANTASY_UNITY
            return true;
#else
            Fantasy.BattleLogic battleLogic = new Fantasy.BattleLogic();
            S8JoinCollisionPair(battleLogic, -F(0.45f), F(0.45f));
            battleLogic.Tick(0u, DeterminismRules.FixedDeltaTimeFixed64);
            return battleLogic.KnockbackTriggerCount == 0 &&
                   battleLogic.TryGetPlayer(1, out PlayerState left) &&
                   battleLogic.TryGetPlayer(2, out PlayerState right) &&
                   left.KnockbackRemainingFrames == 0 &&
                   right.KnockbackRemainingFrames == 0 &&
                   !BuffSystem.HasBuff(left, KnockbackTuning.KnockbackBuffId) &&
                   !BuffSystem.HasBuff(right, KnockbackTuning.KnockbackBuffId);
#endif
        }

        private static bool KnockbackDecayTimelineIsFrameExact()
        {
            PlayerState state = new PlayerState(1, Fixed64.Zero, Fixed64.Zero);
            FrameSyncPhysicsWorld physicsWorld = new FrameSyncPhysicsWorld();
            physicsWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            S8AddDisplacementBuff(
                state,
                KnockbackTuning.KnockbackBuffId,
                KnockbackTuning.KnockbackSpeedFixed,
                Fixed64.Zero,
                KnockbackTuning.KnockbackFrames,
                0u);

            Fixed64 expectedVelocity = KnockbackTuning.KnockbackSpeedFixed;
            for (int frame = 0; frame < KnockbackTuning.KnockbackFrames; frame++)
            {
                if (state.KnockbackVelocityX != expectedVelocity ||
                    state.KnockbackRemainingFrames != KnockbackTuning.KnockbackFrames - frame)
                {
                    return false;
                }

                DisplacementSystem.Apply(state, physicsWorld);
                DisplacementSystem.Decay(state);
                physicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
                if (!physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot body) ||
                    body.LinearVelocityX != expectedVelocity ||
                    body.LinearVelocityY != Fixed64.Zero ||
                    state.KnockbackRemainingFrames != KnockbackTuning.KnockbackFrames - frame - 1)
                {
                    return false;
                }

                expectedVelocity = (expectedVelocity * (Fixed64)KnockbackTuning.DecayNumerator) /
                                   (Fixed64)KnockbackTuning.DecayDenominator;
            }

            DisplacementSystem.Apply(state, physicsWorld);
            physicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
            return state.KnockbackVelocityX == Fixed64.Zero &&
                   state.KnockbackVelocityY == Fixed64.Zero &&
                   state.KnockbackRuntimeBuffId == 0 &&
                   physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot finalBody) &&
                   finalBody.LinearVelocityX == Fixed64.Zero;
        }

        private static bool KnockbackTotalDisplacementMatchesSeries()
        {
            PlayerState state = new PlayerState(1, Fixed64.Zero, Fixed64.Zero);
            FrameSyncPhysicsWorld physicsWorld = new FrameSyncPhysicsWorld();
            physicsWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            S8AddDisplacementBuff(
                state,
                KnockbackTuning.KnockbackBuffId,
                KnockbackTuning.KnockbackSpeedFixed,
                Fixed64.Zero,
                KnockbackTuning.KnockbackFrames,
                0u);

            Fixed64 expectedDisplacement = Fixed64.Zero;
            Fixed64 velocity = KnockbackTuning.KnockbackSpeedFixed;
            for (int frame = 0; frame < KnockbackTuning.KnockbackFrames; frame++)
            {
                expectedDisplacement += velocity * DeterminismRules.FixedDeltaTimeFixed64;
                DisplacementSystem.Apply(state, physicsWorld);
                DisplacementSystem.Decay(state);
                physicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
                velocity = (velocity * (Fixed64)KnockbackTuning.DecayNumerator) /
                           (Fixed64)KnockbackTuning.DecayDenominator;
            }

            return physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot body) &&
                   body.PositionX == expectedDisplacement &&
                   expectedDisplacement == KnockbackTuning.EstimatedTotalDisplacement;
        }

        private static bool SeparationNormalResolverAgreesAcrossCallers()
        {
            SeparationNormalResolver.ResolveWithoutVelocity(
                1,
                2,
                Fixed64.Zero,
                Fixed64.Zero,
                out Fixed64 resolverX,
                out Fixed64 resolverY);
            FrameSyncPhysicsWorld physicsWorld = new FrameSyncPhysicsWorld();
            physicsWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            physicsWorld.EnsureBody(2, Fixed64.Zero, Fixed64.Zero);
            physicsWorld.Step(DeterminismRules.FixedDeltaTimeFixed64);
            if (!physicsWorld.TryGetBodySnapshot(1, out PhysicsBodySnapshot bodyA) ||
                !physicsWorld.TryGetBodySnapshot(2, out PhysicsBodySnapshot bodyB))
            {
                return false;
            }

            Fixed64 separationX = bodyB.PositionX - bodyA.PositionX;
            Fixed64 separationY = bodyB.PositionY - bodyA.PositionY;
            Fixed64 inverseLength = Fixed64.One /
                                    FixedMath.Sqrt((separationX * separationX) + (separationY * separationY));
            separationX *= inverseLength;
            separationY *= inverseLength;
            if (resolverX != separationX || resolverY != separationY)
            {
                return false;
            }

            SeparationNormalResolver.ResolveWithoutVelocity(
                1,
                2,
                (Fixed64)3,
                (Fixed64)4,
                out Fixed64 nonzeroX,
                out Fixed64 nonzeroY);
            return Near(nonzeroX, F(0.6f)) && Near(nonzeroY, F(0.8f));
        }

        private static bool KnockbackStateReplaysWithoutNewMismatch()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot local11 = worldState.TakeSnapshot().Players[0];
            long runtimeBuffId = local11.NextRuntimeBuffId;
            BuffState knockbackBuff = new BuffState(
                runtimeBuffId,
                KnockbackTuning.KnockbackBuffId,
                2,
                1,
                1,
                KnockbackTuning.KnockbackFrames,
                11u,
                BuffFlags.Duration | BuffFlags.Dispellable);
            PlayerStateSnapshot authoritative11 = new PlayerStateSnapshot(
                local11.PlayerId,
                local11.X,
                local11.Y,
                local11.Attributes,
                new[] { knockbackBuff },
                runtimeBuffId + 1,
                local11.Numeric,
                local11.StaminaRegenCounterFrames,
                local11.DashVelocityX,
                local11.DashVelocityY,
                local11.DashRemainingFrames,
                local11.DashRuntimeBuffId,
                KnockbackTuning.KnockbackSpeedFixed,
                Fixed64.Zero,
                KnockbackTuning.KnockbackFrames,
                runtimeBuffId,
                local11.SkillExecutions);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11u, new[] { authoritative11 }),
                11u);
            TickResult first = simulation.Tick(
                12u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);
            if (!first.ConsistencyMismatch || simulation.StateMismatchCount != 1)
            {
                return false;
            }

            BattleWorldSnapshot authoritative12 = S8CaptureWorld(worldState, 12u);
            simulation.EnqueueServerSnapshot(authoritative12, 12u);
            TickResult second = simulation.Tick(
                13u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);
            return !second.ConsistencyMismatch &&
                   simulation.ConsistencyHits >= 1 &&
                   simulation.StateMismatchCount == 1 &&
                   worldState.TryGetPlayer(1, out PlayerState state) &&
                   state.KnockbackRemainingFrames == KnockbackTuning.KnockbackFrames - 2;
        }

        private static bool KnockbackWorstCaseDeviationUnderSmoothingThreshold()
        {
            KnockbackTuning.Validate();
            return PredictionErrorSmoother.MaxSmoothingDistance == KnockbackTuning.MaxSmoothingDistance &&
                   KnockbackTuning.EstimatedWorstCaseDeviation < (Fixed64)PredictionErrorSmoother.MaxSmoothingDistance &&
                   KnockbackTuning.EstimatedTotalDisplacement > Fixed64.Zero;
        }

        private static bool ClientPredictionNeverSelfTriggersKnockback()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            for (uint frame = 11u; frame <= 20u; frame++)
            {
                int skillId = frame == 11u ? DashTuning.DashSkillId : 0;
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero, skillId);
            }

            return worldState.TryGetPlayer(1, out PlayerState state) &&
                   state.KnockbackRemainingFrames == 0 &&
                   state.KnockbackRuntimeBuffId == 0 &&
                   !BuffSystem.HasBuff(state, KnockbackTuning.KnockbackBuffId);
        }

        private static bool RemoteBodyBlocksSelfMovement()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            S8ApplyInitialRemoteSnapshot(simulation, worldState, F(0.9f), Fixed64.Zero);
            for (uint frame = 13u; frame <= 16u; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.One, Fixed64.Zero);
            }

            return worldState.PlayerCount == 1 &&
                   simulation.RemotePlayers.TryGet(2, out _) &&
                   worldState.TryGetPlayer(1, out PlayerState self) &&
                   self.X <= F(0.0001f) &&
                   worldState.PhysicsWorld.TryGetBodySnapshot(2, out _);
        }

        private static bool RemoteBodyNotDisplacedByOccupancy()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            S8ApplyInitialRemoteSnapshot(simulation, worldState, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.PhysicsWorld.TryGetBodySnapshot(2, out PhysicsBodySnapshot before))
            {
                return false;
            }

            for (uint frame = 13u; frame <= 17u; frame++)
            {
                simulation.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            }

            return worldState.PhysicsWorld.TryGetBodySnapshot(2, out PhysicsBodySnapshot after) &&
                   after.PositionX == before.PositionX &&
                   after.PositionY == before.PositionY;
        }

        private static bool RemoteBodyMirrorSurvivesRestoreSelfOnly()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            S8ApplyInitialRemoteSnapshot(simulation, worldState, (Fixed64)3, Fixed64.Zero);
            simulation.Tick(13u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot predicted13 = worldState.TakeSnapshot().Players[0];
            PlayerStateSnapshot mismatchedSelf = S8CopyPlayer(predicted13, x: predicted13.X + F(0.25f));
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    13u,
                    new[]
                    {
                        mismatchedSelf,
                        new PlayerStateSnapshot(2, (Fixed64)3, Fixed64.Zero)
                    }),
                13u);
            TickResult result = simulation.Tick(
                14u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);
            return result.ConsistencyMismatch &&
                   worldState.PhysicsWorld.TryGetBodySnapshot(2, out PhysicsBodySnapshot remoteBody) &&
                   remoteBody.PositionX == (Fixed64)3;
        }

        private static bool RemoteBodyRemovedWithBufferEntry()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            S8ApplyInitialRemoteSnapshot(simulation, worldState, (Fixed64)3, Fixed64.Zero);
            simulation.Tick(13u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot self13 = worldState.TakeSnapshot().Players[0];
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(13u, new[] { self13 }),
                13u);
            simulation.Tick(14u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            return !simulation.RemotePlayers.TryGet(2, out _) &&
                   !worldState.PhysicsWorld.TryGetBodySnapshot(2, out _);
        }

        private static bool DashDisplacementStateProtoRoundTrip()
        {
            PlayerAttributeSnapshot attributes = PlayerAttributeSnapshot.Default;
            BuffState dashBuff = new BuffState(
                7,
                DashTuning.DashBuffId,
                1,
                1,
                1,
                4,
                25u,
                BuffFlags.Duration | BuffFlags.Dispellable);
            PlayerStateSnapshot player = new PlayerStateSnapshot(
                1,
                F(1.25f),
                F(-2.5f),
                attributes,
                new[] { dashBuff },
                8,
                NumericModifierSnapshot.FromAttributes(attributes),
                2,
                Fixed64.FromRaw(123456789),
                Fixed64.FromRaw(-987654321),
                4,
                7,
                Fixed64.Zero,
                Fixed64.Zero,
                0,
                0,
                null);
            PlayerStateSnapshot decoded = S8ProtoRoundTrip(
                new BattleWorldSnapshot(25u, new[] { player })).Players[0];
            return decoded.DashVelocityX.m_rawValue == player.DashVelocityX.m_rawValue &&
                   decoded.DashVelocityY.m_rawValue == player.DashVelocityY.m_rawValue &&
                   decoded.DashRemainingFrames == player.DashRemainingFrames &&
                   decoded.DashRuntimeBuffId == player.DashRuntimeBuffId &&
                   decoded.ActiveBuffs.Count == 1 &&
                   decoded.ActiveBuffs[0].RuntimeBuffId == dashBuff.RuntimeBuffId;
        }

        private static bool KnockbackStateProtoRoundTrip()
        {
            PlayerAttributeSnapshot attributes = PlayerAttributeSnapshot.Default;
            BuffState knockbackBuff = new BuffState(
                9,
                KnockbackTuning.KnockbackBuffId,
                2,
                1,
                1,
                6,
                26u,
                BuffFlags.Duration | BuffFlags.Dispellable);
            PlayerStateSnapshot player = new PlayerStateSnapshot(
                1,
                Fixed64.Zero,
                Fixed64.Zero,
                attributes,
                new[] { knockbackBuff },
                10,
                NumericModifierSnapshot.FromAttributes(attributes),
                1,
                Fixed64.Zero,
                Fixed64.Zero,
                0,
                0,
                Fixed64.FromRaw(long.MaxValue - 100),
                Fixed64.FromRaw(long.MinValue + 100),
                6,
                9,
                null);
            PlayerStateSnapshot decoded = S8ProtoRoundTrip(
                new BattleWorldSnapshot(26u, new[] { player })).Players[0];
            return decoded.KnockbackVelocityX.m_rawValue == player.KnockbackVelocityX.m_rawValue &&
                   decoded.KnockbackVelocityY.m_rawValue == player.KnockbackVelocityY.m_rawValue &&
                   decoded.KnockbackRemainingFrames == player.KnockbackRemainingFrames &&
                   decoded.KnockbackRuntimeBuffId == player.KnockbackRuntimeBuffId;
        }

        private static bool NumericStaminaBaseProtoRoundTrip()
        {
            PlayerAttributeSnapshot attributes = new PlayerAttributeSnapshot(100, 110, 40, 50, 12, 17, 77);
            NumericModifierSnapshot numeric = new NumericModifierSnapshot(
                attributes,
                new[]
                {
                    new NumericModifier(3, ModifierValueType.Flat, AttributeKind.MaxStamina, 5)
                });
            PlayerStateSnapshot player = new PlayerStateSnapshot(
                1,
                Fixed64.Zero,
                Fixed64.Zero,
                new PlayerAttributeSnapshot(100, 110, 40, 50, 12, 17, 82),
                Array.Empty<BuffState>(),
                1,
                numeric);
            PlayerStateSnapshot decoded = S8ProtoRoundTrip(
                new BattleWorldSnapshot(27u, new[] { player })).Players[0];
            if (decoded.Numeric.BaseAttributes.Stamina != 17 ||
                decoded.Numeric.BaseAttributes.MaxStamina != 77 ||
                decoded.Stamina != 17 ||
                decoded.MaxStamina != 82)
            {
                return false;
            }

            PlayerState missingBase = new PlayerState(1, Fixed64.Zero, Fixed64.Zero, player.Attributes);
            missingBase.RestoreRuntimeState(
                Array.Empty<BuffState>(),
                1,
                NumericModifierSnapshot.FromAttributes(new PlayerAttributeSnapshot(100, 110, 40, 50, 12, 0, 0)));
            return missingBase.Stamina == 0 && missingBase.MaxStamina == 0;
        }

        private static bool StaminaMismatchTriggersRollback()
        {
            BattleWorldState worldState = new BattleWorldState();
            BattleSimulation simulation = CreateSimulation(worldState, out _, out _);
            simulation.SetJoined(1, 10u, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState state))
            {
                return false;
            }

            state.Stamina = 50;
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot predicted = worldState.TakeSnapshot().Players[0];
            PlayerAttributeSnapshot authoritativeAttributes = new PlayerAttributeSnapshot(
                predicted.Health,
                predicted.MaxHealth,
                predicted.Mana,
                predicted.MaxMana,
                predicted.Attack,
                predicted.Stamina + 5,
                predicted.MaxStamina);
            PlayerStateSnapshot authoritative = S8CopyPlayer(predicted, attributes: authoritativeAttributes);
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(11u, new[] { authoritative }),
                11u);
            TickResult result = simulation.Tick(
                12u,
                DeterminismRules.FixedDeltaTimeFixed64,
                Fixed64.Zero,
                Fixed64.Zero);
            return result.ConsistencyMismatch &&
                   simulation.StateMismatchCount == 1 &&
                   simulation.StaminaMismatchCount == 1 &&
                   simulation.RollbackCount == 1;
        }

        private static BattleWorldSnapshot S8CaptureWorld(BattleWorldState worldState, uint frameIndex)
        {
            BattleWorldSnapshot snapshot = worldState.TakeSnapshot();
            return new BattleWorldSnapshot(frameIndex, snapshot.Players, snapshot.PhysicsSnapshot);
        }

        private static void S8ApplyInitialRemoteSnapshot(
            BattleSimulation simulation,
            BattleWorldState worldState,
            Fixed64 remoteX,
            Fixed64 remoteY)
        {
            simulation.Tick(11u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
            PlayerStateSnapshot self = worldState.TakeSnapshot().Players[0];
            simulation.EnqueueServerSnapshot(
                new BattleWorldSnapshot(
                    11u,
                    new[]
                    {
                        self,
                        new PlayerStateSnapshot(2, remoteX, remoteY)
                    }),
                11u);
            simulation.Tick(12u, DeterminismRules.FixedDeltaTimeFixed64, Fixed64.Zero, Fixed64.Zero);
        }

        private static BattleWorldSnapshot S8ProtoRoundTrip(BattleWorldSnapshot snapshot)
        {
            Fantasy.S2C_FrameSnapshot wire = BattleSnapshotProtocolMapper.ToFullSyncProto(snapshot);
            EnsureProtoSerializer();
            byte[] payload = SerializerManager.ProtoBufHelper.Serialize(typeof(Fantasy.S2C_FrameSnapshot), wire);
            Fantasy.S2C_FrameSnapshot decodedWire = (Fantasy.S2C_FrameSnapshot)SerializerManager.ProtoBufHelper.Deserialize(
                typeof(Fantasy.S2C_FrameSnapshot),
                payload);
            return BattleSnapshotProtocolMapper.FromFullSyncProto(decodedWire);
        }

#if !FANTASY_UNITY
        private static void S8JoinCollisionPair(
            Fantasy.BattleLogic battleLogic,
            Fixed64 leftX,
            Fixed64 rightX)
        {
            battleLogic.JoinPlayer(1, leftX, Fixed64.Zero);
            battleLogic.JoinPlayer(2, rightX, Fixed64.Zero);
        }

        private static void S8SubmitDash(
            Fantasy.BattleLogic battleLogic,
            long playerId,
            uint frameIndex,
            Fixed64 directionX,
            Fixed64 directionY)
        {
            battleLogic.SubmitInput(
                playerId,
                frameIndex,
                frameIndex + 1u,
                directionX.m_rawValue,
                directionY.m_rawValue,
                DashTuning.DashSkillId);
        }
#endif

        private static long S8AddDisplacementBuff(
            PlayerState state,
            int buffId,
            Fixed64 velocityX,
            Fixed64 velocityY,
            int durationFrames,
            uint frameIndex)
        {
            return BuffSystem.AddBuff(
                state,
                new ApplyBuffCommand
                {
                    CasterId = state.PlayerId,
                    TargetId = state.PlayerId,
                    BuffId = buffId,
                    DurationFrames = durationFrames,
                    StackCount = 1,
                    FrameIndex = frameIndex,
                    Flags = BuffFlags.Duration | BuffFlags.Dispellable,
                    HasDisplacementVelocityOverride = true,
                    DisplacementVelocityX = velocityX,
                    DisplacementVelocityY = velocityY
                },
                new DefaultBuffConfigProvider());
        }

        private static bool S8DashDisplacementIsClear(PlayerState state)
        {
            return state.DashVelocityX == Fixed64.Zero &&
                   state.DashVelocityY == Fixed64.Zero &&
                   state.DashRemainingFrames == 0 &&
                   state.DashRuntimeBuffId == 0;
        }

        private static RuntimeSkillGraph S8CreateDelayThenBuffGraph(
            int skillId,
            int buffId,
            int delayFrames)
        {
            return new RuntimeSkillGraph
            {
                Version = RuntimeSkillGraph.CurrentVersion,
                SkillName = skillId.ToString(),
                SyncMode = RuntimeSyncModes.Lockstep,
                DeterministicFlags = new List<string>
                {
                    "Delay.FrameStep",
                    "Action.CommandOnly",
                    "Trace.ExecutionEventsV1"
                },
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
                                Key = RuntimePropertyKeys.DurationFrames,
                                Value = delayFrames.ToString()
                            }
                        }
                    },
                    new RuntimeSkillNode
                    {
                        NodeId = 2,
                        NodeType = RuntimeNodeTypes.ApplyBuff,
                        Properties = new List<RuntimeProperty>
                        {
                            new RuntimeProperty { Key = RuntimePropertyKeys.TargetSelector, Value = RuntimeBuffTargetSelectors.Target },
                            new RuntimeProperty { Key = RuntimePropertyKeys.BuffId, Value = buffId.ToString() },
                            new RuntimeProperty { Key = RuntimePropertyKeys.DurationFrames, Value = "5" },
                            new RuntimeProperty { Key = RuntimePropertyKeys.StackCount, Value = "1" }
                        }
                    }
                },
                Connections = new List<RuntimeConnection>
                {
                    new RuntimeConnection { FromNodeId = 0, FromPort = "Next", ToNodeId = 1 },
                    new RuntimeConnection { FromNodeId = 1, FromPort = "Out", ToNodeId = 2 }
                }
            };
        }

        private static PlayerStateSnapshot S8CopyPlayer(
            PlayerStateSnapshot source,
            Fixed64? x = null,
            Fixed64? y = null,
            PlayerAttributeSnapshot? attributes = null,
            int? staminaRegenCounterFrames = null)
        {
            PlayerAttributeSnapshot resolvedAttributes = attributes ?? source.Attributes;
            NumericModifierSnapshot numeric = attributes.HasValue
                ? NumericModifierSnapshot.FromAttributes(resolvedAttributes)
                : source.Numeric;
            return new PlayerStateSnapshot(
                source.PlayerId,
                x ?? source.X,
                y ?? source.Y,
                resolvedAttributes,
                source.ActiveBuffs,
                source.NextRuntimeBuffId,
                numeric,
                staminaRegenCounterFrames ?? source.StaminaRegenCounterFrames,
                source.DashVelocityX,
                source.DashVelocityY,
                source.DashRemainingFrames,
                source.DashRuntimeBuffId,
                source.KnockbackVelocityX,
                source.KnockbackVelocityY,
                source.KnockbackRemainingFrames,
                source.KnockbackRuntimeBuffId,
                source.SkillExecutions);
        }

        private sealed class S8BuffCommandSink : IBuffCommandSink
        {
            public List<ApplyBuffCommand> ApplyCommands { get; } = new List<ApplyBuffCommand>();

            public void EnqueueApplyBuff(ApplyBuffCommand command)
            {
                ApplyCommands.Add(new ApplyBuffCommand
                {
                    CasterId = command.CasterId,
                    TargetId = command.TargetId,
                    BuffId = command.BuffId,
                    DurationFrames = command.DurationFrames,
                    StackCount = command.StackCount,
                    FrameIndex = command.FrameIndex,
                    Flags = command.Flags,
                    HasDisplacementVelocityOverride = command.HasDisplacementVelocityOverride,
                    DisplacementVelocityX = command.DisplacementVelocityX,
                    DisplacementVelocityY = command.DisplacementVelocityY
                });
            }

            public void EnqueueRemoveBuff(RemoveBuffCommand command)
            {
            }

            public bool HasBuff(long targetId, int buffId)
            {
                return false;
            }

            public int GetBuffStackCount(long targetId, int buffId)
            {
                return 0;
            }

            public bool ContainsApply(long targetId, int buffId)
            {
                for (int i = 0; i < ApplyCommands.Count; i++)
                {
                    ApplyBuffCommand command = ApplyCommands[i];
                    if (command.TargetId == targetId && command.BuffId == buffId)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
