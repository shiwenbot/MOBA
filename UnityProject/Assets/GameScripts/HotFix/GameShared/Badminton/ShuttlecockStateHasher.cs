using System;

namespace GameShared.Badminton
{
    public static class ShuttlecockStateHasher
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        public static ulong Hash(ShuttlecockSnapshot snapshot)
        {
            ulong hash = FnvOffsetBasis;
            MixUInt(ref hash, snapshot.FrameIndex);
            MixFloat(ref hash, snapshot.XZ.x);
            MixFloat(ref hash, snapshot.XZ.y);
            MixFloat(ref hash, snapshot.Y);
            MixFloat(ref hash, snapshot.Vxz.x);
            MixFloat(ref hash, snapshot.Vxz.y);
            MixFloat(ref hash, snapshot.Vy);
            MixInt(ref hash, (int)snapshot.Phase);
            MixInt(ref hash, snapshot.LastValidFlyingFrame);
            MixFloat(ref hash, snapshot.LandingXZ.x);
            MixFloat(ref hash, snapshot.LandingXZ.y);
            MixBool(ref hash, snapshot.IsInBounds);
            MixInt(ref hash, (int)snapshot.ActiveShotType);
            MixFloat(ref hash, snapshot.HorizontalDrag);
            return hash;
        }

        private static void MixUInt(ref ulong hash, uint value)
        {
            MixInt(ref hash, unchecked((int)value));
        }

        private static void MixFloat(ref ulong hash, float value)
        {
            MixInt(ref hash, NormalizeFloatBits(value));
        }

        private static int NormalizeFloatBits(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }

            int bits = BitConverter.SingleToInt32Bits(value);
            return bits == NegativeZeroBits ? 0 : bits;
        }

        private static void MixInt(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= FnvPrime;
            }
        }

        private static void MixBool(ref ulong hash, bool value)
        {
            MixInt(ref hash, value ? 1 : 0);
        }
    }
}
