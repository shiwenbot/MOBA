using GameShared.Badminton.Config;
using UnityEngine;

namespace GameShared.Badminton
{
    public sealed class ShuttlecockState
    {
        public Vector2 XZ { get; set; }
        public float Y { get; set; }
        public Vector2 Vxz { get; set; }
        public float Vy { get; set; }
        public ShuttlecockFlightPhase Phase { get; set; }
        public int LastValidFlyingFrame { get; set; }
        public Vector2 LandingXZ { get; set; }
        public bool IsInBounds { get; set; }
        public ShuttlecockShotType ActiveShotType { get; set; }
        public float HorizontalDrag { get; set; }

        public bool IsTerminal => Phase == ShuttlecockFlightPhase.Landed || Phase == ShuttlecockFlightPhase.OutOfBounds;

        public void Reset()
        {
            XZ = Vector2.zero;
            Y = 0.0f;
            Vxz = Vector2.zero;
            Vy = 0.0f;
            Phase = ShuttlecockFlightPhase.Idle;
            LastValidFlyingFrame = -1;
            LandingXZ = Vector2.zero;
            IsInBounds = false;
            ActiveShotType = ShuttlecockShotType.Clear;
            HorizontalDrag = 0.0f;
        }
    }
}
