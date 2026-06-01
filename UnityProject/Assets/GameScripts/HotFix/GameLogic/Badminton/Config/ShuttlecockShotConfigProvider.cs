using System;
using FixedMathSharp;
using GameConfig.badminton;

namespace GameShared.Badminton.Config
{
    public sealed class ShuttlecockShotConfigProvider : IShuttlecockShotConfigProvider
    {
        public static ShuttlecockShotConfigProvider Instance { get; } = new ShuttlecockShotConfigProvider();

        public static TbShuttlecockShot Table => ConfigSystem.Instance.Tables.TbShuttlecockShot;

        private ShuttlecockShotConfigProvider()
        {
        }

        public static ShuttlecockShot Get(ShuttlecockShotType shotType)
        {
            return Table.Get((int)shotType);
        }

        public static bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShot config)
        {
            config = Table.GetOrDefault((int)shotType);
            return config != null;
        }

        public bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShotDefinition definition)
        {
            if (!TryGet(shotType, out ShuttlecockShot config))
            {
                definition = default;
                return false;
            }

            definition = new ShuttlecockShotDefinition(
                shotType,
                new Fixed64(config.HorizontalSpeed),
                GetSignedLaunchAngle(shotType, config.LaunchAngle),
                new Fixed64(config.Drag));
            return true;
        }

        private static Fixed64 GetSignedLaunchAngle(ShuttlecockShotType shotType, float angleDegrees)
        {
            float normalizedAngle = Math.Abs(angleDegrees);
            return new Fixed64(shotType == ShuttlecockShotType.Smash ? -normalizedAngle : normalizedAngle);
        }
    }
}
