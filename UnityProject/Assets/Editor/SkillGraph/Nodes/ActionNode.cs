using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class ActionNode : SkillGraphNode
    {
        private const string ActionTypeKey = "actionType";
        private const string ValueKey = "value";

        private readonly EnumField _actionTypeField;
        private readonly FloatField _valueField;

        private SkillActionType _actionType = SkillActionType.PlayAnimation;
        private float _value = 1f;

        public ActionNode()
            : base(SkillNodeType.Action, "Action")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _actionTypeField = new EnumField("Action Type", _actionType);
            _actionTypeField.RegisterValueChangedCallback(OnActionTypeChanged);
            AddPropertyField(_actionTypeField);

            _valueField = new FloatField("Value") { value = _value };
            _valueField.RegisterValueChangedCallback(evt => _value = evt.newValue);
            AddPropertyField(_valueField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties)
        {
            AddProperty(properties, ActionTypeKey, _actionType);
            AddProperty(properties, ValueKey, _value);
        }

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            if (Enum.TryParse(GetPropertyValue(properties, ActionTypeKey, _actionType.ToString()), true, out SkillActionType actionType))
                _actionType = actionType;

            _value = GetFloatPropertyValue(properties, ValueKey, _value);

            _actionTypeField.SetValueWithoutNotify(_actionType);
            _valueField.SetValueWithoutNotify(_value);
        }

        private void OnActionTypeChanged(ChangeEvent<Enum> evt)
        {
            if (evt.newValue is SkillActionType actionType)
                _actionType = actionType;
        }
    }
}
