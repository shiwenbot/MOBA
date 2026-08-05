using System;
using System.Collections.Generic;
using FixedMathSharp.Utility;

namespace GameShared.FrameSync.Network
{
    public sealed class NetworkConditionSimulator
    {
        public const int DefaultQueueCapacity = 1024;

        private const ulong UplinkSeedSalt = 0xA0761D6478BD642FUL;
        private const ulong DownlinkSeedSalt = 0xE7037ED1A0B428DBUL;

        private readonly List<ScheduledAction> _uplinkQueue;
        private readonly List<ScheduledAction> _downlinkQueue;
        private readonly int _queueCapacity;
        private DeterministicRandom _uplinkRandom;
        private DeterministicRandom _downlinkRandom;
        private long _nextSequence;

        public NetworkConditionSimulator(
            NetworkConditionConfig config,
            int queueCapacity = DefaultQueueCapacity)
        {
            if (queueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(queueCapacity), queueCapacity, "Queue capacity must be positive.");
            }

            _queueCapacity = queueCapacity;
            _uplinkQueue = new List<ScheduledAction>(Math.Min(queueCapacity, 16));
            _downlinkQueue = new List<ScheduledAction>(Math.Min(queueCapacity, 16));
            Configure(config);
        }

        public NetworkConditionConfig Config { get; private set; }
        public int QueueCapacity => _queueCapacity;
        public int UplinkQueueDepth => _uplinkQueue.Count;
        public int DownlinkQueueDepth => _downlinkQueue.Count;
        public long UplinkSent { get; private set; }
        public long UplinkDropped { get; private set; }
        public long DownlinkDelivered { get; private set; }
        public long DownlinkDropped { get; private set; }
        public int MaxQueueDepth { get; private set; }
        public long OverflowDropped { get; private set; }

        public void Configure(NetworkConditionConfig config)
        {
            Config = config;
            _uplinkRandom = new DeterministicRandom(config.Seed ^ UplinkSeedSalt);
            _downlinkRandom = new DeterministicRandom(config.Seed ^ DownlinkSeedSalt);
            ClearPending();
        }

        public bool TryEnqueueUplink(long nowMs, Action send)
        {
            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }

            if (!Config.IsEnabled)
            {
                UplinkSent++;
                send();
                return true;
            }

            if (ShouldDrop(ref _uplinkRandom, Config.UplinkLossPercent))
            {
                UplinkDropped++;
                return false;
            }

            Schedule(
                _uplinkQueue,
                nowMs,
                Config.UplinkDelayMs,
                Config.UplinkJitterMs,
                ref _uplinkRandom,
                send,
                isUplink: true);
            return true;
        }

        public bool TryEnqueueDownlink(long nowMs, Action deliver)
        {
            if (deliver == null)
            {
                throw new ArgumentNullException(nameof(deliver));
            }

            if (!Config.IsEnabled)
            {
                DownlinkDelivered++;
                deliver();
                return true;
            }

            if (ShouldDrop(ref _downlinkRandom, Config.DownlinkLossPercent))
            {
                DownlinkDropped++;
                return false;
            }

            EnqueueAcceptedDownlink(nowMs, deliver);
            return true;
        }

        public bool ShouldDropDownlink(long nowMs)
        {
            _ = nowMs;
            if (!Config.IsEnabled || !ShouldDrop(ref _downlinkRandom, Config.DownlinkLossPercent))
            {
                return false;
            }

            DownlinkDropped++;
            return true;
        }

        public void EnqueueAcceptedDownlink(long nowMs, Action deliver)
        {
            if (deliver == null)
            {
                throw new ArgumentNullException(nameof(deliver));
            }

            if (!Config.IsEnabled)
            {
                DownlinkDelivered++;
                deliver();
                return;
            }

            Schedule(
                _downlinkQueue,
                nowMs,
                Config.DownlinkDelayMs,
                Config.DownlinkJitterMs,
                ref _downlinkRandom,
                deliver,
                isUplink: false);
        }

        public void PumpUplink(long nowMs)
        {
            Pump(_uplinkQueue, nowMs, isUplink: true);
        }

        public void PumpDownlink(long nowMs)
        {
            Pump(_downlinkQueue, nowMs, isUplink: false);
        }

        public void ClearPending()
        {
            _uplinkQueue.Clear();
            _downlinkQueue.Clear();
        }

        public void ResetCounters()
        {
            UplinkSent = 0;
            UplinkDropped = 0;
            DownlinkDelivered = 0;
            DownlinkDropped = 0;
            MaxQueueDepth = 0;
            OverflowDropped = 0;
        }

        private void Schedule(
            List<ScheduledAction> queue,
            long nowMs,
            int delayMs,
            int jitterMs,
            ref DeterministicRandom random,
            Action action,
            bool isUplink)
        {
            int jitter = jitterMs > 0 ? random.Next(jitterMs) : 0;
            long releaseAtMs = checked(nowMs + delayMs + jitter);

            if (queue.Count >= _queueCapacity)
            {
                DropOldest(queue, isUplink);
            }

            ScheduledAction scheduled = new ScheduledAction(releaseAtMs, _nextSequence++, action);
            int insertIndex = FindInsertIndex(queue, scheduled);
            queue.Insert(insertIndex, scheduled);
            MaxQueueDepth = Math.Max(MaxQueueDepth, _uplinkQueue.Count + _downlinkQueue.Count);
        }

        private void DropOldest(List<ScheduledAction> queue, bool isUplink)
        {
            int oldestIndex = 0;
            long oldestSequence = queue[0].Sequence;
            for (int i = 1; i < queue.Count; i++)
            {
                if (queue[i].Sequence < oldestSequence)
                {
                    oldestIndex = i;
                    oldestSequence = queue[i].Sequence;
                }
            }

            queue.RemoveAt(oldestIndex);
            OverflowDropped++;
            if (isUplink)
            {
                UplinkDropped++;
            }
            else
            {
                DownlinkDropped++;
            }
        }

        private void Pump(List<ScheduledAction> queue, long nowMs, bool isUplink)
        {
            while (queue.Count > 0 && queue[0].ReleaseAtMs <= nowMs)
            {
                ScheduledAction scheduled = queue[0];
                queue.RemoveAt(0);
                if (isUplink)
                {
                    UplinkSent++;
                }
                else
                {
                    DownlinkDelivered++;
                }

                scheduled.Action();
            }
        }

        private static bool ShouldDrop(ref DeterministicRandom random, int lossPercent)
        {
            if (lossPercent <= 0)
            {
                return false;
            }

            return lossPercent >= 100 || random.Next(100) < lossPercent;
        }

        private static int FindInsertIndex(List<ScheduledAction> queue, ScheduledAction scheduled)
        {
            int low = 0;
            int high = queue.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                ScheduledAction current = queue[middle];
                if (current.ReleaseAtMs < scheduled.ReleaseAtMs ||
                    (current.ReleaseAtMs == scheduled.ReleaseAtMs && current.Sequence < scheduled.Sequence))
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private readonly struct ScheduledAction
        {
            public ScheduledAction(long releaseAtMs, long sequence, Action action)
            {
                ReleaseAtMs = releaseAtMs;
                Sequence = sequence;
                Action = action;
            }

            public long ReleaseAtMs { get; }
            public long Sequence { get; }
            public Action Action { get; }
        }
    }
}
