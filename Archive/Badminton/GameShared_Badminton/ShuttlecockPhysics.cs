using System;
using FixedMathSharp;
using GameShared.FrameSync.Determinism;

namespace GameShared.Badminton
{
    public static class ShuttlecockPhysics
    {
        public static readonly Fixed64 Gravity = new Fixed64(9.8);
        public static readonly Fixed64 VerticalDrag = new Fixed64(0.5);

        public static void Step(ShuttlecockState state, Fixed64 dt, uint frameIndex)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            DeterminismRules.AssertFixedDt(dt);

            if (state.Phase != ShuttlecockFlightPhase.Flying)
            {
                return;
            }

            Fixed64 horizontalDecay = FixedMath.Exp(-state.HorizontalDrag * dt);
            Vector2d nextVxz = state.Vxz * horizontalDecay;
            Vector2d averageVxz = (state.Vxz + nextVxz) * Fixed64.Half;
            state.XZ += averageVxz * dt;
            state.Vxz = nextVxz;

            Fixed64 nextVy = (state.Vy * FixedMath.Exp(-VerticalDrag * dt)) - (Gravity * dt);
            Fixed64 averageVy = (state.Vy + nextVy) * Fixed64.Half;
            state.Y += averageVy * dt;
            state.Vy = nextVy;
            state.LastValidFlyingFrame = unchecked((int)frameIndex);

            if (state.Y > Fixed64.Zero)
            {
                return;
            }

            state.Y = Fixed64.Zero;
            state.LandingXZ = state.XZ;
            state.IsInBounds = CourtConstants.IsInBounds(state.LandingXZ);
            state.Phase = state.IsInBounds ? ShuttlecockFlightPhase.Landed : ShuttlecockFlightPhase.OutOfBounds;
            state.Vxz = Vector2d.Zero;
            state.Vy = Fixed64.Zero;
        }
    }
}
