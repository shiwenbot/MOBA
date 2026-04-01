using System;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphNode : Node
    {
        public static readonly Vector2 DefaultSize = new Vector2(200f, 150f);

        public SkillGraphNode(string nodeTitle)
        {
            title = nodeTitle;
            SetPosition(new Rect(Vector2.zero, DefaultSize));

            AddInputPort("Input", typeof(bool));
            AddOutputPort("Output", typeof(bool));
        }

        public Port AddInputPort(string name, Type type)
        {
            Port port = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, type);
            port.portName = name;
            inputContainer.Add(port);
            RefreshNode();
            return port;
        }

        public Port AddOutputPort(string name, Type type)
        {
            Port port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, type);
            port.portName = name;
            outputContainer.Add(port);
            RefreshNode();
            return port;
        }

        private void RefreshNode()
        {
            RefreshPorts();
            RefreshExpandedState();
        }
    }
}
