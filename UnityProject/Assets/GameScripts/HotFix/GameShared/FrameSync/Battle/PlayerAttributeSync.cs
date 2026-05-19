namespace GameShared.FrameSync.Battle
{
    public static class PlayerAttributeSync
    {
        public static PlayerAttributeDirtyFlags ComputeDirtyMask(
            bool hasPrevious,
            PlayerAttributeSnapshot previous,
            PlayerAttributeSnapshot current)
        {
            if (!hasPrevious)
            {
                return PlayerAttributeDirtyFlags.All;
            }

            PlayerAttributeDirtyFlags dirtyMask = PlayerAttributeDirtyFlags.None;
            if (previous.Health != current.Health)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Health;
            }

            if (previous.MaxHealth != current.MaxHealth)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.MaxHealth;
            }

            if (previous.Mana != current.Mana)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Mana;
            }

            if (previous.MaxMana != current.MaxMana)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.MaxMana;
            }

            if (previous.Attack != current.Attack)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Attack;
            }

            return dirtyMask;
        }

        public static PlayerAttributeSnapshot Merge(
            PlayerAttributeSnapshot baseline,
            PlayerAttributeDirtyFlags dirtyMask,
            int health,
            int maxHealth,
            int mana,
            int maxMana,
            int attack)
        {
            return new PlayerAttributeSnapshot(
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Health) ? health : baseline.Health,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.MaxHealth) ? maxHealth : baseline.MaxHealth,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Mana) ? mana : baseline.Mana,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.MaxMana) ? maxMana : baseline.MaxMana,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Attack) ? attack : baseline.Attack);
        }

        public static int SelectSerializedValue(
            PlayerAttributeDirtyFlags dirtyMask,
            PlayerAttributeDirtyFlags flag,
            int value)
        {
            return HasFlag(dirtyMask, flag) ? value : 0;
        }

        public static bool HasFlag(PlayerAttributeDirtyFlags dirtyMask, PlayerAttributeDirtyFlags flag)
        {
            return (dirtyMask & flag) != 0;
        }
    }
}
