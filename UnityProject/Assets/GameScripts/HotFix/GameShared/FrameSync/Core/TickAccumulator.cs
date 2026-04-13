using System;

namespace GameShared.FrameSync.Core
{
    public sealed class TickAccumulator
    {
        public const float DefaultFixedDeltaTime = 1.0f / 30.0f;
        public const float DefaultMaxDeltaTime = 0.5f;

        private readonly float _fixedDeltaTime;
        private readonly float _maxDeltaTime;
        private readonly int _maxTicksPerUpdate;
        private float _accumulator;

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

            _fixedDeltaTime = fixedDeltaTime;
            _maxDeltaTime = maxDeltaTime;
            _maxTicksPerUpdate = Math.Max(1, (int)MathF.Floor(_maxDeltaTime / _fixedDeltaTime));
        }

        public float FixedDeltaTime => _fixedDeltaTime;
        public float MaxDeltaTime => _maxDeltaTime;
        public int MaxTicksPerUpdate => _maxTicksPerUpdate;
        public float Remainder => _accumulator;

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
            else if (deltaTime > _maxDeltaTime)
            {
                deltaTime = _maxDeltaTime;
            }

            _accumulator += deltaTime;
            int tickCount = (int)(_accumulator / _fixedDeltaTime);
            if (tickCount <= 0)
            {
                return 0;
            }

            if (tickCount > _maxTicksPerUpdate)
            {
                tickCount = _maxTicksPerUpdate;
            }

            _accumulator -= tickCount * _fixedDeltaTime;
            if (_accumulator < 0.0f)
            {
                _accumulator = 0.0f;
            }

            return tickCount;
        }

        public void Reset()
        {
            _accumulator = 0.0f;
        }
    }
}
