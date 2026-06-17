#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Timer;
using NUnit.Framework;

namespace GameShared.FrameSync.Tests
{
    [TestFixture]
    public sealed class FrameTimerServiceTests
    {
        [Test]
        public void Tick_TriggersSingleAndRepeatTimersAtExpectedFrames()
        {
            FrameTimerService timerService = new FrameTimerService();
            List<string> events = new List<string>();

            timerService.AddTimer(3, frame => events.Add($"S:{frame}"));
            timerService.AddTimer(2, frame => events.Add($"R:{frame}"), repeat: true);

            for (uint frame = 0; frame < 10; frame++)
            {
                timerService.Tick(frame, new FixedMathSharp.Fixed64(1) / 30);
            }

            Assert.That(
                events,
                Is.EqualTo(new[]
                {
                    "R:2",
                    "S:3",
                    "R:4",
                    "R:6",
                    "R:8"
                }));
        }

        [Test]
        public void Tick_RemoveInCallbackPreventsLaterSameFrameTrigger()
        {
            FrameTimerService timerService = new FrameTimerService();
            bool secondTriggered = false;
            uint secondId = 0;

            timerService.AddTimer(1, _ => timerService.RemoveTimer(secondId));
            secondId = timerService.AddTimer(1, _ => secondTriggered = true);

            timerService.Tick(1, new FixedMathSharp.Fixed64(1) / 30);

            Assert.That(secondTriggered, Is.False);
        }

        [Test]
        public void Tick_TimerAddedInCallbackDoesNotTriggerInCurrentFrame()
        {
            FrameTimerService timerService = new FrameTimerService();
            uint childTriggeredFrame = uint.MaxValue;

            timerService.AddTimer(1, _ =>
            {
                timerService.AddTimer(0, frame => childTriggeredFrame = frame);
            });

            timerService.Tick(1, new FixedMathSharp.Fixed64(1) / 30);
            timerService.Tick(2, new FixedMathSharp.Fixed64(1) / 30);

            Assert.That(childTriggeredFrame, Is.EqualTo(2));
        }
    }
}
#endif
