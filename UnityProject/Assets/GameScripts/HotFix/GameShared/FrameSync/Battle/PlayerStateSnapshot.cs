using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public readonly struct PlayerStateSnapshot
    {
        public PlayerStateSnapshot(long playerId, float x, float y)
            : this(playerId, x, y, PlayerAttributeSnapshot.Default)
        {
        }

        public PlayerStateSnapshot(long playerId, float x, float y, PlayerAttributeSnapshot attributes)
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
            float x,
            float y,
            PlayerAttributeSnapshot attributes,
            IReadOnlyList<BuffState> activeBuffs,
            long nextRuntimeBuffId,
            NumericModifierSnapshot numeric)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
            Attributes = attributes;
            ActiveBuffs = CopyBuffs(activeBuffs);
            NextRuntimeBuffId = nextRuntimeBuffId > 0 ? nextRuntimeBuffId : 1;
            Numeric = numeric;
        }

        public long PlayerId { get; }
        public float X { get; }
        public float Y { get; }
        public PlayerAttributeSnapshot Attributes { get; }
        public IReadOnlyList<BuffState> ActiveBuffs { get; }
        public long NextRuntimeBuffId { get; }
        public NumericModifierSnapshot Numeric { get; }
        public int Health => Attributes.Health;
        public int MaxHealth => Attributes.MaxHealth;
        public int Mana => Attributes.Mana;
        public int MaxMana => Attributes.MaxMana;
        public int Attack => Attributes.Attack;

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
