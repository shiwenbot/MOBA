#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using FixedMathSharp;
using GameShared.Badminton;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using NUnit.Framework;

namespace GameShared.FixedPoint.Tests
{
    [TestFixture]
    public sealed class FixedMathSharpTests
    {
        [Test]
        public void Fixed64_AccumulationMatchesRawMultiplication()
        {
            Fixed64 step = DeterminismRules.FixedDeltaTimeFixed64;
            Fixed64 accumulated = Fixed64.Zero;

            for (int i = 0; i < 30; i++)
            {
                accumulated += step;
            }

            AssertFixedRawEqual(step * 30, accumulated);
        }

        [Test]
        public void Vector2d_NormalizationIsRawStable()
        {
            Vector2d first = new Vector2d(3, 4).Normal;
            Vector2d second = new Vector2d(3, 4).Normal;

            AssertFixedRawEqual(first.x, second.x);
            AssertFixedRawEqual(first.y, second.y);
            Assert.That((double)first.Magnitude, Is.InRange(0.999, 1.001));
        }

        [Test]
        public void FixedMath_TranscendentalsAreRawStable()
        {
            Fixed64 angle = FixedMath.DegToRad(new Fixed64(55.0));
            Fixed64 sinFirst = FixedMath.Sin(angle);
            Fixed64 sinSecond = FixedMath.Sin(angle);

            Fixed64 drag = FixedMath.Exp(-new Fixed64(0.92) * DeterminismRules.FixedDeltaTimeFixed64);
            Fixed64 dragRepeat = FixedMath.Exp(-new Fixed64(0.92) * DeterminismRules.FixedDeltaTimeFixed64);

            AssertFixedRawEqual(sinFirst, sinSecond);
            AssertFixedRawEqual(drag, dragRepeat);
            Assert.That((double)sinFirst, Is.InRange(0.81, 0.83));
        }

        [Test]
        public void CourtBounds_EvaluatesFixedInputs()
        {
            Assert.That(CourtConstants.IsInBounds(Vector2d.Zero), Is.True);
            Assert.That(
                CourtConstants.IsInBounds(new Vector2d(CourtConstants.HalfSinglesWidth + new Fixed64(0.5), Fixed64.Zero)),
                Is.False);
        }

        [Test]
        public void TickAccumulatorDefaultDelta_MatchesSharedFixedDelta()
        {
            Fixed64 accumulatorDelta = (Fixed64)TickAccumulator.DefaultFixedDeltaTime;

            AssertFixedRawEqual(DeterminismRules.FixedDeltaTimeFixed64, accumulatorDelta);
        }

        private static void AssertFixedRawEqual(Fixed64 expected, Fixed64 actual)
        {
            Assert.That(actual.m_rawValue, Is.EqualTo(expected.m_rawValue));
        }
    }
}
#endif
