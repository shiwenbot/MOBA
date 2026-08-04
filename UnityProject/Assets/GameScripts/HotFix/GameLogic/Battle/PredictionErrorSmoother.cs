using System;

namespace GameLogic
{
    /// <summary>
    /// Pure rendering-side prediction error decay. This state must never enter snapshots or hashes.
    /// </summary>
    public sealed class PredictionErrorSmoother
    {
        public const float SmoothingDurationSeconds = 0.12f;

        // Revisit after S6 knockback distance is finalized. This must exceed a single knockback distance.
        public const float MaxSmoothingDistance = 6.0f;

        private float _offsetX;
        private float _offsetY;
        private float _remainingSeconds;

        public float OffsetX => _offsetX;
        public float OffsetY => _offsetY;
        public float RemainingSeconds => _remainingSeconds;
        public float LastCorrectionMagnitude { get; private set; }

        public void Reset()
        {
            _offsetX = 0.0f;
            _offsetY = 0.0f;
            _remainingSeconds = 0.0f;
            LastCorrectionMagnitude = 0.0f;
        }

        public void SetOffset(float offsetX, float offsetY)
        {
            float magnitude = MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
            LastCorrectionMagnitude = magnitude;
            if (magnitude <= 0.0f || magnitude > MaxSmoothingDistance)
            {
                _offsetX = 0.0f;
                _offsetY = 0.0f;
                _remainingSeconds = 0.0f;
                return;
            }

            _offsetX = offsetX;
            _offsetY = offsetY;
            _remainingSeconds = SmoothingDurationSeconds;
        }

        public void Advance(float deltaTime)
        {
            if (deltaTime <= 0.0f || _remainingSeconds <= 0.0f)
            {
                return;
            }

            if (deltaTime >= _remainingSeconds)
            {
                _offsetX = 0.0f;
                _offsetY = 0.0f;
                _remainingSeconds = 0.0f;
                return;
            }

            float remainingRatio = (_remainingSeconds - deltaTime) / _remainingSeconds;
            _offsetX *= remainingRatio;
            _offsetY *= remainingRatio;
            _remainingSeconds -= deltaTime;
        }
    }
}
