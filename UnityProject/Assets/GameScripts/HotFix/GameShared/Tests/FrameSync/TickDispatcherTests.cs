#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using GameShared.FrameSync.Core;
using NUnit.Framework;

namespace GameShared.FrameSync.Tests
{
    [TestFixture]
    public sealed class TickDispatcherTests
    {
        [Test]
        public void Update_ExecutesTickablesByPriorityAndFrame()
        {
            RecordingLogger logger = new RecordingLogger();
            TickDispatcher dispatcher = new TickDispatcher(logger: logger);
            List<string> calls = new List<string>();

            dispatcher.Register(new MockTickable("Late", 20, calls));
            dispatcher.Register(new MockTickable("Early", 10, calls));

            dispatcher.Update(TickAccumulator.DefaultFixedDeltaTime * 3.0f);

            Assert.That(
                calls,
                Is.EqualTo(new[]
                {
                    "Early:0",
                    "Late:0",
                    "Early:1",
                    "Late:1",
                    "Early:2",
                    "Late:2"
                }));
            Assert.That(logger.Errors, Is.Empty);
        }

        [Test]
        public void Update_DefersRegisterAndUnregisterUntilFrameEnd()
        {
            TickDispatcher dispatcher = new TickDispatcher();
            List<string> calls = new List<string>();

            MockTickable tickableB = new MockTickable("B", 5, calls);
            MockTickable tickableC = new MockTickable("C", 10, calls);
            MockTickable tickableA = new MockTickable("A", 0, calls);

            tickableA.OnTick = (_, _) =>
            {
                dispatcher.Unregister(tickableB);
                dispatcher.Register(tickableC);
            };

            dispatcher.Register(tickableA);
            dispatcher.Register(tickableB);
            dispatcher.Update(TickAccumulator.DefaultFixedDeltaTime * 2.0f);

            Assert.That(
                calls,
                Is.EqualTo(new[]
                {
                    "A:0",
                    "B:0",
                    "A:1",
                    "C:1"
                }));
        }

        [Test]
        public void Update_ContinuesWhenTickableThrows()
        {
            RecordingLogger logger = new RecordingLogger();
            TickDispatcher dispatcher = new TickDispatcher(logger: logger);
            CounterTickable healthyTickable = new CounterTickable(1);
            ThrowingTickable brokenTickable = new ThrowingTickable(0);

            dispatcher.Register(brokenTickable);
            dispatcher.Register(healthyTickable);
            dispatcher.Update(TickAccumulator.DefaultFixedDeltaTime);

            Assert.That(healthyTickable.Ticks, Is.EqualTo(1));
            Assert.That(logger.Errors.Count, Is.EqualTo(1));
        }

        private sealed class MockTickable : ITickable
        {
            private readonly string _name;
            private readonly List<string> _calls;

            public MockTickable(string name, int priority, List<string> calls)
            {
                _name = name;
                _calls = calls;
                Priority = priority;
            }

            public int Priority { get; }
            public Action<uint, float> OnTick { get; set; }

            public void Tick(uint frameIndex, float fixedDt)
            {
                _calls.Add($"{_name}:{frameIndex}");
                OnTick?.Invoke(frameIndex, fixedDt);
            }
        }

        private sealed class CounterTickable : ITickable
        {
            public CounterTickable(int priority)
            {
                Priority = priority;
            }

            public int Priority { get; }
            public int Ticks { get; private set; }

            public void Tick(uint frameIndex, float fixedDt)
            {
                Ticks++;
            }
        }

        private sealed class ThrowingTickable : ITickable
        {
            public ThrowingTickable(int priority)
            {
                Priority = priority;
            }

            public int Priority { get; }

            public void Tick(uint frameIndex, float fixedDt)
            {
                throw new InvalidOperationException("Tick failed.");
            }
        }

        private sealed class RecordingLogger : IFrameSyncLogger
        {
            public List<string> Errors { get; } = new List<string>();

            public void LogError(Exception exception, string context)
            {
                Errors.Add($"{context}|{exception.GetType().Name}");
            }
        }
    }
}
#endif
