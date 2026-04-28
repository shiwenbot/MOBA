using System;
using GameShared.FrameSync.Battle;

namespace Fantasy;

public readonly struct TestSnapshot
{
    public TestSnapshot(uint frameIndex, PlayerStateSnapshot[] players)
    {
        FrameIndex = frameIndex;
        Players = players ?? Array.Empty<PlayerStateSnapshot>();
    }

    public uint FrameIndex { get; }
    public PlayerStateSnapshot[] Players { get; }

    public BattleWorldSnapshot ToBattleWorldSnapshot()
    {
        return new BattleWorldSnapshot(FrameIndex, Players);
    }
}
