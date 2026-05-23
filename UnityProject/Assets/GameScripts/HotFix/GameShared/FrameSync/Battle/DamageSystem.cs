using System;

namespace GameShared.FrameSync.Battle
{
    public static class DamageSystem
    {
        public static void CastDamage(PlayerState target, int damage)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (damage <= 0)
            {
                return;
            }

            target.Numeric.ApplyBaseDelta(AttributeKind.Health, -damage);
            target.Numeric.Recalculate(target);
        }
    }
}
