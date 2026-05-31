using System;
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
                config.HorizontalSpeed,
                GetSignedLaunchAngle(shotType, config.LaunchAngle),
                config.Drag);
            return true;
        }

        private static float GetSignedLaunchAngle(ShuttlecockShotType shotType, float angleDegrees)
        {
            float normalizedAngle = Math.Abs(angleDegrees);
            return shotType == ShuttlecockShotType.Smash ? -normalizedAngle : normalizedAngle;
        }
    }
}
