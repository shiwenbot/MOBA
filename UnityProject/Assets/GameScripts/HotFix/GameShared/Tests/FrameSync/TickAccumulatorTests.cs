#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using GameShared.FrameSync.Core;
using NUnit.Framework;

namespace GameShared.FrameSync.Tests
{
    [TestFixture]
    public sealed class TickAccumulatorTests
    {
        [Test]
        public void Accumulate_Converts60FpsTo30TicksPerSecond()
        {
            TickAccumulator accumulator = new TickAccumulator();
            int totalTicks = 0;

            for (int i = 0; i < 60; i++)
            {
                totalTicks += accumulator.Accumulate(1.0f / 60.0f);
            }

            Assert.That(totalTicks, Is.EqualTo(30));
        }

        [Test]
        public void Accumulate_Converts15FpsToTwoTicksPerFrame()
        {
            TickAccumulator accumulator = new TickAccumulator();
            int totalTicks = 0;

            for (int i = 0; i < 15; i++)
            {
                totalTicks += accumulator.Accumulate(1.0f / 15.0f);
            }

            Assert.That(totalTicks, Is.EqualTo(30));
        }

        [Test]
        public void Accumulate_ClampsStallToMaxTicksPerUpdate()
        {
            TickAccumulator accumulator = new TickAccumulator();
            int tickCount = accumulator.Accumulate(1.0f);

            Assert.That(tickCount, Is.EqualTo(15));
        }
    }
}
#endif
