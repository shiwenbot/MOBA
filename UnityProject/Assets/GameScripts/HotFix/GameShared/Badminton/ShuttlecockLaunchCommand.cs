using GameShared.Badminton.Config;
using GameShared.FrameSync.Command;
using UnityEngine;

namespace GameShared.Badminton
{
    public sealed class ShuttlecockLaunchCommand : IResettable
    {
        public uint TargetFrame { get; set; }
        public ShuttlecockShotType ShotType { get; set; }
        public Vector2 OriginXZ { get; set; }
        public float OriginY { get; set; }
        public Vector2 DirectionXZ { get; set; }

        public void Reset()
        {
            TargetFrame = 0;
            ShotType = ShuttlecockShotType.Clear;
            OriginXZ = Vector2.zero;
            OriginY = 0.0f;
            DirectionXZ = Vector2.zero;
        }
    }
}
