using System;

namespace GameShared.FrameSync.Battle
{
    [Flags]
    public enum BuffDirtyFlags : uint
    {
        None = 0,
        Added = 1 << 0,
        Removed = 1 << 1,
        Updated = 1 << 2
    }
}
