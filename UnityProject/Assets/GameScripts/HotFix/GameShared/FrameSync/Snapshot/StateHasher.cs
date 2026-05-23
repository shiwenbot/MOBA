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
                MixInt(ref hash, player.Health);
                MixInt(ref hash, player.MaxHealth);
                MixInt(ref hash, player.Mana);
                MixInt(ref hash, player.MaxMana);
                MixInt(ref hash, player.Attack);
                MixInt(ref hash, player.ActiveBuffs.Count);
                for (int buffIndex = 0; buffIndex < player.ActiveBuffs.Count; buffIndex++)
                {
                    BuffState buffState = player.ActiveBuffs[buffIndex];
                    MixInt(ref hash, (int)(buffState.RuntimeBuffId >> 32));
                    MixInt(ref hash, (int)buffState.RuntimeBuffId);
                    MixInt(ref hash, buffState.BuffId);
                    MixInt(ref hash, (int)(buffState.CasterId >> 32));
                    MixInt(ref hash, (int)buffState.CasterId);
                    MixInt(ref hash, (int)(buffState.TargetId >> 32));
                    MixInt(ref hash, (int)buffState.TargetId);
                    MixInt(ref hash, buffState.StackCount);
                    MixInt(ref hash, buffState.RemainingFrames);
                    MixInt(ref hash, unchecked((int)buffState.AppliedFrame));
                    MixInt(ref hash, unchecked((int)buffState.Flags));
                }

                MixInt(ref hash, (int)(player.NextRuntimeBuffId >> 32));
                MixInt(ref hash, (int)player.NextRuntimeBuffId);
                MixInt(ref hash, player.Numeric.BaseAttributes.Health);
                MixInt(ref hash, player.Numeric.BaseAttributes.MaxHealth);
                MixInt(ref hash, player.Numeric.BaseAttributes.Mana);
                MixInt(ref hash, player.Numeric.BaseAttributes.MaxMana);
                MixInt(ref hash, player.Numeric.BaseAttributes.Attack);
                MixInt(ref hash, player.Numeric.Count);
                for (int modifierIndex = 0; modifierIndex < player.Numeric.Modifiers.Count; modifierIndex++)
                {
                    NumericModifier modifier = player.Numeric.Modifiers[modifierIndex];
                    MixInt(ref hash, (int)(modifier.SourceBuffId >> 32));
                    MixInt(ref hash, (int)modifier.SourceBuffId);
                    MixInt(ref hash, (int)modifier.ValueType);
                    MixInt(ref hash, (int)modifier.AttributeKind);
                    MixInt(ref hash, modifier.Value);
                }
            }

            if (snapshot.PhysicsSnapshot != null)
            {
                MixInt(ref hash, snapshot.PhysicsSnapshot.Bodies.Count);
                for (int i = 0; i < snapshot.PhysicsSnapshot.Bodies.Count; i++)
                {
                    PhysicsBodySnapshot body = snapshot.PhysicsSnapshot.Bodies[i];
                    MixInt(ref hash, body.BodyId);
                    MixInt(ref hash, NormalizeFloatBits(body.PositionX));
                    MixInt(ref hash, NormalizeFloatBits(body.PositionY));
                    MixInt(ref hash, NormalizeFloatBits(body.RotationRadians));
                    MixInt(ref hash, NormalizeFloatBits(body.LinearVelocityX));
                    MixInt(ref hash, NormalizeFloatBits(body.LinearVelocityY));
                    MixInt(ref hash, NormalizeFloatBits(body.AngularVelocity));
                    MixBool(ref hash, body.IsAwake);
                    MixBool(ref hash, body.IsEnabled);
                }

                MixInt(ref hash, snapshot.PhysicsSnapshot.Contacts.Count);
                for (int i = 0; i < snapshot.PhysicsSnapshot.Contacts.Count; i++)
                {
                    PhysicsContactSnapshot contact = snapshot.PhysicsSnapshot.Contacts[i];
                    MixInt(ref hash, contact.BodyAId);
                    MixInt(ref hash, contact.BodyBId);
                    MixBool(ref hash, contact.IsTouching);
                }
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

        private static void MixBool(ref ulong hash, bool value)
        {
            MixInt(ref hash, value ? 1 : 0);
        }
    }
}
