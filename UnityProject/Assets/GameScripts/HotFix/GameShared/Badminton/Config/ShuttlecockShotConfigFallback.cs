using System.Collections.Generic;

namespace GameShared.Badminton.Config
{
    public sealed class ShuttlecockShotConfigFallback : IShuttlecockShotConfigProvider
    {
        private readonly Dictionary<ShuttlecockShotType, ShuttlecockShotDefinition> _definitions =
            new Dictionary<ShuttlecockShotType, ShuttlecockShotDefinition>
            {
                { ShuttlecockShotType.Clear, new ShuttlecockShotDefinition(ShuttlecockShotType.Clear, 18.0f, 55.0f, 0.92f) },
                { ShuttlecockShotType.Drop, new ShuttlecockShotDefinition(ShuttlecockShotType.Drop, 8.0f, 35.0f, 0.92f) },
                { ShuttlecockShotType.Smash, new ShuttlecockShotDefinition(ShuttlecockShotType.Smash, 22.0f, -15.0f, 0.92f) },
                { ShuttlecockShotType.Drive, new ShuttlecockShotDefinition(ShuttlecockShotType.Drive, 16.0f, 8.0f, 0.92f) },
                { ShuttlecockShotType.NetShot, new ShuttlecockShotDefinition(ShuttlecockShotType.NetShot, 5.0f, 20.0f, 0.92f) },
                { ShuttlecockShotType.Lift, new ShuttlecockShotDefinition(ShuttlecockShotType.Lift, 12.0f, 65.0f, 0.92f) },
            };

        public static ShuttlecockShotConfigFallback Instance { get; } = new ShuttlecockShotConfigFallback();

        private ShuttlecockShotConfigFallback()
        {
        }

        public bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShotDefinition definition)
        {
            return _definitions.TryGetValue(shotType, out definition);
        }
    }
}
