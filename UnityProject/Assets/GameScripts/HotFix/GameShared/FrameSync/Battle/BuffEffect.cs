namespace GameShared.FrameSync.Battle
{
    public readonly struct BuffEffect
    {
        public BuffEffect(AttributeKind attributeKind, ModifierValueType valueType, int value)
        {
            AttributeKind = attributeKind;
            ValueType = valueType;
            Value = value;
        }

        public AttributeKind AttributeKind { get; }
        public ModifierValueType ValueType { get; }
        public int Value { get; }
    }
}
