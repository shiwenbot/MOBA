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
                new SearchTreeGroupEntry(new GUIContent("Entry"), 1),
                new SearchTreeEntry(new GUIContent("Entry Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.Entry
                },
                new SearchTreeGroupEntry(new GUIContent("Debug"), 1),
                new SearchTreeEntry(new GUIContent("Debug Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.Debug
                },
                new SearchTreeGroupEntry(new GUIContent("Action"), 1),
                new SearchTreeEntry(new GUIContent("Action Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.Action
                },
                new SearchTreeGroupEntry(new GUIContent("Condition"), 1),
                new SearchTreeEntry(new GUIContent("Condition Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.Condition
                },
                new SearchTreeGroupEntry(new GUIContent("Branch"), 1),
                new SearchTreeEntry(new GUIContent("Branch Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.Branch
                },
                new SearchTreeGroupEntry(new GUIContent("Variables"), 1),
                new SearchTreeEntry(new GUIContent("Set Variable Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.SetVariable
                },
                new SearchTreeGroupEntry(new GUIContent("Delay"), 1),
                new SearchTreeEntry(new GUIContent("Delay Node", _indentationIcon))
                {
                    level = 2,
                    userData = SkillNodeType.Delay
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

            if (searchTreeEntry.userData is SkillNodeType nodeType)
            {
                _graphView.CreateNode(nodeType, graphMousePosition);
                return true;
            }

            string nodeTypeName = searchTreeEntry.userData as string;
            _graphView.CreateNode(nodeTypeName, graphMousePosition);
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
