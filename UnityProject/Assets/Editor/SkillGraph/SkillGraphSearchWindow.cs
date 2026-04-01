using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphSearchWindow : ScriptableObject, ISearchWindowProvider
    {
        private EditorWindow _editorWindow;
        private SkillGraphView _graphView;
        private Texture2D _indentationIcon;

        public void Initialize(EditorWindow editorWindow, SkillGraphView graphView)
        {
            _editorWindow = editorWindow;
            _graphView = graphView;

            if (_indentationIcon == null)
            {
                _indentationIcon = new Texture2D(1, 1);
                _indentationIcon.SetPixel(0, 0, Color.clear);
                _indentationIcon.Apply();
                _indentationIcon.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            return new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Skill Node"), 0),
                new SearchTreeEntry(new GUIContent("Skill Node", _indentationIcon))
                {
                    level = 1,
                    userData = "Skill Node"
                }
            };
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            VisualElement rootVisualElement = _editorWindow.rootVisualElement;
            VisualElement targetElement = rootVisualElement.parent ?? rootVisualElement;
            Vector2 windowMousePosition = rootVisualElement.ChangeCoordinatesTo(
                targetElement,
                context.screenMousePosition - _editorWindow.position.position);
            Vector2 graphMousePosition = _graphView.contentViewContainer.WorldToLocal(windowMousePosition);

            string nodeTitle = searchTreeEntry.userData as string ?? "Skill Node";
            _graphView.CreateNode(nodeTitle, graphMousePosition);
            return true;
        }

        private void OnDisable()
        {
            if (_indentationIcon != null)
            {
                DestroyImmediate(_indentationIcon);
                _indentationIcon = null;
            }
        }
    }
}
