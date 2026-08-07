using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.SkillGraph;

namespace GameShared.FrameSync.Battle
{
    public readonly struct PlayerStateSnapshot
    {
        public PlayerStateSnapshot(long playerId, float x, float y)
            : this(playerId, (Fixed64)x, (Fixed64)y)
        {
        }

        public PlayerStateSnapshot(long playerId, float x, float y, PlayerAttributeSnapshot attributes)
            : this(playerId, (Fixed64)x, (Fixed64)y, attributes)
        {
        }

        public PlayerStateSnapshot(
            long playerId,
            float x,
            float y,
            PlayerAttributeSnapshot attributes,
            IReadOnlyList<BuffState> activeBuffs,
            long nextRuntimeBuffId,
            NumericModifierSnapshot numeric)
            : this(playerId, (Fixed64)x, (Fixed64)y, attributes, activeBuffs, nextRuntimeBuffId, numeric)
        {
        }

        public PlayerStateSnapshot(long playerId, Fixed64 x, Fixed64 y)
            : this(playerId, x, y, PlayerAttributeSnapshot.Default)
        {
        }

        public PlayerStateSnapshot(long playerId, Fixed64 x, Fixed64 y, PlayerAttributeSnapshot attributes)
            : this(
                playerId,
                x,
                y,
                attributes,
                Array.Empty<BuffState>(),
                1,
                NumericModifierSnapshot.FromAttributes(attributes))
        {
        }

        public PlayerStateSnapshot(
            long playerId,
            Fixed64 x,
            Fixed64 y,
            PlayerAttributeSnapshot attributes,
            IReadOnlyList<BuffState> activeBuffs,
            long nextRuntimeBuffId,
            NumericModifierSnapshot numeric)
            : this(
                playerId,
                x,
                y,
                attributes,
                activeBuffs,
                nextRuntimeBuffId,
                numeric,
                0,
                Fixed64.Zero,
                Fixed64.Zero,
                0,
                0,
                Fixed64.Zero,
                Fixed64.Zero,
                0,
                0,
                null)
        {
        }

        public PlayerStateSnapshot(
            long playerId,
            Fixed64 x,
            Fixed64 y,
            PlayerAttributeSnapshot attributes,
            IReadOnlyList<BuffState> activeBuffs,
            long nextRuntimeBuffId,
            NumericModifierSnapshot numeric,
            int staminaRegenCounterFrames,
            Fixed64 dashVelocityX,
            Fixed64 dashVelocityY,
            int dashRemainingFrames,
            long dashRuntimeBuffId,
            Fixed64 knockbackVelocityX,
            Fixed64 knockbackVelocityY,
            int knockbackRemainingFrames,
            long knockbackRuntimeBuffId,
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> skillExecutions)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
            Attributes = attributes;
            ActiveBuffs = CopyBuffs(activeBuffs);
            NextRuntimeBuffId = nextRuntimeBuffId > 0 ? nextRuntimeBuffId : 1;
            Numeric = numeric;
            StaminaRegenCounterFrames = staminaRegenCounterFrames < 0 ? 0 : staminaRegenCounterFrames;
            DashVelocityX = dashVelocityX;
            DashVelocityY = dashVelocityY;
            DashRemainingFrames = dashRemainingFrames < 0 ? 0 : dashRemainingFrames;
            DashRuntimeBuffId = dashRuntimeBuffId;
            KnockbackVelocityX = knockbackVelocityX;
            KnockbackVelocityY = knockbackVelocityY;
            KnockbackRemainingFrames = knockbackRemainingFrames < 0 ? 0 : knockbackRemainingFrames;
            KnockbackRuntimeBuffId = knockbackRuntimeBuffId;
            SkillExecutions = SkillExecutionSnapshotCodec.CloneExecutions(skillExecutions);
        }

        public long PlayerId { get; }
        public Fixed64 X { get; }
        public Fixed64 Y { get; }
        public PlayerAttributeSnapshot Attributes { get; }
        public IReadOnlyList<BuffState> ActiveBuffs { get; }
        public long NextRuntimeBuffId { get; }
        public NumericModifierSnapshot Numeric { get; }
        public int Health => Attributes.Health;
        public int MaxHealth => Attributes.MaxHealth;
        public int Mana => Attributes.Mana;
        public int MaxMana => Attributes.MaxMana;
        public int Attack => Attributes.Attack;
        public int Stamina => Attributes.Stamina;
        public int MaxStamina => Attributes.MaxStamina;
        public int StaminaRegenCounterFrames { get; }
        public Fixed64 DashVelocityX { get; }
        public Fixed64 DashVelocityY { get; }
        public int DashRemainingFrames { get; }
        public long DashRuntimeBuffId { get; }
        public Fixed64 KnockbackVelocityX { get; }
        public Fixed64 KnockbackVelocityY { get; }
        public int KnockbackRemainingFrames { get; }
        public long KnockbackRuntimeBuffId { get; }
        public IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> SkillExecutions { get; }

        private static BuffState[] CopyBuffs(IReadOnlyList<BuffState> activeBuffs)
        {
            if (activeBuffs == null || activeBuffs.Count == 0)
            {
                return Array.Empty<BuffState>();
            }

            BuffState[] copy = new BuffState[activeBuffs.Count];
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                copy[i] = activeBuffs[i];
            }

            return copy;
        }
    }
}
