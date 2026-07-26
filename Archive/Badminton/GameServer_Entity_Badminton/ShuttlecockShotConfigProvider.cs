using GameConfig.badminton;
using GameShared.Badminton.Config;

namespace GameShared.Badminton.Config
{
    public static class ShuttlecockShotConfigProvider
    {
        public static TbShuttlecockShot Table => ServerConfigSystem.Instance.Tables.TbShuttlecockShot;

        public static ShuttlecockShot Get(ShuttlecockShotType shotType)
        {
            return Table.Get((int)shotType);
        }

        public static bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShot config)
        {
            config = Table.GetOrDefault((int)shotType);
            return config != null;
        }
    }
}
