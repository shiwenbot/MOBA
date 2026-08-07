using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    public static class SeparationNormalResolver
    {
        private static readonly Fixed64 Epsilon = Fixed64.One / (Fixed64)10000;

        public static void Resolve(
            int bodyAId,
            int bodyBId,
            Fixed64 deltaX,
            Fixed64 deltaY,
            Fixed64 distanceSquared,
            Fixed64 velocityAX,
            Fixed64 velocityAY,
            Fixed64 velocityBX,
            Fixed64 velocityBY,
            bool hasVelocityInformation,
            out Fixed64 normalX,
            out Fixed64 normalY)
        {
            if (distanceSquared > Epsilon)
            {
                Fixed64 inverseDistance = Fixed64.One / FixedMath.Sqrt(distanceSquared);
                normalX = deltaX * inverseDistance;
                normalY = deltaY * inverseDistance;
                return;
            }

            if (hasVelocityInformation)
            {
                Fixed64 relativeVelocityX = velocityAX - velocityBX;
                Fixed64 relativeVelocityY = velocityAY - velocityBY;
                Fixed64 relativeVelocitySquared =
                    (relativeVelocityX * relativeVelocityX) + (relativeVelocityY * relativeVelocityY);
                if (relativeVelocitySquared > Epsilon)
                {
                    Fixed64 inverseLength = Fixed64.One / FixedMath.Sqrt(relativeVelocitySquared);
                    normalX = relativeVelocityX * inverseLength;
                    normalY = relativeVelocityY * inverseLength;
                    return;
                }
            }

            normalX = bodyAId <= bodyBId ? Fixed64.One : -Fixed64.One;
            normalY = Fixed64.Zero;
        }

        public static void ResolveWithoutVelocity(
            int bodyAId,
            int bodyBId,
            Fixed64 deltaX,
            Fixed64 deltaY,
            out Fixed64 normalX,
            out Fixed64 normalY)
        {
            Fixed64 distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            Resolve(
                bodyAId,
                bodyBId,
                deltaX,
                deltaY,
                distanceSquared,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.Zero,
                false,
                out normalX,
                out normalY);
        }
    }
}
