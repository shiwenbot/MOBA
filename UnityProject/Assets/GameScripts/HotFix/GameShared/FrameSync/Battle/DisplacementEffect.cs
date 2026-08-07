using System;
using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    public enum DisplacementKind : byte
    {
        Dash = 1,
        Knockback = 2
    }

    public readonly struct DisplacementEffect
    {
        public DisplacementEffect(
            DisplacementKind kind,
            Fixed64 velocityX,
            Fixed64 velocityY,
            int decayNumerator,
            int decayDenominator,
            int durationFrames)
        {
            if (decayNumerator < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(decayNumerator));
            }

            if (decayDenominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(decayDenominator));
            }

            if (durationFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationFrames));
            }

            Kind = kind;
            VelocityX = velocityX;
            VelocityY = velocityY;
            DecayNumerator = decayNumerator;
            DecayDenominator = decayDenominator;
            DurationFrames = durationFrames;
        }

        public DisplacementKind Kind { get; }
        public Fixed64 VelocityX { get; }
        public Fixed64 VelocityY { get; }
        public int DecayNumerator { get; }
        public int DecayDenominator { get; }
        public int DurationFrames { get; }

        public DisplacementEffect WithVelocity(Fixed64 velocityX, Fixed64 velocityY)
        {
            return new DisplacementEffect(
                Kind,
                velocityX,
                velocityY,
                DecayNumerator,
                DecayDenominator,
                DurationFrames);
        }
    }
}
