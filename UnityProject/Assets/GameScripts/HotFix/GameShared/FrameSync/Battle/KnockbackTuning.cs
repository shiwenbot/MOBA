using System;
using FixedMathSharp;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Battle
{
    public static class KnockbackTuning
    {
        public const int KnockbackBuffId = 9303;
        public const int KnockbackSpeed = 12;
        public const int KnockbackFrames = 8;
        public const int DecayNumerator = 3;
        public const int DecayDenominator = 4;
        public const float MaxSmoothingDistance = 6.0f;

        public static readonly Fixed64 KnockbackSpeedFixed = (Fixed64)KnockbackSpeed;
        public static readonly Fixed64 EstimatedTotalDisplacement = CalculateTotalDisplacement();
        public static readonly Fixed64 EstimatedWorstCaseDeviation =
            EstimatedTotalDisplacement + (Fixed64)1.3f;

        static KnockbackTuning()
        {
            if (DecayNumerator < 0 || DecayDenominator <= 0 || KnockbackFrames <= 0)
            {
                throw new InvalidOperationException("Knockback tuning contains an invalid decay or duration.");
            }

            if (EstimatedWorstCaseDeviation >= (Fixed64)MaxSmoothingDistance)
            {
                throw new InvalidOperationException(
                    $"Knockback deviation {EstimatedWorstCaseDeviation} must remain below smoothing threshold {MaxSmoothingDistance}.");
            }
        }

        public static void Validate()
        {
            _ = EstimatedWorstCaseDeviation;
        }

        private static Fixed64 CalculateTotalDisplacement()
        {
            Fixed64 total = Fixed64.Zero;
            Fixed64 velocity = KnockbackSpeedFixed;
            for (int i = 0; i < KnockbackFrames; i++)
            {
                total += velocity * DeterminismRules.FixedDeltaTimeFixed64;
                velocity = (velocity * (Fixed64)DecayNumerator) / (Fixed64)DecayDenominator;
            }

            return total;
        }
    }
}
