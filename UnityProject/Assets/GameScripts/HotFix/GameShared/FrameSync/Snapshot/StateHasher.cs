using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.SkillGraph;

namespace GameShared.FrameSync.Snapshot
{
    public static class StateHasher
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Hash(BattleWorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerStateSnapshot player = snapshot.Players[i];
                MixInt(ref hash, (int)(player.PlayerId >> 32));
                MixInt(ref hash, (int)player.PlayerId);
                MixLong(ref hash, player.X.m_rawValue);
                MixLong(ref hash, player.Y.m_rawValue);
                MixInt(ref hash, player.Health);
                MixInt(ref hash, player.MaxHealth);
                MixInt(ref hash, player.Mana);
                MixInt(ref hash, player.MaxMana);
                MixInt(ref hash, player.Attack);
                MixInt(ref hash, player.Stamina);
                MixInt(ref hash, player.MaxStamina);
                MixInt(ref hash, player.StaminaRegenCounterFrames);
                MixLong(ref hash, player.DashVelocityX.m_rawValue);
                MixLong(ref hash, player.DashVelocityY.m_rawValue);
                MixInt(ref hash, player.DashRemainingFrames);
                MixLong(ref hash, player.DashRuntimeBuffId);
                MixLong(ref hash, player.KnockbackVelocityX.m_rawValue);
                MixLong(ref hash, player.KnockbackVelocityY.m_rawValue);
                MixInt(ref hash, player.KnockbackRemainingFrames);
                MixLong(ref hash, player.KnockbackRuntimeBuffId);
                MixInt(ref hash, player.ActiveBuffs.Count);
                for (int buffIndex = 0; buffIndex < player.ActiveBuffs.Count; buffIndex++)
                {
                    BuffState buffState = player.ActiveBuffs[buffIndex];
                    MixInt(ref hash, (int)(buffState.RuntimeBuffId >> 32));
                    MixInt(ref hash, (int)buffState.RuntimeBuffId);
                    MixInt(ref hash, buffState.BuffId);
                    MixInt(ref hash, (int)(buffState.CasterId >> 32));
                    MixInt(ref hash, (int)buffState.CasterId);
                    MixInt(ref hash, (int)(buffState.TargetId >> 32));
                    MixInt(ref hash, (int)buffState.TargetId);
                    MixInt(ref hash, buffState.StackCount);
                    MixInt(ref hash, buffState.RemainingFrames);
                    MixInt(ref hash, unchecked((int)buffState.AppliedFrame));
                    MixInt(ref hash, unchecked((int)buffState.Flags));
                }

                MixInt(ref hash, (int)(player.NextRuntimeBuffId >> 32));
                MixInt(ref hash, (int)player.NextRuntimeBuffId);
                MixInt(ref hash, player.Numeric.BaseAttributes.Health);
                MixInt(ref hash, player.Numeric.BaseAttributes.MaxHealth);
                MixInt(ref hash, player.Numeric.BaseAttributes.Mana);
                MixInt(ref hash, player.Numeric.BaseAttributes.MaxMana);
                MixInt(ref hash, player.Numeric.BaseAttributes.Attack);
                MixInt(ref hash, player.Numeric.BaseAttributes.Stamina);
                MixInt(ref hash, player.Numeric.BaseAttributes.MaxStamina);
                MixInt(ref hash, player.Numeric.Count);
                for (int modifierIndex = 0; modifierIndex < player.Numeric.Modifiers.Count; modifierIndex++)
                {
                    NumericModifier modifier = player.Numeric.Modifiers[modifierIndex];
                    MixInt(ref hash, (int)(modifier.SourceBuffId >> 32));
                    MixInt(ref hash, (int)modifier.SourceBuffId);
                    MixInt(ref hash, (int)modifier.ValueType);
                    MixInt(ref hash, (int)modifier.AttributeKind);
                    MixInt(ref hash, modifier.Value);
                }

                MixSkillExecutions(ref hash, player.SkillExecutions);
            }

            if (snapshot.PhysicsSnapshot != null)
            {
                MixInt(ref hash, snapshot.PhysicsSnapshot.Bodies.Count);
                for (int i = 0; i < snapshot.PhysicsSnapshot.Bodies.Count; i++)
                {
                    PhysicsBodySnapshot body = snapshot.PhysicsSnapshot.Bodies[i];
                    MixInt(ref hash, body.BodyId);
                    MixLong(ref hash, body.PositionX.m_rawValue);
                    MixLong(ref hash, body.PositionY.m_rawValue);
                    MixLong(ref hash, body.RotationRadians.m_rawValue);
                    MixLong(ref hash, body.LinearVelocityX.m_rawValue);
                    MixLong(ref hash, body.LinearVelocityY.m_rawValue);
                    MixLong(ref hash, body.AngularVelocity.m_rawValue);
                    MixBool(ref hash, body.IsAwake);
                    MixBool(ref hash, body.IsEnabled);
                }

                MixInt(ref hash, snapshot.PhysicsSnapshot.Contacts.Count);
                for (int i = 0; i < snapshot.PhysicsSnapshot.Contacts.Count; i++)
                {
                    PhysicsContactSnapshot contact = snapshot.PhysicsSnapshot.Contacts[i];
                    MixInt(ref hash, contact.BodyAId);
                    MixInt(ref hash, contact.BodyBId);
                    MixBool(ref hash, contact.IsTouching);
                }
            }

