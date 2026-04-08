using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphBlackboardPanel : VisualElement
    {
        private const float PanelWidth = 200f;
        private const float RowHeight = 170f;

        private readonly List<SkillVariableDef> _variables = new List<SkillVariableDef>();
        private readonly ListView _listView;

        public event Action VariablesChanged;

        public SkillGraphBlackboardPanel()
        {
            style.flexShrink = 0f;
            style.width = PanelWidth;
            style.minWidth = PanelWidth;
            style.maxWidth = PanelWidth;
            style.marginRight = 4f;
            style.paddingLeft = 6f;
            style.paddingRight = 6f;
            style.paddingTop = 4f;
            style.paddingBottom = 4f;

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4f;
            Add(header);

            Label title = new Label("Blackboard");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            header.Add(title);

            Button addButton = new Button(AddVariable) { text = "+ Add" };
            header.Add(addButton);

            _listView = new ListView
            {
                itemsSource = _variables,
                makeItem = MakeRow,
                bindItem = BindRow,
                selectionType = SelectionType.None,
                reorderable = false,
                fixedItemHeight = RowHeight
            };
            _listView.style.flexGrow = 1f;
            Add(_listView);
        }

        public void SetVariables(IReadOnlyList<SkillVariableDef> variables)
        {
            _variables.Clear();
            if (variables != null)
            {
                foreach (SkillVariableDef variable in variables)
                    _variables.Add(CloneVariable(variable));
            }

            _listView.Rebuild();
        }

        public List<SkillVariableDef> GetVariablesSnapshot() =>
            _variables.Select(CloneVariable).ToList();

        private VisualElement MakeRow()
        {
            VariableRowElement row = new VariableRowElement();
            row.NameField.RegisterValueChangedCallback(evt =>
            {
                if (!TryGetVariable(row.Index, out SkillVariableDef variable))
                    return;

                string normalizedName = NormalizeVariableName(evt.newValue, row.Index, variable.name);
                if (!string.Equals(normalizedName, row.NameField.value, StringComparison.Ordinal))
                    row.NameField.SetValueWithoutNotify(normalizedName);

                variable.name = normalizedName;
                NotifyVariablesChanged();
            });

            row.TypeField.RegisterValueChangedCallback(evt =>
            {
                if (!TryGetVariable(row.Index, out SkillVariableDef variable))
                    return;

                if (evt.newValue is SkillBlackboardValueType variableType)
                {
                    variable.type = variableType;
                    variable.defaultValue = NormalizeDefaultValue(variable.defaultValue, variable.type);
                    BindDefaultValue(row, variable);
                    NotifyVariablesChanged();
                }
            });

            row.DefaultStringField.RegisterValueChangedCallback(evt =>
            {
                if (!TryGetVariable(row.Index, out SkillVariableDef variable))
                    return;

                variable.defaultValue = evt.newValue ?? string.Empty;
                NotifyVariablesChanged();
            });

            row.DefaultFloatField.RegisterValueChangedCallback(evt =>
            {
                if (!TryGetVariable(row.Index, out SkillVariableDef variable))
                    return;

                variable.defaultValue = evt.newValue.ToString(CultureInfo.InvariantCulture);
                NotifyVariablesChanged();
            });

            row.DefaultIntField.RegisterValueChangedCallback(evt =>
            {
                if (!TryGetVariable(row.Index, out SkillVariableDef variable))
                    return;

                variable.defaultValue = evt.newValue.ToString(CultureInfo.InvariantCulture);
                NotifyVariablesChanged();
            });

            row.DefaultBoolField.RegisterValueChangedCallback(evt =>
            {
                if (!TryGetVariable(row.Index, out SkillVariableDef variable))
                    return;

                variable.defaultValue = evt.newValue ? "true" : "false";
                NotifyVariablesChanged();
            });

            row.DeleteButton.clicked += () =>
            {
                if (row.Index < 0 || row.Index >= _variables.Count)
                    return;

                _variables.RemoveAt(row.Index);
                _listView.Rebuild();
                NotifyVariablesChanged();
            };

            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            if (!(element is VariableRowElement row) || !TryGetVariable(index, out SkillVariableDef variable))
                return;

            row.Index = index;
            variable.name = NormalizeVariableName(variable.name, index, GenerateNewVariableName());
            row.NameField.SetValueWithoutNotify(variable.name);
            row.TypeField.SetValueWithoutNotify(variable.type);
            BindDefaultValue(row, variable);
        }

        private void AddVariable()
        {
            SkillVariableDef variable = new SkillVariableDef
            {
                name = GenerateNewVariableName(),
                type = SkillBlackboardValueType.String,
                defaultValue = string.Empty
            };

            _variables.Add(variable);
            _listView.Rebuild();
            NotifyVariablesChanged();
        }

        private bool TryGetVariable(int index, out SkillVariableDef variable)
        {
            variable = null;
            if (index < 0 || index >= _variables.Count)
                return false;

            variable = _variables[index];
            return variable != null;
        }

        private string GenerateNewVariableName()
        {
            int index = _variables.Count + 1;
            while (true)
            {
                string candidate = $"var{index}";
                bool exists = _variables.Any(variable => string.Equals(variable?.name, candidate, StringComparison.Ordinal));
                if (!exists)
                    return candidate;

                index++;
            }
        }

        private string NormalizeVariableName(string rawName, int currentIndex, string fallbackName)
        {
            string normalized = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
                normalized = string.IsNullOrWhiteSpace(fallbackName) ? GenerateNewVariableName() : fallbackName;

            return EnsureUniqueVariableName(normalized, currentIndex);
        }

        private string EnsureUniqueVariableName(string baseName, int currentIndex)
        {
            string candidate = baseName;
            int suffix = 1;
            while (true)
            {
                bool exists = false;
                for (int index = 0; index < _variables.Count; index++)
                {
                    if (index == currentIndex)
                        continue;

                    if (string.Equals(_variables[index]?.name, candidate, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    return candidate;

                candidate = $"{baseName}_{suffix}";
                suffix++;
            }
        }

        private void BindDefaultValue(VariableRowElement row, SkillVariableDef variable)
        {
            SkillBlackboardValueType variableType = variable?.type ?? SkillBlackboardValueType.String;
            string defaultValue = NormalizeDefaultValue(variable?.defaultValue, variableType);
            if (variable != null)
                variable.defaultValue = defaultValue;

            row.DefaultStringField.style.display = variableType == SkillBlackboardValueType.String ? DisplayStyle.Flex : DisplayStyle.None;
            row.DefaultFloatField.style.display = variableType == SkillBlackboardValueType.Float ? DisplayStyle.Flex : DisplayStyle.None;
            row.DefaultIntField.style.display = variableType == SkillBlackboardValueType.Int ? DisplayStyle.Flex : DisplayStyle.None;
            row.DefaultBoolField.style.display = variableType == SkillBlackboardValueType.Bool ? DisplayStyle.Flex : DisplayStyle.None;

            row.DefaultStringField.SetValueWithoutNotify(defaultValue);
            row.DefaultFloatField.SetValueWithoutNotify(ParseFloat(defaultValue));
            row.DefaultIntField.SetValueWithoutNotify(ParseInt(defaultValue));
            row.DefaultBoolField.SetValueWithoutNotify(ParseBool(defaultValue));
        }

        private static string NormalizeDefaultValue(string value, SkillBlackboardValueType variableType)
        {
            string rawValue = value ?? string.Empty;
            switch (variableType)
            {
                case SkillBlackboardValueType.Float:
                    return ParseFloat(rawValue).ToString(CultureInfo.InvariantCulture);
                case SkillBlackboardValueType.Int:
                    return ParseInt(rawValue).ToString(CultureInfo.InvariantCulture);
                case SkillBlackboardValueType.Bool:
                    return ParseBool(rawValue) ? "true" : "false";
                case SkillBlackboardValueType.String:
                default:
                    return rawValue;
            }
        }

        private static float ParseFloat(string rawValue)
        {
            return float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat)
                ? parsedFloat
                : 0f;
        }

        private static int ParseInt(string rawValue)
        {
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt)
                ? parsedInt
                : 0;
        }

        private static bool ParseBool(string rawValue)
        {
            return bool.TryParse(rawValue, out bool parsedBool) && parsedBool;
        }

        private void NotifyVariablesChanged() =>
            VariablesChanged?.Invoke();

        private static SkillVariableDef CloneVariable(SkillVariableDef variable)
        {
            if (variable == null)
                return new SkillVariableDef();

            return new SkillVariableDef
            {
                name = variable.name ?? string.Empty,
                type = variable.type,
                defaultValue = variable.defaultValue ?? string.Empty
            };
        }

        private sealed class VariableRowElement : VisualElement
        {
            public readonly TextField NameField;
            public readonly EnumField TypeField;
            public readonly TextField DefaultStringField;
            public readonly FloatField DefaultFloatField;
            public readonly IntegerField DefaultIntField;
            public readonly Toggle DefaultBoolField;
            public readonly Button DeleteButton;

            public int Index { get; set; }

            public VariableRowElement()
            {
                style.flexDirection = FlexDirection.Column;
                style.paddingLeft = 2f;
                style.paddingRight = 2f;
                style.paddingTop = 4f;
                style.paddingBottom = 4f;
                style.borderBottomWidth = 1f;

                NameField = new TextField("Name");
                Add(NameField);

                TypeField = new EnumField("Type", SkillBlackboardValueType.String);
                Add(TypeField);

                DefaultStringField = new TextField("Default");
                Add(DefaultStringField);

                DefaultFloatField = new FloatField("Default");
                Add(DefaultFloatField);

                DefaultIntField = new IntegerField("Default");
                Add(DefaultIntField);

                DefaultBoolField = new Toggle("Default");
                Add(DefaultBoolField);

                DeleteButton = new Button { text = "Delete" };
                Add(DeleteButton);
            }
        }
    }
}
