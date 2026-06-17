using System;
using FixedMathSharp;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Battle
{
    public static class MoveSystem
    {
        public static void Apply(BattleWorldState worldState, PlayerState state, float dx, float dy, float dt)
        {
            Apply(worldState, state, (Fixed64)dx, (Fixed64)dy, (Fixed64)dt);
        }

        public static void Apply(BattleWorldState worldState, PlayerState state, Fixed64 dx, Fixed64 dy, Fixed64 dt)
        {
            if (worldState == null)
            {
                throw new ArgumentNullException(nameof(worldState));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            DeterminismRules.AssertFixedDt(dt);

            worldState.PhysicsWorld.SetBodyMovementInput(checked((int)state.PlayerId), dx, dy);
        }

        public static void SyncFromPhysics(BattleWorldState worldState, PlayerState state)
        {
            if (worldState == null)
            {
                throw new ArgumentNullException(nameof(worldState));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!worldState.PhysicsWorld.TryGetBodySnapshot(checked((int)state.PlayerId), out PhysicsBodySnapshot bodySnapshot))
            {
                return;
            }

            state.X = bodySnapshot.PositionX;
            state.Y = bodySnapshot.PositionY;
        }
    }
}
