using System.Collections.Generic;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class DebugNode : SkillGraphNode
    {
        private const string MessageKey = "message";

        private readonly TextField _messageField;

        private string _message = string.Empty;

        public DebugNode()
            : base(SkillNodeType.Debug, "Debug")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _messageField = new TextField("Message") { value = _message };
            _messageField.RegisterValueChangedCallback(evt => _message = evt.newValue ?? string.Empty);
            AddPropertyField(_messageField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties) =>
            AddProperty(properties, MessageKey, _message);

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            _message = GetPropertyValue(properties, MessageKey);
            _messageField.SetValueWithoutNotify(_message);
        }
    }
}
