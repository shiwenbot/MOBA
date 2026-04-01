using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class ConditionNode : SkillGraphNode
    {
        private const string ConditionTypeKey = "conditionType";
        private const string CompareValueKey = "compareValue";

        private readonly EnumField _conditionTypeField;
        private readonly FloatField _compareValueField;

        private SkillConditionType _conditionType = SkillConditionType.AlwaysTrue;
        private float _compareValue = 0f;

        public ConditionNode()
            : base(SkillNodeType.Condition, "Condition")
        {
            AddFlowInput("In");
            AddFlowOutput("True");
            AddFlowOutput("False");

            _conditionTypeField = new EnumField("Condition Type", _conditionType);
            _conditionTypeField.RegisterValueChangedCallback(OnConditionTypeChanged);
            AddPropertyField(_conditionTypeField);

            _compareValueField = new FloatField("Compare Value") { value = _compareValue };
            _compareValueField.RegisterValueChangedCallback(evt => _compareValue = evt.newValue);
            AddPropertyField(_compareValueField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, ConditionTypeKey, _conditionType);
            AddProperty(properties, CompareValueKey, _compareValue);
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            if (Enum.TryParse(GetPropertyValue(properties, ConditionTypeKey, _conditionType.ToString()), true, out SkillConditionType conditionType))
                _conditionType = conditionType;

            _compareValue = GetFloatPropertyValue(properties, CompareValueKey, _compareValue);

            _conditionTypeField.SetValueWithoutNotify(_conditionType);
            _compareValueField.SetValueWithoutNotify(_compareValue);
        }

        private void OnConditionTypeChanged(ChangeEvent<Enum> evt)
        {
            if (evt.newValue is SkillConditionType conditionType)
                _conditionType = conditionType;
        }
    }
}
