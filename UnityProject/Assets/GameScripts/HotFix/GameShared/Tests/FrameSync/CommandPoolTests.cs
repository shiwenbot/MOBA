#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using GameShared.FrameSync.Command;
using NUnit.Framework;

namespace GameShared.FrameSync.Tests
{
    [TestFixture]
    public sealed class CommandPoolTests
    {
        [Test]
        public void Return_ResetsAndReusesObject()
        {
            CommandPool<TestCommand> pool = new CommandPool<TestCommand>();
            TestCommand first = pool.Rent();
            first.Value = 42;

            pool.Return(first);
            TestCommand second = pool.Rent();

            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(second.Value, Is.EqualTo(0));
            Assert.That(second.ResetCount, Is.EqualTo(1));
        }

        [Test]
        public void Pool_CountMatchesReturnOperations()
        {
            CommandPool<TestCommand> pool = new CommandPool<TestCommand>();
            TestCommand[] commands = new TestCommand[10];

            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = pool.Rent();
            }

            for (int i = 0; i < commands.Length; i++)
            {
                pool.Return(commands[i]);
            }

            Assert.That(pool.AvailableCount, Is.EqualTo(10));
        }

        private sealed class TestCommand : IResettable
        {
            public int Value { get; set; }
            public int ResetCount { get; private set; }

            public void Reset()
            {
                Value = 0;
                ResetCount++;
            }
        }
    }
}
#endif
