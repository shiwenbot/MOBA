using FixedMathSharp;
using GameShared.Badminton.Config;

namespace GameShared.Badminton
{
    public readonly struct ShuttlecockSnapshot
    {
        public ShuttlecockSnapshot(
            uint frameIndex,
            Vector2d xz,
            Fixed64 y,
            Vector2d vxz,
            Fixed64 vy,
            ShuttlecockFlightPhase phase,
            int lastValidFlyingFrame,
            Vector2d landingXz,
            bool isInBounds,
            ShuttlecockShotType activeShotType,
            Fixed64 horizontalDrag)
        {
            FrameIndex = frameIndex;
            XZ = xz;
            Y = y;
            Vxz = vxz;
            Vy = vy;
            Phase = phase;
            LastValidFlyingFrame = lastValidFlyingFrame;
            LandingXZ = landingXz;
            IsInBounds = isInBounds;
            ActiveShotType = activeShotType;
            HorizontalDrag = horizontalDrag;
        }

        public uint FrameIndex { get; }
        public Vector2d XZ { get; }
        public Fixed64 Y { get; }
        public Vector2d Vxz { get; }
        public Fixed64 Vy { get; }
        public ShuttlecockFlightPhase Phase { get; }
        public int LastValidFlyingFrame { get; }
        public Vector2d LandingXZ { get; }
        public bool IsInBounds { get; }
        public ShuttlecockShotType ActiveShotType { get; }
        public Fixed64 HorizontalDrag { get; }

        public ShuttlecockSnapshot WithFrameIndex(uint frameIndex)
        {
            return new ShuttlecockSnapshot(
                frameIndex,
                XZ,
                Y,
                Vxz,
                Vy,
                Phase,
                LastValidFlyingFrame,
                LandingXZ,
                IsInBounds,
                ActiveShotType,
                HorizontalDrag);
        }
    }
}
