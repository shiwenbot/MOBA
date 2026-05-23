using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public readonly struct NumericModifierSnapshot
    {
        public static readonly NumericModifierSnapshot Empty =
            new NumericModifierSnapshot(PlayerAttributeSnapshot.Default, Array.Empty<NumericModifier>());

        public NumericModifierSnapshot(PlayerAttributeSnapshot baseAttributes, IReadOnlyList<NumericModifier> modifiers)
        {
            BaseAttributes = baseAttributes;
            Modifiers = CopyModifiers(modifiers);
        }

        public PlayerAttributeSnapshot BaseAttributes { get; }
        public IReadOnlyList<NumericModifier> Modifiers { get; }
        public int Count => Modifiers.Count;

        public static NumericModifierSnapshot FromAttributes(PlayerAttributeSnapshot attributes)
        {
            return new NumericModifierSnapshot(attributes, Array.Empty<NumericModifier>());
        }

        private static NumericModifier[] CopyModifiers(IReadOnlyList<NumericModifier> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0)
            {
                return Array.Empty<NumericModifier>();
            }

            NumericModifier[] copy = new NumericModifier[modifiers.Count];
            for (int i = 0; i < modifiers.Count; i++)
            {
                copy[i] = modifiers[i];
            }

            return copy;
        }
    }
}
