namespace GameShared.FrameSync.Battle
{
    public enum ModifierValueType : byte
    {
        Flat = 0,
        Percent = 1
    }

    public enum AttributeKind : byte
    {
        Health = 1,
        MaxHealth = 2,
        Mana = 3,
        MaxMana = 4,
        Attack = 5
    }

    public readonly struct NumericModifier
    {
        public NumericModifier(long sourceBuffId, ModifierValueType valueType, AttributeKind attributeKind, int value)
        {
            SourceBuffId = sourceBuffId;
            ValueType = valueType;
            AttributeKind = attributeKind;
            Value = value;
        }

        public long SourceBuffId { get; }
        public ModifierValueType ValueType { get; }
        public AttributeKind AttributeKind { get; }
        public int Value { get; }
    }
}
