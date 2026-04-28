using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Snapshot
{
    public sealed class SnapshotBuffer<TSnapshot>
    {
        private readonly Dictionary<uint, TSnapshot> _snapshots;
        private readonly Queue<uint> _insertionOrder;

        public SnapshotBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Snapshot buffer capacity must be positive.");
            }

            Capacity = capacity;
            _snapshots = new Dictionary<uint, TSnapshot>(capacity);
            _insertionOrder = new Queue<uint>(capacity);
        }

        public int Capacity { get; }
        public int Count => _snapshots.Count;

        public void Save(uint frameIndex, TSnapshot snapshot)
        {
            if (_snapshots.ContainsKey(frameIndex))
            {
                _snapshots[frameIndex] = snapshot;
                return;
            }

            if (_snapshots.Count >= Capacity)
            {
                EvictOldest();
            }

            _snapshots[frameIndex] = snapshot;
            _insertionOrder.Enqueue(frameIndex);
        }

        public bool TryGet(uint frameIndex, out TSnapshot snapshot)
        {
            return _snapshots.TryGetValue(frameIndex, out snapshot);
        }

        public void Clear()
        {
            _snapshots.Clear();
            _insertionOrder.Clear();
        }

        private void EvictOldest()
        {
            while (_insertionOrder.Count > 0)
            {
                uint oldestFrameIndex = _insertionOrder.Dequeue();
                if (_snapshots.Remove(oldestFrameIndex))
                {
                    return;
                }
            }
        }
    }
}
