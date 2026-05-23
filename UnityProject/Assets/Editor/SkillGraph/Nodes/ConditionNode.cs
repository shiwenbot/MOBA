using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameShared.SkillGraph;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class ConditionNode : SkillGraphNode, ISkillVariableBindableNode
    {
        private const string KeyProperty = "key";
        private const string OperatorProperty = "operator";
        private const string ValueTypeProperty = "valueType";
        private const string ValueProperty = "value";
        private const string MissingTypeText = "Missing";

        private readonly PopupField<string> _keyField;
        private readonly EnumField _operatorField;
        private readonly Label _valueTypeLabel;
        private readonly TextField _stringValueField;
        private readonly FloatField _floatValueField;
        private readonly IntegerField _intValueField;
        private readonly Toggle _boolValueField;
        private readonly List<SkillVariableDef> _availableVariables = new List<SkillVariableDef>();

        private string _key = string.Empty;
        private SkillConditionOperator _conditionOperator = SkillConditionOperator.Exists;
        private SkillBlackboardValueType _valueType = SkillBlackboardValueType.Bool;
        private string _stringValue = string.Empty;
        private float _floatValue;
        private int _intValue;
        private bool _boolValue;

        public ConditionNode()
            : base(SkillNodeType.Condition, "Condition")
        {
            AddFlowInput("In");
            AddFlowOutput("True");
            AddFlowOutput("False");

            _keyField = new PopupField<string>("Key", BuildKeyChoices(), 0, FormatVariableName, FormatVariableName);
            _keyField.RegisterValueChangedCallback(OnKeyChanged);
            AddPropertyField(_keyField);

            _operatorField = new EnumField("Operator", _conditionOperator);
            _operatorField.RegisterValueChangedCallback(OnOperatorChanged);
            AddPropertyField(_operatorField);

            _valueTypeLabel = new Label();
            _valueTypeLabel.style.marginTop = 2f;
            _valueTypeLabel.style.marginBottom = 2f;
            AddPropertyField(_valueTypeLabel);

            _stringValueField = new TextField("Value") { value = _stringValue };
            _stringValueField.RegisterValueChangedCallback(evt =>
            {
                _stringValue = evt.newValue ?? string.Empty;
                NotifyPropertiesChanged();
            });
            AddPropertyField(_stringValueField);

            _floatValueField = new FloatField("Value") { value = _floatValue };
            _floatValueField.RegisterValueChangedCallback(evt =>
            {
                _floatValue = evt.newValue;
                NotifyPropertiesChanged();
            });
            AddPropertyField(_floatValueField);

            _intValueField = new IntegerField("Value") { value = _intValue };
            _intValueField.RegisterValueChangedCallback(evt =>
            {
                _intValue = evt.newValue;
                NotifyPropertiesChanged();
            });
            AddPropertyField(_intValueField);

            _boolValueField = new Toggle("Value") { value = _boolValue };
            _boolValueField.RegisterValueChangedCallback(evt =>
            {
                _boolValue = evt.newValue;
                NotifyPropertiesChanged();
            });
            AddPropertyField(_boolValueField);

            UpdateUi();
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, KeyProperty, _key);
            AddProperty(properties, OperatorProperty, _conditionOperator);
            AddProperty(properties, ValueTypeProperty, GetEffectiveValueType());

            if (RequiresCompareValue())
                AddProperty(properties, ValueProperty, GetSerializedValue());
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            _key = GetPropertyValue(properties, KeyProperty);

            if (Enum.TryParse(
                    GetPropertyValue(properties, OperatorProperty, _conditionOperator.ToString()),
                    true,
                    out SkillConditionOperator parsedOperator))
            {
                _conditionOperator = parsedOperator;
            }

            if (Enum.TryParse(
                    GetPropertyValue(properties, ValueTypeProperty, _valueType.ToString()),
                    true,
                    out SkillBlackboardValueType parsedValueType))
            {
                _valueType = parsedValueType;
            }

            string rawValue = GetPropertyValue(properties, ValueProperty);
            _stringValue = rawValue;

            if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat))
                _floatValue = parsedFloat;

            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                _intValue = parsedInt;

            if (bool.TryParse(rawValue, out bool parsedBool))
                _boolValue = parsedBool;

            SyncValueTypeFromKey();
            RefreshKeyField();
            _operatorField.SetValueWithoutNotify(_conditionOperator);
            _stringValueField.SetValueWithoutNotify(_stringValue);
            _floatValueField.SetValueWithoutNotify(_floatValue);
            _intValueField.SetValueWithoutNotify(_intValue);
            _boolValueField.SetValueWithoutNotify(_boolValue);

            UpdateUi();
        }

        private void OnOperatorChanged(ChangeEvent<Enum> evt)
        {
            if (evt.newValue is SkillConditionOperator parsedOperator)
            {
                _conditionOperator = parsedOperator;
                if (_conditionOperator == SkillConditionOperator.IsTrue || _conditionOperator == SkillConditionOperator.IsFalse)
                {
                    _valueType = SkillBlackboardValueType.Bool;
                }
                else if (IsNumericOperator(_conditionOperator) && !IsNumericValueType(_valueType))
                {
                    _conditionOperator = SkillConditionOperator.Equal;
                    _operatorField.SetValueWithoutNotify(_conditionOperator);
                }

                UpdateUi();
                NotifyPropertiesChanged();
            }
        }

        public void SetAvailableVariables(IReadOnlyList<SkillVariableDef> variables)
        {
            _availableVariables.Clear();
            if (variables != null)
            {
                foreach (SkillVariableDef variable in variables)
                {
                    if (variable == null || string.IsNullOrWhiteSpace(variable.name))
                        continue;

                    if (_availableVariables.Any(existing => string.Equals(existing.name, variable.name, StringComparison.Ordinal)))
                        continue;

                    _availableVariables.Add(new SkillVariableDef
                    {
                        name = variable.name ?? string.Empty,
                        type = variable.type,
                        defaultValue = variable.defaultValue ?? string.Empty
                    });
                }
            }

            SyncValueTypeFromKey();
            RefreshKeyField();
            UpdateUi();
        }

        private void UpdateUi()
        {
            bool showValueType = RequiresValueType();
            bool showCompareValue = RequiresCompareValue();
            bool hasKey = !string.IsNullOrEmpty(_key);

            _valueTypeLabel.style.display = showValueType ? DisplayStyle.Flex : DisplayStyle.None;
            _valueTypeLabel.text = $"Value Type: {GetValueTypeText()}";
            _stringValueField.style.display = showCompareValue && hasKey && _valueType == SkillBlackboardValueType.String
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _floatValueField.style.display = showCompareValue && hasKey && _valueType == SkillBlackboardValueType.Float
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _intValueField.style.display = showCompareValue && hasKey && _valueType == SkillBlackboardValueType.Int
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _boolValueField.style.display = showCompareValue && hasKey && _valueType == SkillBlackboardValueType.Bool
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private bool RequiresValueType() =>
            _conditionOperator != SkillConditionOperator.Exists;

        private bool RequiresCompareValue() =>
            _conditionOperator != SkillConditionOperator.Exists &&
            _conditionOperator != SkillConditionOperator.IsTrue &&
            _conditionOperator != SkillConditionOperator.IsFalse;

        private static bool IsNumericOperator(SkillConditionOperator conditionOperator) =>
            conditionOperator == SkillConditionOperator.Greater ||
            conditionOperator == SkillConditionOperator.GreaterOrEqual ||
            conditionOperator == SkillConditionOperator.Less ||
            conditionOperator == SkillConditionOperator.LessOrEqual;

        private static bool IsNumericValueType(SkillBlackboardValueType valueType) =>
            valueType == SkillBlackboardValueType.Float || valueType == SkillBlackboardValueType.Int;

        private SkillBlackboardValueType GetEffectiveValueType() =>
            _conditionOperator == SkillConditionOperator.IsTrue || _conditionOperator == SkillConditionOperator.IsFalse
                ? SkillBlackboardValueType.Bool
                : _valueType;

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

        private void OnKeyChanged(ChangeEvent<string> evt)
        {
            _key = evt.newValue ?? string.Empty;
            SyncValueTypeFromKey();
            RefreshKeyField();
            UpdateUi();
            NotifyPropertiesChanged();
        }

        private void RefreshKeyField()
        {
            List<string> choices = BuildKeyChoices();
            _keyField.choices = choices;
            _keyField.SetValueWithoutNotify(GetSelectedKey(choices));
        }

        private List<string> BuildKeyChoices()
        {
            List<string> choices = new List<string> { string.Empty };
            foreach (SkillVariableDef variable in _availableVariables)
            {
                string variableName = variable.name;
                if (choices.Any(existing => string.Equals(existing, variableName, StringComparison.Ordinal)))
                    continue;

                choices.Add(variableName);
            }

            if (!string.IsNullOrEmpty(_key) &&
                !choices.Any(existing => string.Equals(existing, _key, StringComparison.Ordinal)))
            {
                choices.Insert(1, _key);
            }

            return choices;
        }

        private string GetSelectedKey(IReadOnlyList<string> choices)
        {
            if (choices == null || choices.Count == 0)
                return string.Empty;

            foreach (string choice in choices)
            {
                if (string.Equals(choice, _key, StringComparison.Ordinal))
                    return choice;
            }

            return choices[0];
        }

        private string FormatVariableName(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "<None>";

            bool isKnownVariable = _availableVariables.Any(variable => string.Equals(variable.name, key, StringComparison.Ordinal));
            return isKnownVariable ? key : $"{key} (Missing)";
        }

        private void SyncValueTypeFromKey()
        {
            SkillVariableDef selectedVariable = _availableVariables.FirstOrDefault(variable =>
                string.Equals(variable.name, _key, StringComparison.Ordinal));
            if (selectedVariable != null)
            {
                _valueType = selectedVariable.type;

                if (IsNumericOperator(_conditionOperator) && !IsNumericValueType(_valueType))
                {
                    _conditionOperator = SkillConditionOperator.Equal;
                    _operatorField.SetValueWithoutNotify(_conditionOperator);
                }
            }
        }

        private string GetValueTypeText()
        {
            if (_conditionOperator == SkillConditionOperator.IsTrue || _conditionOperator == SkillConditionOperator.IsFalse)
                return SkillBlackboardValueType.Bool.ToString();

            if (string.IsNullOrEmpty(_key))
                return MissingTypeText;

            SkillVariableDef selectedVariable = _availableVariables.FirstOrDefault(variable =>
                string.Equals(variable.name, _key, StringComparison.Ordinal));
            return selectedVariable != null ? selectedVariable.type.ToString() : MissingTypeText;
        }
    }

    internal sealed class BuffConditionNode : SkillGraphNode
    {
        private readonly IntegerField _buffIdField;
        private readonly IntegerField _minimumStackCountField;
        private readonly EnumField _targetSelectorField;

        private int _buffId;
        private int _minimumStackCount = 1;
        private SkillBuffTargetSelector _targetSelector = SkillBuffTargetSelector.Target;

        public BuffConditionNode()
            : base(SkillNodeType.BuffCondition, "Buff Condition")
        {
            AddFlowInput("In");
            AddFlowOutput("True");
            AddFlowOutput("False");

            _targetSelectorField = new EnumField("Target", _targetSelector);
            _targetSelectorField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillBuffTargetSelector selector)
                {
                    _targetSelector = selector;
                    NotifyPropertiesChanged();
                }
            });
            AddPropertyField(_targetSelectorField);

            _buffIdField = new IntegerField("Buff Id") { value = _buffId };
            _buffIdField.RegisterValueChangedCallback(evt =>
            {
                _buffId = Math.Max(0, evt.newValue);
                _buffIdField.SetValueWithoutNotify(_buffId);
                NotifyPropertiesChanged();
            });
            AddPropertyField(_buffIdField);

            _minimumStackCountField = new IntegerField("Min Stacks") { value = _minimumStackCount };
            _minimumStackCountField.RegisterValueChangedCallback(evt =>
            {
                _minimumStackCount = Math.Max(1, evt.newValue);
                _minimumStackCountField.SetValueWithoutNotify(_minimumStackCount);
                NotifyPropertiesChanged();
            });
            AddPropertyField(_minimumStackCountField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, RuntimePropertyKeys.TargetSelector, _targetSelector);
            AddProperty(properties, RuntimePropertyKeys.BuffId, _buffId);
            AddProperty(properties, RuntimePropertyKeys.MinimumStackCount, _minimumStackCount);
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            if (Enum.TryParse(
                    GetPropertyValue(properties, RuntimePropertyKeys.TargetSelector, _targetSelector.ToString()),
                    true,
                    out SkillBuffTargetSelector parsedSelector))
            {
                _targetSelector = parsedSelector;
            }

            _buffId = Math.Max(0, (int)GetFloatPropertyValue(properties, RuntimePropertyKeys.BuffId, _buffId));
            _minimumStackCount = Math.Max(1, (int)GetFloatPropertyValue(properties, RuntimePropertyKeys.MinimumStackCount, _minimumStackCount));

            _targetSelectorField.SetValueWithoutNotify(_targetSelector);
            _buffIdField.SetValueWithoutNotify(_buffId);
            _minimumStackCountField.SetValueWithoutNotify(_minimumStackCount);
        }
    }
}
