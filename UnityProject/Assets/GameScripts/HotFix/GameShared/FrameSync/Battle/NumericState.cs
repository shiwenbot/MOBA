using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public sealed class NumericState
    {
        private readonly List<NumericModifier> _modifiers = new List<NumericModifier>();
        private PlayerAttributeSnapshot _baseAttributes = PlayerAttributeSnapshot.Default;

        public IReadOnlyList<NumericModifier> Modifiers => _modifiers;
        public PlayerAttributeSnapshot BaseAttributes => _baseAttributes;
        public PlayerAttributeDirtyFlags DirtyFlags { get; private set; }
        public int Count => _modifiers.Count;
        public bool HasModifiers => _modifiers.Count > 0;

        public void AddModifier(NumericModifier modifier)
        {
            int insertIndex = _modifiers.Count;
            for (int i = 0; i < _modifiers.Count; i++)
            {
                if (CompareModifier(modifier, _modifiers[i]) < 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            _modifiers.Insert(insertIndex, modifier);
        }

        public int RemoveBySource(long sourceBuffId)
        {
            int removed = 0;
            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                if (_modifiers[i].SourceBuffId != sourceBuffId)
                {
                    continue;
                }

                _modifiers.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        public void SetBaseAttributes(PlayerAttributeSnapshot attributes)
        {
            _baseAttributes = attributes;
        }

        public int GetBaseValue(AttributeKind attributeKind)
        {
            return attributeKind switch
            {
                AttributeKind.Health => _baseAttributes.Health,
                AttributeKind.MaxHealth => _baseAttributes.MaxHealth,
                AttributeKind.Mana => _baseAttributes.Mana,
                AttributeKind.MaxMana => _baseAttributes.MaxMana,
                AttributeKind.Attack => _baseAttributes.Attack,
                AttributeKind.Stamina => _baseAttributes.Stamina,
                AttributeKind.MaxStamina => _baseAttributes.MaxStamina,
                _ => 0
            };
        }

        public void SetBaseValue(AttributeKind attributeKind, int value)
        {
            _baseAttributes = attributeKind switch
            {
                AttributeKind.Health => new PlayerAttributeSnapshot(
                    value,
                    _baseAttributes.MaxHealth,
                    _baseAttributes.Mana,
                    _baseAttributes.MaxMana,
                    _baseAttributes.Attack,
                    _baseAttributes.Stamina,
                    _baseAttributes.MaxStamina),
                AttributeKind.MaxHealth => new PlayerAttributeSnapshot(
                    _baseAttributes.Health,
                    value,
                    _baseAttributes.Mana,
                    _baseAttributes.MaxMana,
                    _baseAttributes.Attack,
                    _baseAttributes.Stamina,
                    _baseAttributes.MaxStamina),
                AttributeKind.Mana => new PlayerAttributeSnapshot(
                    _baseAttributes.Health,
                    _baseAttributes.MaxHealth,
                    value,
                    _baseAttributes.MaxMana,
                    _baseAttributes.Attack,
                    _baseAttributes.Stamina,
                    _baseAttributes.MaxStamina),
                AttributeKind.MaxMana => new PlayerAttributeSnapshot(
                    _baseAttributes.Health,
                    _baseAttributes.MaxHealth,
                    _baseAttributes.Mana,
                    value,
                    _baseAttributes.Attack,
                    _baseAttributes.Stamina,
                    _baseAttributes.MaxStamina),
                AttributeKind.Attack => new PlayerAttributeSnapshot(
                    _baseAttributes.Health,
                    _baseAttributes.MaxHealth,
                    _baseAttributes.Mana,
                    _baseAttributes.MaxMana,
                    value,
                    _baseAttributes.Stamina,
                    _baseAttributes.MaxStamina),
                AttributeKind.Stamina => new PlayerAttributeSnapshot(
                    _baseAttributes.Health,
                    _baseAttributes.MaxHealth,
                    _baseAttributes.Mana,
                    _baseAttributes.MaxMana,
                    _baseAttributes.Attack,
                    value,
                    _baseAttributes.MaxStamina),
                AttributeKind.MaxStamina => new PlayerAttributeSnapshot(
                    _baseAttributes.Health,
                    _baseAttributes.MaxHealth,
                    _baseAttributes.Mana,
                    _baseAttributes.MaxMana,
                    _baseAttributes.Attack,
                    _baseAttributes.Stamina,
                    value),
                _ => _baseAttributes
            };
        }

        public void ApplyBaseDelta(AttributeKind attributeKind, int delta)
        {
            long updated = (long)GetBaseValue(attributeKind) + delta;
            SetBaseValue(attributeKind, ClampToInt(updated));
        }

        public NumericModifierSnapshot CaptureSnapshot()
        {
            return new NumericModifierSnapshot(_baseAttributes, _modifiers.ToArray());
        }

        public void RestoreSnapshot(NumericModifierSnapshot snapshot)
        {
            _baseAttributes = snapshot.BaseAttributes;
            _modifiers.Clear();

            IReadOnlyList<NumericModifier> modifiers = snapshot.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                _modifiers.Add(modifiers[i]);
            }

            _modifiers.Sort(CompareModifier);
            DirtyFlags = PlayerAttributeDirtyFlags.None;
        }

        public PlayerAttributeDirtyFlags Recalculate(PlayerState target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            PlayerAttributeSnapshot before = target.CaptureAttributeSnapshot();
            int maxHealth = Math.Max(0, CalculateFinalValue(AttributeKind.MaxHealth));
            int maxMana = Math.Max(0, CalculateFinalValue(AttributeKind.MaxMana));
            int maxStamina = Math.Max(0, CalculateFinalValue(AttributeKind.MaxStamina));
            int attack = CalculateFinalValue(AttributeKind.Attack);
            int health = Math.Clamp(CalculateFinalValue(AttributeKind.Health), 0, maxHealth);
            int mana = Math.Clamp(CalculateFinalValue(AttributeKind.Mana), 0, maxMana);
            int stamina = Math.Clamp(CalculateFinalValue(AttributeKind.Stamina), 0, maxStamina);

            target.SetComputedAttributes(new PlayerAttributeSnapshot(
                health,
                maxHealth,
                mana,
                maxMana,
                attack,
                stamina,
                maxStamina));
            DirtyFlags = PlayerAttributeSync.ComputeDirtyMask(true, before, target.CaptureAttributeSnapshot());
            return DirtyFlags;
        }

        private int CalculateFinalValue(AttributeKind attributeKind)
        {
            long baseValue = GetBaseValue(attributeKind);
            long flatModifier = 0;
            long percentModifier = 0;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                NumericModifier modifier = _modifiers[i];
                if (modifier.AttributeKind != attributeKind)
                {
                    continue;
                }

                if (modifier.ValueType == ModifierValueType.Flat)
                {
                    flatModifier += modifier.Value;
                    continue;
                }

                percentModifier += modifier.Value;
            }

            long normalizedPercent = Math.Clamp(percentModifier, -10000L, int.MaxValue);
            long multiplier = 10000L + normalizedPercent;
            long finalValue = ((baseValue + flatModifier) * multiplier) / 10000L;
            return ClampToInt(finalValue);
        }

        private static int CompareModifier(NumericModifier left, NumericModifier right)
        {
            int bySource = left.SourceBuffId.CompareTo(right.SourceBuffId);
            if (bySource != 0)
            {
                return bySource;
            }

            int byAttribute = left.AttributeKind.CompareTo(right.AttributeKind);
            if (byAttribute != 0)
            {
                return byAttribute;
            }

            int byType = left.ValueType.CompareTo(right.ValueType);
            if (byType != 0)
            {
                return byType;
            }

            return left.Value.CompareTo(right.Value);
        }

        private static int ClampToInt(long value)
        {
            return value > int.MaxValue
                ? int.MaxValue
                : value < int.MinValue
                    ? int.MinValue
                    : (int)value;
        }
    }
}
