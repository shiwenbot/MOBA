using System;
using GameShared.FrameSync.Determinism;
using UnityEngine;

namespace GameShared.Badminton
{
    public static class ShuttlecockPhysics
    {
        public const float Gravity = 9.8f;
        public const float VerticalDrag = 0.5f;

        public static void Step(ShuttlecockState state, float dt, uint frameIndex)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            DeterminismRules.AssertFixedDt(dt);
            EnsureFinite(state);

            if (state.Phase != ShuttlecockFlightPhase.Flying)
            {
                return;
            }

            float horizontalDecay = (float)Math.Exp(-state.HorizontalDrag * dt);
            Vector2 nextVxz = state.Vxz * horizontalDecay;
            Vector2 averageVxz = (state.Vxz + nextVxz) * 0.5f;
            state.XZ += averageVxz * dt;
            state.Vxz = nextVxz;

            float nextVy = (state.Vy * (float)Math.Exp(-VerticalDrag * dt)) - (Gravity * dt);
            float averageVy = (state.Vy + nextVy) * 0.5f;
            state.Y += averageVy * dt;
            state.Vy = nextVy;
            state.LastValidFlyingFrame = unchecked((int)frameIndex);

            if (state.Y > 0.0f)
            {
                EnsureFinite(state);
                return;
            }

            state.Y = 0.0f;
            state.LandingXZ = state.XZ;
            state.IsInBounds = CourtConstants.IsInBounds(state.LandingXZ);
            state.Phase = state.IsInBounds ? ShuttlecockFlightPhase.Landed : ShuttlecockFlightPhase.OutOfBounds;
            state.Vxz = Vector2.zero;
            state.Vy = 0.0f;
            EnsureFinite(state);
        }

        private static void EnsureFinite(ShuttlecockState state)
        {
            DeterminismRules.AssertFinite(state.XZ.x, nameof(state.XZ));
            DeterminismRules.AssertFinite(state.XZ.y, nameof(state.XZ));
            DeterminismRules.AssertFinite(state.Y, nameof(state.Y));
            DeterminismRules.AssertFinite(state.Vxz.x, nameof(state.Vxz));
            DeterminismRules.AssertFinite(state.Vxz.y, nameof(state.Vxz));
            DeterminismRules.AssertFinite(state.Vy, nameof(state.Vy));
            DeterminismRules.AssertFinite(state.HorizontalDrag, nameof(state.HorizontalDrag));
        }
    }
}
