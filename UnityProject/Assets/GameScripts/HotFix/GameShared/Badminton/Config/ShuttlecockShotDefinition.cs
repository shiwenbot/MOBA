using System;
using GameShared.FrameSync.Determinism;

namespace GameShared.Badminton.Config
{
    public readonly struct ShuttlecockShotDefinition
    {
        public ShuttlecockShotDefinition(
            ShuttlecockShotType shotType,
            float horizontalSpeed,
            float launchAngleDegrees,
            float horizontalDrag)
        {
            DeterminismRules.AssertFinite(horizontalSpeed, nameof(horizontalSpeed));
            DeterminismRules.AssertFinite(launchAngleDegrees, nameof(launchAngleDegrees));
            DeterminismRules.AssertFinite(horizontalDrag, nameof(horizontalDrag));
            if (horizontalSpeed < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontalSpeed), horizontalSpeed, "Horizontal speed must be non-negative.");
            }

            if (horizontalDrag < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontalDrag), horizontalDrag, "Horizontal drag must be non-negative.");
            }

            ShotType = shotType;
            HorizontalSpeed = horizontalSpeed;
            LaunchAngleDegrees = launchAngleDegrees;
            HorizontalDrag = horizontalDrag;
        }

        public ShuttlecockShotType ShotType { get; }
        public float HorizontalSpeed { get; }
        public float LaunchAngleDegrees { get; }
        public float HorizontalDrag { get; }
    }
}
