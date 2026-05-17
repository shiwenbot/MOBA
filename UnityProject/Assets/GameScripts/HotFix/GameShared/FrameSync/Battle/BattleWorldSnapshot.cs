using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public sealed class BattleWorldSnapshot
    {
        public BattleWorldSnapshot(
            uint frameIndex,
            IReadOnlyList<PlayerStateSnapshot> players,
            PhysicsWorldSnapshot physicsSnapshot = null)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            FrameIndex = frameIndex;
            Players = players;
            PhysicsSnapshot = physicsSnapshot;
        }

        public uint FrameIndex { get; }
        public IReadOnlyList<PlayerStateSnapshot> Players { get; }
        public PhysicsWorldSnapshot PhysicsSnapshot { get; }
        public bool HasPhysicsSnapshot => PhysicsSnapshot != null;

        public BattleWorldSnapshot WithFrameIndex(uint frameIndex)
        {
            return new BattleWorldSnapshot(frameIndex, Players, PhysicsSnapshot);
        }
    }
}
