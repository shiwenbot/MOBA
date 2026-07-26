using FixedMathSharp;
using GameShared.Badminton.Config;

namespace GameShared.Badminton
{
    public sealed class ShuttlecockState
    {
        public Vector2d XZ { get; set; }
        public Fixed64 Y { get; set; }
        public Vector2d Vxz { get; set; }
        public Fixed64 Vy { get; set; }
        public ShuttlecockFlightPhase Phase { get; set; }
        public int LastValidFlyingFrame { get; set; }
        public Vector2d LandingXZ { get; set; }
        public bool IsInBounds { get; set; }
        public ShuttlecockShotType ActiveShotType { get; set; }
        public Fixed64 HorizontalDrag { get; set; }

        public bool IsTerminal => Phase == ShuttlecockFlightPhase.Landed || Phase == ShuttlecockFlightPhase.OutOfBounds;

        public void Reset()
        {
            XZ = Vector2d.Zero;
            Y = Fixed64.Zero;
            Vxz = Vector2d.Zero;
            Vy = Fixed64.Zero;
            Phase = ShuttlecockFlightPhase.Idle;
            LastValidFlyingFrame = -1;
            LandingXZ = Vector2d.Zero;
            IsInBounds = false;
            ActiveShotType = ShuttlecockShotType.Clear;
            HorizontalDrag = Fixed64.Zero;
        }
    }
}
