using System;
using GameShared.FrameSync.Battle;

namespace Fantasy;

public readonly struct TestSnapshot
{
    public TestSnapshot(uint frameIndex, PlayerStateSnapshot[] players, PhysicsWorldSnapshot physicsSnapshot = null)
    {
        FrameIndex = frameIndex;
        Players = players ?? Array.Empty<PlayerStateSnapshot>();
        PhysicsSnapshot = physicsSnapshot;
    }

    public uint FrameIndex { get; }
    public PlayerStateSnapshot[] Players { get; }
    public PhysicsWorldSnapshot PhysicsSnapshot { get; }

    public BattleWorldSnapshot ToBattleWorldSnapshot()
    {
        return new BattleWorldSnapshot(FrameIndex, Players, PhysicsSnapshot);
    }

    public static TestSnapshot FromBattleWorldSnapshot(BattleWorldSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        PlayerStateSnapshot[] players = new PlayerStateSnapshot[snapshot.Players.Count];
        for (int i = 0; i < snapshot.Players.Count; i++)
        {
            players[i] = snapshot.Players[i];
        }

        return new TestSnapshot(snapshot.FrameIndex, players, snapshot.PhysicsSnapshot);
    }
}
