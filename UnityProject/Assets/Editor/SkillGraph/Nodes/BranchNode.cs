using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class BranchNode : SkillGraphNode, ISkillVariableBindableNode
    {
        private const string KeyProperty = "key";
        private const string ValueTypeProperty = "valueType";
        private const string MismatchSuffix = " (Type Mismatch)";

        private readonly PopupField<string> _keyField;
        private readonly List<SkillVariableDef> _availableVariables = new List<SkillVariableDef>();

        private string _key = string.Empty;

        public BranchNode()
            : base(SkillNodeType.Branch, "Branch")
        {
            AddFlowInput("In");
            AddFlowOutput("True");
            AddFlowOutput("False");

            _keyField = new PopupField<string>("Key", BuildKeyChoices(), 0, FormatVariableName, FormatVariableName);
            _keyField.RegisterValueChangedCallback(OnKeyChanged);
            AddPropertyField(_keyField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, KeyProperty, _key);
            AddProperty(properties, ValueTypeProperty, SkillBlackboardValueType.Bool);
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            _key = GetPropertyValue(properties, KeyProperty);
            RefreshKeyField();
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

            RefreshKeyField();
        }

        private void OnKeyChanged(ChangeEvent<string> evt)
        {
            _key = evt.newValue ?? string.Empty;
            RefreshKeyField();
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
                if (variable.type != SkillBlackboardValueType.Bool)
                    continue;

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

            SkillVariableDef matchedVariable = _availableVariables.FirstOrDefault(variable =>
                string.Equals(variable.name, key, StringComparison.Ordinal));
            if (matchedVariable == null)
                return $"{key} (Missing)";

            return matchedVariable.type == SkillBlackboardValueType.Bool
                ? key
                : $"{key}{MismatchSuffix}";
        }
    }
}
