#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using GameShared.InputBuffering;
using NUnit.Framework;

namespace GameShared.InputBuffering.Tests
{
    [TestFixture]
    public sealed class InputBufferTests
    {
        [Test]
        public void Record_ThenTryConsume_ReturnsBufferedValue()
        {
            InputBuffer<int, int> buffer = new InputBuffer<int, int>();
            buffer.Record(1, 99, 2);

            bool consumed = buffer.TryConsume(1, out int value);

            Assert.That(consumed, Is.True);
            Assert.That(value, Is.EqualTo(99));
            Assert.That(buffer.Has(1), Is.False);
        }

        [Test]
        public void TickDecay_ExpiresSlot_WhenFramesRunOut()
        {
            InputBuffer<int, int> buffer = new InputBuffer<int, int>();
            buffer.Record(1, 42, 2);

            buffer.TickDecay();
            bool firstTickConsumed = buffer.TryConsume(1, out int firstTickValue);

            buffer.Record(1, 42, 2);
            buffer.TickDecay();
            buffer.TickDecay();
            bool expiredConsumed = buffer.TryConsume(1, out _);

            Assert.That(firstTickConsumed, Is.True);
            Assert.That(firstTickValue, Is.EqualTo(42));
            Assert.That(expiredConsumed, Is.False);
        }

        [Test]
        public void TryConsume_ReturnsLatestValue_WhenSameKeyRecordedTwice()
        {
            InputBuffer<int, string> buffer = new InputBuffer<int, string>();
            buffer.Record(7, "old", 2);
            buffer.Record(7, "new", 2);

            bool consumed = buffer.TryConsume(7, out string value);

            Assert.That(consumed, Is.True);
            Assert.That(value, Is.EqualTo("new"));
        }

        [Test]
        public void Enqueue_ThenTryDequeue_PreservesFifoOrder()
        {
            InputBuffer<int, string> buffer = new InputBuffer<int, string>();
            buffer.Enqueue(3, "first", 2);
            buffer.Enqueue(3, "second", 2);

            bool firstDequeued = buffer.TryDequeue(3, out string first);
            bool secondDequeued = buffer.TryDequeue(3, out string second);

            Assert.That(firstDequeued, Is.True);
            Assert.That(secondDequeued, Is.True);
            Assert.That(first, Is.EqualTo("first"));
            Assert.That(second, Is.EqualTo("second"));
        }

        [Test]
        public void Enqueue_DropsOldestEntry_WhenQueueExceedsMaxSize()
        {
            InputBuffer<int, string> buffer = new InputBuffer<int, string>(maxQueueSize: 2);
            buffer.Enqueue(5, "first", 2);
            buffer.Enqueue(5, "second", 2);
            buffer.Enqueue(5, "third", 2);

            bool firstDequeued = buffer.TryDequeue(5, out string first);
            bool secondDequeued = buffer.TryDequeue(5, out string second);
            bool thirdDequeued = buffer.TryDequeue(5, out _);

            Assert.That(firstDequeued, Is.True);
            Assert.That(secondDequeued, Is.True);
            Assert.That(thirdDequeued, Is.False);
            Assert.That(first, Is.EqualTo("second"));
            Assert.That(second, Is.EqualTo("third"));
        }

        [Test]
        public void TickDecay_RemovesExpiredQueueEntries()
        {
            InputBuffer<int, string> buffer = new InputBuffer<int, string>();
            buffer.Enqueue(9, "short", 1);
            buffer.Enqueue(9, "long", 2);

            buffer.TickDecay();
            bool dequeued = buffer.TryDequeue(9, out string value);

            Assert.That(dequeued, Is.True);
            Assert.That(value, Is.EqualTo("long"));
        }

        [Test]
        public void Interrupt_RemovesBufferedEntries_ForSpecifiedKey()
        {
            InputBuffer<int, int> buffer = new InputBuffer<int, int>();
            buffer.Record(1, 11, 2);
            buffer.Enqueue(1, 22, 2);

            buffer.Interrupt(1);

            Assert.That(buffer.TryConsume(1, out _), Is.False);
            Assert.That(buffer.TryDequeue(1, out _), Is.False);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            InputBuffer<int, int> buffer = new InputBuffer<int, int>();
            buffer.Record(1, 11, 2);
            buffer.Record(2, 22, 2);
            buffer.Enqueue(3, 33, 2);

            buffer.Clear();

            Assert.That(buffer.TryConsume(1, out _), Is.False);
            Assert.That(buffer.TryConsume(2, out _), Is.False);
            Assert.That(buffer.TryDequeue(3, out _), Is.False);
        }

        [Test]
        public void EmptyBuffer_ReturnsFalse_ForConsumeAndDequeue()
        {
            InputBuffer<int, int> buffer = new InputBuffer<int, int>();

            Assert.That(buffer.TryConsume(1, out _), Is.False);
            Assert.That(buffer.TryDequeue(1, out _), Is.False);
        }
    }
}
#endif
