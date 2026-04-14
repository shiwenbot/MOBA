using System;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Battle
{
    public static class MoveSystem
    {
        public static void Apply(PlayerState state, float dx, float dy, float dt)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            DeterminismRules.AssertFinite(dx, nameof(dx));
            DeterminismRules.AssertFinite(dy, nameof(dy));
            DeterminismRules.AssertFinite(dt, nameof(dt));

            float lenSq = dx * dx + dy * dy;
            if (lenSq > 1.0f)
            {
                float invLen = 1.0f / MathF.Sqrt(lenSq);
                dx *= invLen;
                dy *= invLen;
            }

            state.X += dx * DeterminismRules.MoveSpeed * dt;
            state.Y += dy * DeterminismRules.MoveSpeed * dt;
        }
    }
}
