using FixedMathSharp;
using GameShared.Badminton.Config;
using GameShared.FrameSync.Command;

namespace GameShared.Badminton
{
    public sealed class ShuttlecockLaunchCommand : IResettable
    {
        public uint TargetFrame { get; set; }
        public ShuttlecockShotType ShotType { get; set; }
        public Vector2d OriginXZ { get; set; }
        public Fixed64 OriginY { get; set; }
        public Vector2d DirectionXZ { get; set; }

        public void Reset()
        {
            TargetFrame = 0;
            ShotType = ShuttlecockShotType.Clear;
            OriginXZ = Vector2d.Zero;
            OriginY = Fixed64.Zero;
            DirectionXZ = Vector2d.Zero;
        }
    }
}
