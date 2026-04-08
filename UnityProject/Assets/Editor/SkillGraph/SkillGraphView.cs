using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphView : GraphView
    {
        private const float MiniMapWidth = 220f;
        private const float MiniMapHeight = 150f;
        private const float MiniMapMargin = 12f;

        private SkillGraphSearchWindow _searchWindowProvider;
        private readonly List<SkillVariableDef> _variables = new List<SkillVariableDef>();
        private bool _isRestoring;
        private readonly MiniMap _miniMap;

        public event Action GraphModified;

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
            graphViewChanged = OnGraphViewChanged;

            _miniMap = new MiniMap { anchored = true };
            _miniMap.style.position = Position.Absolute;
            _miniMap.style.width = MiniMapWidth;
            _miniMap.style.height = MiniMapHeight;
            _miniMap.style.right = MiniMapMargin;
            _miniMap.style.bottom = MiniMapMargin;
            Add(_miniMap);
        }

        public void SetSearchWindow(SkillGraphSearchWindow searchWindowProvider) =>
            _searchWindowProvider = searchWindowProvider;

        public SkillGraphNode CreateNode(SkillNodeType nodeType, Vector2 position)
        {
            SkillGraphNode node = SkillGraphNodeFactory.CreateNode(nodeType, position);
            AddElement(node);
            AttachNode(node);
            NotifyGraphModified();
            return node;
        }

        public SkillGraphNode CreateNode(string nodeType, Vector2 position)
        {
            SkillGraphNode node = SkillGraphNodeFactory.CreateNode(nodeType, position);
            AddElement(node);
            AttachNode(node);
            NotifyGraphModified();
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

        public void DeserializeGraph(SkillGraphData graphData, bool frameGraph = true)
        {
            ClearGraph();
            if (graphData == null)
                return;

            Dictionary<string, SkillGraphNode> nodeLookup = new Dictionary<string, SkillGraphNode>();
            foreach (SkillNodeData nodeData in graphData.nodes ?? new List<SkillNodeData>())
            {
                SkillGraphNode node = SkillGraphNodeFactory.CreateNode(nodeData);
                AddElement(node);
                AttachNode(node);
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

            if (frameGraph)
                FrameAll();
        }

        public void ClearGraph()
        {
            foreach (Edge edge in edges.ToList())
                RemoveElement(edge);

            foreach (SkillGraphNode node in nodes.OfType<SkillGraphNode>().ToList())
            {
                DetachNode(node);
                RemoveElement(node);
            }
        }

        public void SetRestoring(bool isRestoring) =>
            _isRestoring = isRestoring;

        public void SetVariables(IReadOnlyList<SkillVariableDef> variables)
        {
            _variables.Clear();
            if (variables != null)
            {
                foreach (SkillVariableDef variable in variables)
                {
                    if (variable == null || string.IsNullOrWhiteSpace(variable.name))
                        continue;

                    if (_variables.Any(existing => string.Equals(existing.name, variable.name, StringComparison.Ordinal)))
                        continue;

                    _variables.Add(new SkillVariableDef
                    {
                        name = variable.name ?? string.Empty,
                        type = variable.type,
                        defaultValue = variable.defaultValue ?? string.Empty
                    });
                }
            }

            foreach (SkillGraphNode node in nodes.OfType<SkillGraphNode>())
                BindNodeVariables(node);
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

            if (IsEditingTextInput())
                return;

            List<GraphElement> elementsToDelete = selection.OfType<GraphElement>().ToList();
            if (elementsToDelete.Count == 0)
                return;

            DeleteSelection();
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (graphViewChange.elementsToRemove != null)
            {
                foreach (GraphElement element in graphViewChange.elementsToRemove.OfType<GraphElement>())
                {
                    if (element is SkillGraphNode node)
                        DetachNode(node);
                }
            }

            if (_isRestoring)
                return graphViewChange;

            bool hasCreatedEdges = graphViewChange.edgesToCreate != null && graphViewChange.edgesToCreate.Count > 0;
            bool hasRemovedElements = graphViewChange.elementsToRemove != null &&
                                      graphViewChange.elementsToRemove.Any(element => element is SkillGraphNode || element is Edge);
            bool hasMovedNodes = graphViewChange.movedElements != null && graphViewChange.movedElements.Count > 0;

            if (hasCreatedEdges || hasRemovedElements || hasMovedNodes)
                NotifyGraphModified();

            return graphViewChange;
        }

        private void AttachNode(SkillGraphNode node)
        {
            if (node == null)
                return;

            node.PropertiesChanged -= OnNodePropertiesChanged;
            node.PropertiesChanged += OnNodePropertiesChanged;
            BindNodeVariables(node);
        }

        private void DetachNode(SkillGraphNode node)
        {
            if (node == null)
                return;

            node.PropertiesChanged -= OnNodePropertiesChanged;
        }

        private void OnNodePropertiesChanged() =>
            NotifyGraphModified();

        private void BindNodeVariables(SkillGraphNode node)
        {
            if (node is ISkillVariableBindableNode variableBindableNode)
                variableBindableNode.SetAvailableVariables(_variables);
        }

        private void NotifyGraphModified()
        {
            if (_isRestoring)
                return;

            GraphModified?.Invoke();
        }

        private bool IsEditingTextInput()
        {
            VisualElement focusedElement = panel?.focusController?.focusedElement as VisualElement;
            if (focusedElement == null)
                return false;

            return focusedElement is TextField ||
                   focusedElement is IntegerField ||
                   focusedElement is FloatField ||
                   focusedElement.GetFirstAncestorOfType<TextField>() != null ||
                   focusedElement.GetFirstAncestorOfType<IntegerField>() != null ||
                   focusedElement.GetFirstAncestorOfType<FloatField>() != null;
        }
    }
}
