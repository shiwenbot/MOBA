using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Core;

namespace GameShared.FrameSync.Timer
{
    public sealed class FrameTimerService : ITickable
    {
        private readonly SortedDictionary<uint, FrameTimer> _timers = new SortedDictionary<uint, FrameTimer>();
        private readonly List<FrameTimer> _dueTimers = new List<FrameTimer>();
        private readonly List<uint> _cleanupIds = new List<uint>();
        private readonly IFrameSyncLogger? _logger;

        private bool _isTicking;
        private uint _nextTimerId;
        private uint _currentFrame;

        public FrameTimerService(int priority = 0, IFrameSyncLogger? logger = null)
        {
            Priority = priority;
            _logger = logger;
        }

        public int Priority { get; }
        public int ActiveTimerCount => _timers.Count;

        public uint AddTimer(uint delayFrames, Action<uint> callback, bool repeat = false)
        {
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            uint normalizedDelay = delayFrames == 0 ? 1u : delayFrames;
            uint interval = repeat ? normalizedDelay : 0u;

            uint timerId = ++_nextTimerId;
            FrameTimer timer = new FrameTimer(
                timerId,
                _currentFrame + normalizedDelay,
                interval,
                repeat,
                callback);

            _timers[timerId] = timer;
            return timerId;
        }

        public bool RemoveTimer(uint timerId)
        {
            if (!_timers.TryGetValue(timerId, out FrameTimer? timer) || timer == null)
            {
                return false;
            }

            timer.IsRemoved = true;
            if (!_isTicking)
            {
                _timers.Remove(timerId);
            }

            return true;
        }

        public void Tick(uint frameIndex, Fixed64 fixedDt)
        {
            _currentFrame = frameIndex;
            _dueTimers.Clear();
            _cleanupIds.Clear();

            foreach (KeyValuePair<uint, FrameTimer> pair in _timers)
            {
                FrameTimer timer = pair.Value;
                if (timer.IsRemoved)
                {
                    _cleanupIds.Add(pair.Key);
                    continue;
                }

                if (timer.TriggerFrame <= frameIndex)
                {
                    _dueTimers.Add(timer);
                }
            }

            _isTicking = true;
            for (int i = 0; i < _dueTimers.Count; i++)
            {
                FrameTimer timer = _dueTimers[i];
                if (timer.IsRemoved)
                {
                    continue;
                }

                try
                {
                    timer.Callback(frameIndex);
                }
                catch (Exception exception)
                {
                    _logger?.LogError(exception, $"FrameTimer callback failed. TimerId={timer.Id}, Frame={frameIndex}.");
                }

                if (timer.IsRemoved)
                {
                    continue;
                }

                if (timer.Repeat)
                {
                    timer.TriggerFrame = frameIndex + timer.IntervalFrames;
                }
                else
                {
                    timer.IsRemoved = true;
                    _cleanupIds.Add(timer.Id);
                }
            }

            _isTicking = false;
            CleanupRemovedTimers();
        }

        private void CleanupRemovedTimers()
        {
            if (_cleanupIds.Count == 0)
            {
                foreach (KeyValuePair<uint, FrameTimer> pair in _timers)
                {
                    if (pair.Value.IsRemoved)
                    {
                        _cleanupIds.Add(pair.Key);
                    }
                }
            }

            for (int i = 0; i < _cleanupIds.Count; i++)
            {
                _timers.Remove(_cleanupIds[i]);
            }

            _cleanupIds.Clear();
        }
    }
}
