using GameShared.Badminton.Config;
using UnityEngine;

namespace GameShared.Badminton
{
    public readonly struct ShuttlecockSnapshot
    {
        public ShuttlecockSnapshot(
            uint frameIndex,
            Vector2 xz,
            float y,
            Vector2 vxz,
            float vy,
            ShuttlecockFlightPhase phase,
            int lastValidFlyingFrame,
            Vector2 landingXz,
            bool isInBounds,
            ShuttlecockShotType activeShotType,
            float horizontalDrag)
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
        public Vector2 XZ { get; }
        public float Y { get; }
        public Vector2 Vxz { get; }
        public float Vy { get; }
        public ShuttlecockFlightPhase Phase { get; }
        public int LastValidFlyingFrame { get; }
        public Vector2 LandingXZ { get; }
        public bool IsInBounds { get; }
        public ShuttlecockShotType ActiveShotType { get; }
        public float HorizontalDrag { get; }

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
