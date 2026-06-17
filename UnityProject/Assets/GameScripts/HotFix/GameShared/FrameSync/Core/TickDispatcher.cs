using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Core
{
    public sealed class TickDispatcher
    {
        private readonly TickAccumulator _tickAccumulator;
        private readonly List<TickableEntry> _entries = new List<TickableEntry>();
        private readonly List<PendingOperation> _pendingOperations = new List<PendingOperation>();
        private readonly IFrameSyncLogger? _logger;

        private bool _isTicking;
        private long _nextRegistrationOrder;
        private uint _currentFrame;
        private readonly Fixed64 _fixedDt;

        public TickDispatcher(
            float fixedDeltaTime = TickAccumulator.DefaultFixedDeltaTime,
            float maxDeltaTime = TickAccumulator.DefaultMaxDeltaTime,
            IFrameSyncLogger? logger = null)
        {
            _tickAccumulator = new TickAccumulator(fixedDeltaTime, maxDeltaTime);
            _fixedDt = (Fixed64)_tickAccumulator.FixedDeltaTime;
            _logger = logger;
        }

        public float FixedDeltaTime => _tickAccumulator.FixedDeltaTime;
        public uint CurrentFrame => _currentFrame;
        public int RegisteredCount => _entries.Count;

        public bool Register(ITickable tickable)
        {
            if (tickable == null)
            {
                throw new ArgumentNullException(nameof(tickable));
            }

            long order = _nextRegistrationOrder++;
            if (_isTicking)
            {
                _pendingOperations.Add(PendingOperation.CreateRegister(tickable, order));
                return true;
            }

            return RegisterImmediate(tickable, order);
        }

        public bool Unregister(ITickable tickable)
        {
            if (tickable == null)
            {
                throw new ArgumentNullException(nameof(tickable));
            }

            if (_isTicking)
            {
                _pendingOperations.Add(PendingOperation.CreateUnregister(tickable));
                return true;
            }

            return UnregisterImmediate(tickable);
        }

        public void Update(float deltaTime)
        {
            int tickCount = _tickAccumulator.Accumulate(deltaTime);
            for (int tickIndex = 0; tickIndex < tickCount; tickIndex++)
            {
                ExecuteTick(_fixedDt);
            }

            if (!_isTicking && _pendingOperations.Count > 0)
            {
                ApplyPendingOperations();
            }
        }

        public void TickOnce()
        {
            ExecuteTick(_fixedDt);

            if (!_isTicking && _pendingOperations.Count > 0)
            {
                ApplyPendingOperations();
            }
        }

        public void SetCurrentFrame(uint frameIndex, bool resetAccumulator = false)
        {
            if (_isTicking)
            {
                throw new InvalidOperationException("Cannot change current frame while ticking.");
            }

            _currentFrame = frameIndex;
            if (resetAccumulator)
            {
                _tickAccumulator.Reset();
            }
        }

        private void ExecuteTick(Fixed64 fixedDeltaTime)
        {
            DeterminismRules.AssertFixedDt(fixedDeltaTime);

            _isTicking = true;
            try
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    TickableEntry entry = _entries[i];
                    try
                    {
                        entry.Tickable.Tick(_currentFrame, fixedDeltaTime);
                    }
                    catch (Exception exception)
                    {
                        _logger?.LogError(
                            exception,
                            $"Tick failed. Tickable={entry.Tickable.GetType().FullName}, Frame={_currentFrame}.");
                    }
                }
            }
            finally
            {
                _isTicking = false;
            }

            ApplyPendingOperations();
            _currentFrame++;
        }

        private void ApplyPendingOperations()
        {
            if (_pendingOperations.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                PendingOperation operation = _pendingOperations[i];
                if (operation.IsRegister)
                {
                    RegisterImmediate(operation.Tickable, operation.RegistrationOrder);
                }
                else
                {
                    UnregisterImmediate(operation.Tickable);
                }
            }

            _pendingOperations.Clear();
        }

        private bool RegisterImmediate(ITickable tickable, long registrationOrder)
        {
            if (FindEntryIndex(tickable) >= 0)
            {
                return false;
            }

            _entries.Add(new TickableEntry(tickable, registrationOrder));
            _entries.Sort(TickableEntryComparer.Instance);
            return true;
        }

        private bool UnregisterImmediate(ITickable tickable)
        {
            int index = FindEntryIndex(tickable);
            if (index < 0)
            {
                return false;
            }

            _entries.RemoveAt(index);
            return true;
        }

        private int FindEntryIndex(ITickable tickable)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ReferenceEquals(_entries[i].Tickable, tickable))
                {
                    return i;
                }
            }

            return -1;
        }

        private readonly struct PendingOperation
        {
            private PendingOperation(bool isRegister, ITickable tickable, long registrationOrder)
            {
                IsRegister = isRegister;
                Tickable = tickable;
                RegistrationOrder = registrationOrder;
            }

            public bool IsRegister { get; }
            public ITickable Tickable { get; }
            public long RegistrationOrder { get; }

            public static PendingOperation CreateRegister(ITickable tickable, long registrationOrder)
            {
                return new PendingOperation(true, tickable, registrationOrder);
            }

            public static PendingOperation CreateUnregister(ITickable tickable)
            {
                return new PendingOperation(false, tickable, 0);
            }
        }

        private sealed class TickableEntry
        {
            public TickableEntry(ITickable tickable, long registrationOrder)
            {
                Tickable = tickable;
                RegistrationOrder = registrationOrder;
            }

            public ITickable Tickable { get; }
            public long RegistrationOrder { get; }
        }

        private sealed class TickableEntryComparer : IComparer<TickableEntry>
        {
            public static readonly TickableEntryComparer Instance = new TickableEntryComparer();

            public int Compare(TickableEntry? x, TickableEntry? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                int priorityCompare = x.Tickable.Priority.CompareTo(y.Tickable.Priority);
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                return x.RegistrationOrder.CompareTo(y.RegistrationOrder);
            }
        }
    }
}
