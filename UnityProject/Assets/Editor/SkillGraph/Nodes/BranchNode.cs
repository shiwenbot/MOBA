using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class BranchNode : SkillGraphNode
    {
        private const string KeyProperty = "key";
        private const string ValueTypeProperty = "valueType";

        private readonly TextField _keyField;

        private string _key = string.Empty;

        public BranchNode()
            : base(SkillNodeType.Branch, "Branch")
        {
            AddFlowInput("In");
            AddFlowOutput("True");
            AddFlowOutput("False");

            _keyField = new TextField("Key") { value = _key };
            _keyField.RegisterValueChangedCallback(evt => _key = evt.newValue ?? string.Empty);
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
            _keyField.SetValueWithoutNotify(_key);
        }
    }
}
