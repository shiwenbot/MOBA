using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SetVariableNode : SkillGraphNode
    {
        private const string KeyProperty = "key";
        private const string ValueTypeProperty = "valueType";
        private const string ValueProperty = "value";

        private readonly TextField _keyField;
        private readonly EnumField _valueTypeField;
        private readonly TextField _stringValueField;
        private readonly FloatField _floatValueField;
        private readonly IntegerField _intValueField;
        private readonly Toggle _boolValueField;

        private string _key = string.Empty;
        private SkillBlackboardValueType _valueType = SkillBlackboardValueType.Bool;
        private string _stringValue = string.Empty;
        private float _floatValue;
        private int _intValue;
        private bool _boolValue;

        public SetVariableNode()
            : base(SkillNodeType.SetVariable, "Set Variable")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _keyField = new TextField("Key") { value = _key };
            _keyField.RegisterValueChangedCallback(evt => _key = evt.newValue ?? string.Empty);
            AddPropertyField(_keyField);

            _valueTypeField = new EnumField("Value Type", _valueType);
            _valueTypeField.RegisterValueChangedCallback(OnValueTypeChanged);
            AddPropertyField(_valueTypeField);

            _stringValueField = new TextField("Value") { value = _stringValue };
            _stringValueField.RegisterValueChangedCallback(evt => _stringValue = evt.newValue ?? string.Empty);
            AddPropertyField(_stringValueField);

            _floatValueField = new FloatField("Value") { value = _floatValue };
            _floatValueField.RegisterValueChangedCallback(evt => _floatValue = evt.newValue);
            AddPropertyField(_floatValueField);

            _intValueField = new IntegerField("Value") { value = _intValue };
            _intValueField.RegisterValueChangedCallback(evt => _intValue = evt.newValue);
            AddPropertyField(_intValueField);

            _boolValueField = new Toggle("Value") { value = _boolValue };
            _boolValueField.RegisterValueChangedCallback(evt => _boolValue = evt.newValue);
            AddPropertyField(_boolValueField);

            UpdateUi();
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, KeyProperty, _key);
            AddProperty(properties, ValueTypeProperty, _valueType);
            AddProperty(properties, ValueProperty, GetSerializedValue());
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            _key = GetPropertyValue(properties, KeyProperty);

            if (Enum.TryParse(GetPropertyValue(properties, ValueTypeProperty, _valueType.ToString()), true, out SkillBlackboardValueType parsedValueType))
                _valueType = parsedValueType;

            string rawValue = GetPropertyValue(properties, ValueProperty);
            _stringValue = rawValue;

            if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat))
                _floatValue = parsedFloat;

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                _intValue = parsedInt;

            if (bool.TryParse(rawValue, out bool parsedBool))
                _boolValue = parsedBool;

            _keyField.SetValueWithoutNotify(_key);
            _valueTypeField.SetValueWithoutNotify(_valueType);
            _stringValueField.SetValueWithoutNotify(_stringValue);
            _floatValueField.SetValueWithoutNotify(_floatValue);
            _intValueField.SetValueWithoutNotify(_intValue);
            _boolValueField.SetValueWithoutNotify(_boolValue);

            UpdateUi();
        }

        private void OnValueTypeChanged(ChangeEvent<Enum> evt)
        {
            if (evt.newValue is SkillBlackboardValueType parsedValueType)
            {
                _valueType = parsedValueType;
                UpdateUi();
            }
        }

        private void UpdateUi()
        {
            _stringValueField.style.display = _valueType == SkillBlackboardValueType.String ? DisplayStyle.Flex : DisplayStyle.None;
            _floatValueField.style.display = _valueType == SkillBlackboardValueType.Float ? DisplayStyle.Flex : DisplayStyle.None;
            _intValueField.style.display = _valueType == SkillBlackboardValueType.Int ? DisplayStyle.Flex : DisplayStyle.None;
            _boolValueField.style.display = _valueType == SkillBlackboardValueType.Bool ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private string GetSerializedValue()
        {
            return _valueType switch
            {
                SkillBlackboardValueType.String => _stringValue ?? string.Empty,
                SkillBlackboardValueType.Float => _floatValue.ToString(CultureInfo.InvariantCulture),
                SkillBlackboardValueType.Int => _intValue.ToString(CultureInfo.InvariantCulture),
                SkillBlackboardValueType.Bool => _boolValue ? "true" : "false",
                _ => string.Empty
            };
        }
    }
}
