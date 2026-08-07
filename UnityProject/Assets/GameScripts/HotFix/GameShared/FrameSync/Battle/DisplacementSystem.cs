using System;
using System.Collections.Generic;
using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    public static class DisplacementSystem
    {
        public static void ApplyAll(IEnumerable<PlayerState> states, IPhysicsMovementWorld physicsWorld)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            foreach (PlayerState state in states)
            {
                Apply(state, physicsWorld);
            }
        }

        public static void DecayAll(IEnumerable<PlayerState> states)
        {
            if (states == null)
            {
                throw new ArgumentNullException(nameof(states));
            }

            foreach (PlayerState state in states)
            {
                Decay(state);
            }
        }

        public static void Apply(PlayerState state, IPhysicsMovementWorld physicsWorld)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (physicsWorld == null)
            {
                throw new ArgumentNullException(nameof(physicsWorld));
            }

            Fixed64 velocityX = Fixed64.Zero;
            Fixed64 velocityY = Fixed64.Zero;
            if (state.DashRemainingFrames > 0)
            {
                velocityX += state.DashVelocityX;
                velocityY += state.DashVelocityY;
            }

            if (state.KnockbackRemainingFrames > 0)
            {
                velocityX += state.KnockbackVelocityX;
                velocityY += state.KnockbackVelocityY;
            }

            if (velocityX == Fixed64.Zero && velocityY == Fixed64.Zero)
            {
                return;
            }

            physicsWorld.ApplyBodyImpulse(checked((int)state.PlayerId), velocityX, velocityY);
        }

        public static void Decay(PlayerState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            DecayDash(state);
            DecayKnockback(state);
        }

        private static void DecayDash(PlayerState state)
        {
            if (state.DashRemainingFrames <= 0)
            {
                return;
            }

            state.DashRemainingFrames--;
            if (state.DashRemainingFrames == 0)
            {
                state.ClearDisplacement(DisplacementKind.Dash);
                return;
            }

            state.DashVelocityX = DecayVelocity(
                state.DashVelocityX,
                DashTuning.DashDecayNumerator,
                DashTuning.DashDecayDenominator);
            state.DashVelocityY = DecayVelocity(
                state.DashVelocityY,
                DashTuning.DashDecayNumerator,
                DashTuning.DashDecayDenominator);
        }

        private static void DecayKnockback(PlayerState state)
        {
            if (state.KnockbackRemainingFrames <= 0)
            {
                return;
            }

            state.KnockbackRemainingFrames--;
            if (state.KnockbackRemainingFrames == 0)
            {
                state.ClearDisplacement(DisplacementKind.Knockback);
                return;
            }

            state.KnockbackVelocityX = DecayVelocity(
                state.KnockbackVelocityX,
                KnockbackTuning.DecayNumerator,
                KnockbackTuning.DecayDenominator);
            state.KnockbackVelocityY = DecayVelocity(
                state.KnockbackVelocityY,
                KnockbackTuning.DecayNumerator,
                KnockbackTuning.DecayDenominator);
        }

        private static Fixed64 DecayVelocity(Fixed64 value, int numerator, int denominator)
        {
            return (value * (Fixed64)numerator) / (Fixed64)denominator;
        }
    }
}
