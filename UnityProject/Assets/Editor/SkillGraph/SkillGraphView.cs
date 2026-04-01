using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
            style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);

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

        public SkillGraphNode CreateNode(string nodeTitle, Vector2 position)
        {
            SkillGraphNode node = new SkillGraphNode(nodeTitle);
            node.SetPosition(new Rect(position, SkillGraphNode.DefaultSize));
            AddElement(node);
            return node;
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
