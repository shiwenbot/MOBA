using FixedMathSharp;

namespace GameShared.Badminton
{
    public static class ShuttlecockStateHasher
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Hash(ShuttlecockSnapshot snapshot)
        {
            ulong hash = FnvOffsetBasis;
            MixUInt(ref hash, snapshot.FrameIndex);
            MixFixed(ref hash, snapshot.XZ.x);
            MixFixed(ref hash, snapshot.XZ.y);
            MixFixed(ref hash, snapshot.Y);
            MixFixed(ref hash, snapshot.Vxz.x);
            MixFixed(ref hash, snapshot.Vxz.y);
            MixFixed(ref hash, snapshot.Vy);
            MixInt(ref hash, (int)snapshot.Phase);
            MixInt(ref hash, snapshot.LastValidFlyingFrame);
            MixFixed(ref hash, snapshot.LandingXZ.x);
            MixFixed(ref hash, snapshot.LandingXZ.y);
            MixBool(ref hash, snapshot.IsInBounds);
            MixInt(ref hash, (int)snapshot.ActiveShotType);
            MixFixed(ref hash, snapshot.HorizontalDrag);
            return hash;
        }

        private static void MixUInt(ref ulong hash, uint value)
        {
            MixInt(ref hash, unchecked((int)value));
        }

        private static void MixFixed(ref ulong hash, Fixed64 value)
        {
            MixLong(ref hash, value.m_rawValue);
        }

        private static void MixInt(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= FnvPrime;
            }
        }

        private static void MixLong(ref ulong hash, long value)
        {
            MixInt(ref hash, unchecked((int)value));
            MixInt(ref hash, unchecked((int)(value >> 32)));
        }

        private static void MixBool(ref ulong hash, bool value)
        {
            MixInt(ref hash, value ? 1 : 0);
        }
    }
}
