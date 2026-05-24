using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;

namespace Fantasy;

public sealed class BuffBroadcastBaseline
{
    public BuffBroadcastBaseline(IReadOnlyList<BuffState> activeBuffs, long nextRuntimeBuffId, uint frameIndex)
    {
        Update(activeBuffs, nextRuntimeBuffId, frameIndex);
    }

    public IReadOnlyList<BuffState> ActiveBuffs { get; private set; } = Array.Empty<BuffState>();
    public long NextRuntimeBuffId { get; private set; } = 1;
    public uint FrameIndex { get; private set; }

    public void Update(IReadOnlyList<BuffState> activeBuffs, long nextRuntimeBuffId, uint frameIndex)
    {
        ActiveBuffs = BuffSync.CopyBuffs(activeBuffs);
        NextRuntimeBuffId = nextRuntimeBuffId > 0 ? nextRuntimeBuffId : 1;
        FrameIndex = frameIndex;
    }
}
