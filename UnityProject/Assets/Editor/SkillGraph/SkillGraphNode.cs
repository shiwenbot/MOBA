using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal interface ISkillVariableBindableNode
    {
        void SetAvailableVariables(IReadOnlyList<SkillVariableDef> variables);
    }

    internal abstract class SkillGraphNode : Node
    {
        public static readonly Vector2 DefaultSize = new Vector2(240f, 180f);
        private bool _isApplyingNodeData;

        protected SkillGraphNode(SkillNodeType nodeType, string nodeTitle)
        {
            NodeType = SkillNodeTypeUtility.ToTypeName(nodeType);
            Guid = GUID.Generate().ToString();
            title = nodeTitle;
            SetPosition(new Rect(Vector2.zero, DefaultSize));
        }

        public string Guid { get; private set; }

        public string NodeType { get; }

        public event Action PropertiesChanged;

        public SkillNodeData GetNodeData()
        {
            SkillNodeData nodeData = new SkillNodeData
            {
                guid = Guid,
                title = title,
                nodeType = NodeType,
                position = GetPosition().position,
                properties = new List<SkillNodePropertyData>()
            };

            WriteProperties(nodeData.properties);
            return nodeData;
        }

        public void ApplyNodeData(SkillNodeData nodeData)
        {
            if (nodeData == null)
                return;

            Guid = string.IsNullOrEmpty(nodeData.guid) ? GUID.Generate().ToString() : nodeData.guid;

            if (!string.IsNullOrEmpty(nodeData.title))
                title = nodeData.title;

            SetPosition(new Rect(nodeData.position, DefaultSize));
            _isApplyingNodeData = true;
            try
            {
                ReadProperties(nodeData.properties ?? new List<SkillNodePropertyData>());
                RefreshNode();
            }
            finally
            {
                _isApplyingNodeData = false;
            }
        }

        public Port GetPort(Direction direction, string portName)
        {
            IEnumerable<Port> portCollection = direction == Direction.Input
                ? inputContainer.Children().OfType<Port>()
                : outputContainer.Children().OfType<Port>();

            return portCollection.FirstOrDefault(port => port.portName == portName);
        }

        protected Port AddFlowInput(string portName, Port.Capacity capacity = Port.Capacity.Single) =>
            AddPort(portName, Direction.Input, capacity);

        protected Port AddFlowOutput(string portName, Port.Capacity capacity = Port.Capacity.Multi) =>
            AddPort(portName, Direction.Output, capacity);

        protected void AddPropertyField(VisualElement field)
        {
            extensionContainer.Add(field);
            RefreshNode();
        }

        protected string GetPropertyValue(IReadOnlyList<SkillNodePropertyData> properties, string key, string fallbackValue = "")
        {
            if (properties == null)
                return fallbackValue;

            foreach (SkillNodePropertyData property in properties)
            {
                if (property != null && property.key == key)
                    return property.value ?? fallbackValue;
            }

            return fallbackValue;
        }

        protected float GetFloatPropertyValue(IReadOnlyList<SkillNodePropertyData> properties, string key, float fallbackValue)
        {
            string rawValue = GetPropertyValue(properties, key, fallbackValue.ToString(CultureInfo.InvariantCulture));
            return float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue)
                ? parsedValue
                : fallbackValue;
        }

        protected void AddProperty(List<SkillNodePropertyData> properties, string key, string value)
        {
            properties.Add(new SkillNodePropertyData
            {
                key = key,
                value = value ?? string.Empty
            });
        }

        protected void AddProperty(List<SkillNodePropertyData> properties, string key, float value) =>
            AddProperty(properties, key, value.ToString(CultureInfo.InvariantCulture));

        protected void AddProperty(List<SkillNodePropertyData> properties, string key, int value) =>
            AddProperty(properties, key, value.ToString(CultureInfo.InvariantCulture));

        protected void AddProperty(List<SkillNodePropertyData> properties, string key, bool value) =>
            AddProperty(properties, key, value ? "true" : "false");

        protected void AddProperty(List<SkillNodePropertyData> properties, string key, Enum value) =>
            AddProperty(properties, key, value.ToString());

        protected void RefreshNode()
        {
            RefreshPorts();
            RefreshExpandedState();
        }

        protected void NotifyPropertiesChanged()
        {
            if (_isApplyingNodeData)
                return;

            PropertiesChanged?.Invoke();
        }

        protected virtual void WriteProperties(List<SkillNodePropertyData> properties)
        {
        }

        protected virtual void ReadProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
        }

        private Port AddPort(string portName, Direction direction, Port.Capacity capacity)
        {
            Port port = InstantiatePort(Orientation.Horizontal, direction, capacity, typeof(SkillFlowPort));
            port.portName = portName;

            if (direction == Direction.Input)
                inputContainer.Add(port);
            else
                outputContainer.Add(port);

            RefreshNode();
            return port;
        }
    }
}
