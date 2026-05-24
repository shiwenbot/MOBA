using System;
using System.Collections.Generic;

namespace GameShared.InputBuffering
{
    /// <summary>
    /// Frame-driven input buffer that supports one-slot and FIFO queue semantics.
    /// </summary>
    public sealed class InputBuffer<TKey, TValue>
    {
        private readonly Dictionary<TKey, Slot> _slots;
        private readonly Dictionary<TKey, Queue<Slot>> _queues;
        private readonly int _maxQueueSize;

        public InputBuffer(int maxQueueSize = 4, IEqualityComparer<TKey> comparer = null)
        {
            if (maxQueueSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxQueueSize), maxQueueSize, "Queue size must be at least 1.");
            }

            _maxQueueSize = maxQueueSize;
            _slots = comparer == null
                ? new Dictionary<TKey, Slot>()
                : new Dictionary<TKey, Slot>(comparer);
            _queues = comparer == null
                ? new Dictionary<TKey, Queue<Slot>>()
                : new Dictionary<TKey, Queue<Slot>>(comparer);
        }

        public bool Has(TKey key)
        {
            if (!_slots.TryGetValue(key, out Slot slot))
            {
                return false;
            }

            if (slot.IsActive)
            {
                return true;
            }

            _slots.Remove(key);
            return false;
        }

        public void Record(TKey key, TValue value, int bufferFrames)
        {
            _slots[key] = CreateSlot(value, bufferFrames);
        }

        public bool TryConsume(TKey key, out TValue value)
        {
            if (_slots.TryGetValue(key, out Slot slot) && slot.IsActive)
            {
                value = slot.Value;
                _slots.Remove(key);
                return true;
            }

            _slots.Remove(key);
            value = default;
            return false;
        }

        public void Enqueue(TKey key, TValue value, int bufferFrames)
        {
            if (!_queues.TryGetValue(key, out Queue<Slot> queue))
            {
                queue = new Queue<Slot>(_maxQueueSize);
                _queues.Add(key, queue);
            }

            if (queue.Count >= _maxQueueSize)
            {
                queue.Dequeue();
            }

            queue.Enqueue(CreateSlot(value, bufferFrames));
        }

        public bool TryDequeue(TKey key, out TValue value)
        {
            if (!_queues.TryGetValue(key, out Queue<Slot> queue))
            {
                value = default;
                return false;
            }

            while (queue.Count > 0)
            {
                Slot slot = queue.Dequeue();
                if (!slot.IsActive)
                {
                    continue;
                }

                value = slot.Value;
                CleanupEmptyQueue(key, queue);
                return true;
            }

            CleanupEmptyQueue(key, queue);
            value = default;
            return false;
        }

        public void TickDecay()
        {
            if (_slots.Count > 0)
            {
                List<TKey> expiredKeys = null;
                foreach (KeyValuePair<TKey, Slot> pair in _slots)
                {
                    pair.Value.Decay();
                    if (!pair.Value.IsActive)
                    {
                        expiredKeys ??= new List<TKey>();
                        expiredKeys.Add(pair.Key);
                    }
                }

                if (expiredKeys != null)
                {
                    for (int i = 0; i < expiredKeys.Count; i++)
                    {
                        _slots.Remove(expiredKeys[i]);
                    }
                }
            }

            if (_queues.Count > 0)
            {
                List<TKey> emptyQueueKeys = null;
                foreach (KeyValuePair<TKey, Queue<Slot>> pair in _queues)
                {
                    Queue<Slot> queue = pair.Value;
                    int remaining = queue.Count;
                    for (int i = 0; i < remaining; i++)
                    {
                        Slot slot = queue.Dequeue();
                        slot.Decay();
                        if (slot.IsActive)
                        {
                            queue.Enqueue(slot);
                        }
                    }

                    if (queue.Count == 0)
                    {
                        emptyQueueKeys ??= new List<TKey>();
                        emptyQueueKeys.Add(pair.Key);
                    }
                }

                if (emptyQueueKeys != null)
                {
                    for (int i = 0; i < emptyQueueKeys.Count; i++)
                    {
                        _queues.Remove(emptyQueueKeys[i]);
                    }
                }
            }
        }

        public void Interrupt(TKey key)
        {
            _slots.Remove(key);
            _queues.Remove(key);
        }

        public void Clear()
        {
            _slots.Clear();
            _queues.Clear();
        }

        private static Slot CreateSlot(TValue value, int bufferFrames)
        {
            if (bufferFrames < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferFrames), bufferFrames, "Buffer frames must be at least 1.");
            }

            return new Slot(value, bufferFrames);
        }

        private void CleanupEmptyQueue(TKey key, Queue<Slot> queue)
        {
            if (queue.Count == 0)
            {
                _queues.Remove(key);
            }
        }

        private sealed class Slot
        {
            public Slot(TValue value, int remainingFrames)
            {
                Value = value;
                RemainingFrames = remainingFrames;
            }

            public TValue Value { get; }
            public int RemainingFrames { get; private set; }
            public bool IsActive => RemainingFrames > 0;

            public void Decay()
            {
                if (RemainingFrames > 0)
                {
                    RemainingFrames--;
                }
            }
        }
    }
}
