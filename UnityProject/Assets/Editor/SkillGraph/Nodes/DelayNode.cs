using System.Collections.Generic;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class DelayNode : SkillGraphNode
    {
        private const string DurationKey = "duration";

        private readonly FloatField _durationField;

        private float _duration = 1f;

        public DelayNode()
            : base(SkillNodeType.Delay, "Delay")
        {
            AddFlowInput("In");
            AddFlowOutput("Out");

            _durationField = new FloatField("Duration") { value = _duration };
            _durationField.RegisterValueChangedCallback(evt => _duration = evt.newValue);
            AddPropertyField(_durationField);
        }

        protected override void WriteProperties(List<SkillNodePropertyData> properties) =>
            AddProperty(properties, DurationKey, _duration);

        protected override void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            _duration = GetFloatPropertyValue(properties, DurationKey, _duration);
            _durationField.SetValueWithoutNotify(_duration);
        }
    }
}