            return hash;
        }

        private static void MixInt(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= FnvPrime;
            }
        }

        private static void MixLong(ref ulong hash, long value)
        {
            MixInt(ref hash, (int)(value >> 32));
            MixInt(ref hash, (int)value);
        }

        private static void MixBool(ref ulong hash, bool value)
        {
            MixInt(ref hash, value ? 1 : 0);
        }

        private static void MixSkillExecutions(
            ref ulong hash,
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> executions)
        {
            int count = executions?.Count ?? 0;
            MixInt(ref hash, count);
            if (count == 0)
            {
                return;
            }

            List<long> casterIds = new List<long>(executions.Keys);
            casterIds.Sort();
            for (int i = 0; i < casterIds.Count; i++)
            {
                ActiveSkillExecutionSnapshot execution = executions[casterIds[i]];
                MixLong(ref hash, execution.CasterId);
                MixLong(ref hash, execution.TargetId);
                MixInt(ref hash, execution.SkillId);
                MixLong(ref hash, execution.DirectionX.m_rawValue);
                MixLong(ref hash, execution.DirectionY.m_rawValue);

                SkillExecutionSnapshot runner = execution.RunnerSnapshot;
                MixInt(ref hash, runner.CurrentNodeId);
                MixInt(ref hash, (int)runner.Status);
                MixInt(ref hash, runner.ExecutedSteps);
                MixInt(ref hash, runner.FrameIndex);
                MixString(ref hash, runner.Message);

                SkillBlackboardSnapshot blackboard = runner.Blackboard;
                MixStringDictionary(ref hash, blackboard?.Strings);
                MixFloatDictionary(ref hash, blackboard?.Floats);
                MixIntDictionary(ref hash, blackboard?.Ints);
                MixBoolDictionary(ref hash, blackboard?.Bools);
                MixDelayDictionary(ref hash, runner.DelayRemainingFrames);
            }
        }

        private static void MixStringDictionary(
            ref ulong hash,
            IReadOnlyDictionary<string, string> values)
        {
            List<string> keys = GetSortedKeys(values);
            MixInt(ref hash, keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                MixString(ref hash, key);
                MixString(ref hash, values[key]);
            }
        }

        private static void MixFloatDictionary(
            ref ulong hash,
            IReadOnlyDictionary<string, float> values)
        {
            List<string> keys = GetSortedKeys(values);
            MixInt(ref hash, keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                MixString(ref hash, key);
                MixInt(ref hash, BitConverter.SingleToInt32Bits(values[key]));
            }
        }

        private static void MixIntDictionary(
            ref ulong hash,
            IReadOnlyDictionary<string, int> values)
        {
            List<string> keys = GetSortedKeys(values);
            MixInt(ref hash, keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                MixString(ref hash, key);
                MixInt(ref hash, values[key]);
            }
        }

        private static void MixBoolDictionary(
            ref ulong hash,
            IReadOnlyDictionary<string, bool> values)
        {
            List<string> keys = GetSortedKeys(values);
            MixInt(ref hash, keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                MixString(ref hash, key);
                MixBool(ref hash, values[key]);
            }
        }

        private static void MixDelayDictionary(
            ref ulong hash,
            IReadOnlyDictionary<int, int> values)
        {
            int count = values?.Count ?? 0;
            MixInt(ref hash, count);
            if (count == 0)
            {
                return;
            }

            List<int> keys = new List<int>(values.Keys);
            keys.Sort();
            for (int i = 0; i < keys.Count; i++)
            {
                MixInt(ref hash, keys[i]);
                MixInt(ref hash, values[keys[i]]);
            }
        }

        private static List<string> GetSortedKeys<TValue>(IReadOnlyDictionary<string, TValue> values)
        {
            List<string> keys = values == null
                ? new List<string>()
                : new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static void MixString(ref ulong hash, string value)
        {
            string normalized = value ?? string.Empty;
            MixInt(ref hash, normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                MixInt(ref hash, normalized[i]);
            }
        }
    }
}
