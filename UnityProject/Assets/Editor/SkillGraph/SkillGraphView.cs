using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphView : GraphView
    {
        private SkillGraphSearchWindow _searchWindowProvider;

        public SkillGraphView()
        {
            style.flexGrow = 1f;

            GridBackground gridBackground = new GridBackground();
            Insert(0, gridBackground);
            gridBackground.StretchToParentSize();

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            nodeCreationRequest = OnNodeCreationRequested;
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        public void SetSearchWindow(SkillGraphSearchWindow searchWindowProvider) =>
            _searchWindowProvider = searchWindowProvider;

        public SkillGraphNode CreateNode(SkillNodeType nodeType, Vector2 position)
        {
            SkillGraphNode node = SkillGraphNodeFactory.CreateNode(nodeType, position);
            AddElement(node);
            return node;
        }

        public SkillGraphNode CreateNode(string nodeType, Vector2 position)
        {
            SkillGraphNode node = SkillGraphNodeFactory.CreateNode(nodeType, position);
            AddElement(node);
            return node;
        }

        public SkillGraphData SerializeGraph(string graphName)
        {
            SkillGraphData graphData = new SkillGraphData
            {
                graphName = graphName
            };

            graphData.nodes.AddRange(nodes
                .OfType<SkillGraphNode>()
                .Select(node => node.GetNodeData()));

            graphData.edges.AddRange(edges
                .Where(edge => edge.input?.node is SkillGraphNode && edge.output?.node is SkillGraphNode)
                .Select(edge => new SkillEdgeData
                {
                    outputNodeGuid = ((SkillGraphNode)edge.output.node).Guid,
                    outputPortName = edge.output.portName,
                    inputNodeGuid = ((SkillGraphNode)edge.input.node).Guid,
                    inputPortName = edge.input.portName
                }));

            return graphData;
        }

        public void DeserializeGraph(SkillGraphData graphData)
        {
            ClearGraph();
            if (graphData == null)
                return;

            Dictionary<string, SkillGraphNode> nodeLookup = new Dictionary<string, SkillGraphNode>();
            foreach (SkillNodeData nodeData in graphData.nodes ?? new List<SkillNodeData>())
            {
                SkillGraphNode node = SkillGraphNodeFactory.CreateNode(nodeData);
                AddElement(node);
                nodeLookup[node.Guid] = node;
            }

            foreach (SkillEdgeData edgeData in graphData.edges ?? new List<SkillEdgeData>())
            {
                if (!nodeLookup.TryGetValue(edgeData.outputNodeGuid, out SkillGraphNode outputNode) ||
                    !nodeLookup.TryGetValue(edgeData.inputNodeGuid, out SkillGraphNode inputNode))
                {
                    continue;
                }

                Port outputPort = outputNode.GetPort(Direction.Output, edgeData.outputPortName);
                Port inputPort = inputNode.GetPort(Direction.Input, edgeData.inputPortName);
                if (outputPort == null || inputPort == null)
                    continue;

                Edge edge = outputPort.ConnectTo(inputPort);
                AddElement(edge);
            }

            FrameAll();
        }

        public void ClearGraph()
        {
            foreach (Edge edge in edges.ToList())
                RemoveElement(edge);

            foreach (SkillGraphNode node in nodes.OfType<SkillGraphNode>().ToList())
                RemoveElement(node);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList()
                .Where(endPort => endPort != startPort
                    && endPort.node != startPort.node
                    && endPort.direction != startPort.direction
                    && endPort.portType == startPort.portType)
                .ToList();
        }

        private void OnNodeCreationRequested(NodeCreationContext context)
        {
            if (_searchWindowProvider == null)
                return;

            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _searchWindowProvider);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace)
                return;

            List<GraphElement> elementsToDelete = selection.OfType<GraphElement>().ToList();
            if (elementsToDelete.Count == 0)
                return;

            DeleteElements(elementsToDelete);
            evt.StopPropagation();
        }
    }
}
