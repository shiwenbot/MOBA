using System.Collections.Generic;
using FixedMathSharp;

namespace GameShared.Badminton.Config
{
    public sealed class ShuttlecockShotConfigFallback : IShuttlecockShotConfigProvider
    {
        private readonly Dictionary<ShuttlecockShotType, ShuttlecockShotDefinition> _definitions =
            new Dictionary<ShuttlecockShotType, ShuttlecockShotDefinition>
            {
                { ShuttlecockShotType.Clear, new ShuttlecockShotDefinition(ShuttlecockShotType.Clear, new Fixed64(18.0), new Fixed64(55.0), new Fixed64(0.92)) },
                { ShuttlecockShotType.Drop, new ShuttlecockShotDefinition(ShuttlecockShotType.Drop, new Fixed64(8.0), new Fixed64(35.0), new Fixed64(0.92)) },
                { ShuttlecockShotType.Smash, new ShuttlecockShotDefinition(ShuttlecockShotType.Smash, new Fixed64(22.0), new Fixed64(-15.0), new Fixed64(0.92)) },
                { ShuttlecockShotType.Drive, new ShuttlecockShotDefinition(ShuttlecockShotType.Drive, new Fixed64(16.0), new Fixed64(8.0), new Fixed64(0.92)) },
                { ShuttlecockShotType.NetShot, new ShuttlecockShotDefinition(ShuttlecockShotType.NetShot, new Fixed64(5.0), new Fixed64(20.0), new Fixed64(0.92)) },
                { ShuttlecockShotType.Lift, new ShuttlecockShotDefinition(ShuttlecockShotType.Lift, new Fixed64(12.0), new Fixed64(65.0), new Fixed64(0.92)) },
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
