using System;
using FixedMathSharp;

namespace GameShared.FrameSync.Core
{
    public sealed class TickAccumulator
    {
        public const float DefaultFixedDeltaTime = 1.0f / 30.0f;
        public const float DefaultMaxDeltaTime = 0.5f;
        // Keep the fixed-point tick delta aligned with the float API boundary.
        public static readonly Fixed64 DefaultFixedDeltaTimeFixed64 = (Fixed64)DefaultFixedDeltaTime;
        public static readonly Fixed64 DefaultMaxDeltaTimeFixed64 = Fixed64.FromRaw(FixedMath.ONE_L / 2L);

        private readonly Fixed64 _fixedDeltaTime;
        private readonly Fixed64 _maxDeltaTime;
        private readonly Fixed64 _tickBoundaryTolerance;
        private readonly int _maxTicksPerUpdate;
        private Fixed64 _accumulator;

        public TickAccumulator(float fixedDeltaTime = DefaultFixedDeltaTime, float maxDeltaTime = DefaultMaxDeltaTime)
        {
            if (fixedDeltaTime <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime), fixedDeltaTime, "fixedDeltaTime must be greater than zero.");
            }

            if (maxDeltaTime <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDeltaTime), maxDeltaTime, "maxDeltaTime must be greater than zero.");
            }

            _fixedDeltaTime = fixedDeltaTime == DefaultFixedDeltaTime
                ? DefaultFixedDeltaTimeFixed64
                : (Fixed64)fixedDeltaTime;
            _maxDeltaTime = maxDeltaTime == DefaultMaxDeltaTime
                ? DefaultMaxDeltaTimeFixed64
                : (Fixed64)maxDeltaTime;

            // Float inputs can land a few raw units below an exact tick boundary after conversion.
            _tickBoundaryTolerance = Fixed64.FromRaw(Math.Max(1L, _fixedDeltaTime.m_rawValue >> 17));
            _maxTicksPerUpdate = Math.Max(1, CountWholeTicks(_maxDeltaTime));
        }

        public float FixedDeltaTime => (float)_fixedDeltaTime;
        public float MaxDeltaTime => (float)_maxDeltaTime;
        public int MaxTicksPerUpdate => _maxTicksPerUpdate;
        public float Remainder => (float)_accumulator;

        public int Accumulate(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                return 0;
            }

            if (deltaTime < 0.0f)
            {
                deltaTime = 0.0f;
            }
            Fixed64 fixedDeltaTime = (Fixed64)deltaTime;
            if (fixedDeltaTime > _maxDeltaTime)
            {
                fixedDeltaTime = _maxDeltaTime;
            }

            _accumulator += fixedDeltaTime;
            int tickCount = CountWholeTicks(_accumulator);
            if (tickCount <= 0)
            {
                return 0;
            }

            if (tickCount > _maxTicksPerUpdate)
            {
                tickCount = _maxTicksPerUpdate;
            }

            _accumulator -= tickCount * _fixedDeltaTime;
            if (_accumulator < Fixed64.Zero)
            {
                _accumulator = Fixed64.Zero;
            }

            return tickCount;
        }

        public void Reset()
        {
            _accumulator = Fixed64.Zero;
        }

        private int CountWholeTicks(Fixed64 duration)
        {
            return (int)((duration + _tickBoundaryTolerance) / _fixedDeltaTime);
        }
    }
}
