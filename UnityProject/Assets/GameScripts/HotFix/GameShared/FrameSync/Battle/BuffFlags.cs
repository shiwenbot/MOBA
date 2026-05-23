using System;

namespace GameShared.FrameSync.Battle
{
    [Flags]
    public enum BuffFlags : uint
    {
        None = 0,
        Duration = 1 << 0,
        Stackable = 1 << 1,
        Passive = 1 << 2,
        Dispellable = 1 << 3,
        Debuff = 1 << 4
    }
}
