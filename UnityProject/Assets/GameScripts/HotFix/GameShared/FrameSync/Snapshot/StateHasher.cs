using System;
using GameShared.FrameSync.Battle;

namespace GameShared.FrameSync.Snapshot
{
    public static class StateHasher
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;
        private const int NegativeZeroBits = unchecked((int)0x80000000);

        public static ulong Hash(BattleWorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerStateSnapshot player = snapshot.Players[i];
                MixInt(ref hash, (int)(player.PlayerId >> 32));
                MixInt(ref hash, (int)player.PlayerId);
                MixInt(ref hash, NormalizeFloatBits(player.X));
                MixInt(ref hash, NormalizeFloatBits(player.Y));
            }

            return hash;
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
    }
}
