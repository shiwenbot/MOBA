using System;
using FixedMathSharp;

namespace GameShared.Badminton.Config
{
    public readonly struct ShuttlecockShotDefinition
    {
        public ShuttlecockShotDefinition(
            ShuttlecockShotType shotType,
            Fixed64 horizontalSpeed,
            Fixed64 launchAngleDegrees,
            Fixed64 horizontalDrag)
        {
            if (horizontalSpeed < Fixed64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontalSpeed), horizontalSpeed, "Horizontal speed must be non-negative.");
            }

            if (horizontalDrag < Fixed64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontalDrag), horizontalDrag, "Horizontal drag must be non-negative.");
            }

            ShotType = shotType;
            HorizontalSpeed = horizontalSpeed;
            LaunchAngleDegrees = launchAngleDegrees;
            HorizontalDrag = horizontalDrag;
        }

        public ShuttlecockShotType ShotType { get; }
        public Fixed64 HorizontalSpeed { get; }
        public Fixed64 LaunchAngleDegrees { get; }
        public Fixed64 HorizontalDrag { get; }
    }
}
