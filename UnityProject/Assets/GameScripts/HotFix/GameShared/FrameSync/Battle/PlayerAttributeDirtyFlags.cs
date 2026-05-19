using System;

namespace GameShared.FrameSync.Battle
{
    [Flags]
    public enum PlayerAttributeDirtyFlags : uint
    {
        None = 0,
        Health = 1 << 0,
        MaxHealth = 1 << 1,
        Mana = 1 << 2,
        MaxMana = 1 << 3,
        Attack = 1 << 4,
        All = Health | MaxHealth | Mana | MaxMana | Attack
    }
}
