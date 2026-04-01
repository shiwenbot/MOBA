using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphEditor : EditorWindow
    {
        private SkillGraphView _graphView;
        private SkillGraphSearchWindow _searchWindow;

        [MenuItem("TEngine/Skill Graph Editor", false, 110)]
        public static void OpenWindow()
        {
            SkillGraphEditor window = GetWindow<SkillGraphEditor>();
            window.titleContent = new GUIContent("Skill Graph");
            window.minSize = new Vector2(800f, 600f);
            window.Show();
        }

        private void OnEnable()
        {
            rootVisualElement.Clear();
            CreateGraphView();
            CreateSearchWindow();
        }

        private void OnDisable()
        {
            if (_graphView != null)
            {
                rootVisualElement.Remove(_graphView);
                _graphView = null;
            }

            if (_searchWindow != null)
            {
                DestroyImmediate(_searchWindow);
                _searchWindow = null;
            }
        }

        private void CreateGraphView()
        {
            _graphView = new SkillGraphView();
            _graphView.StretchToParentSize();
            rootVisualElement.Add(_graphView);
        }

        private void CreateSearchWindow()
        {
            _searchWindow = CreateInstance<SkillGraphSearchWindow>();
            _searchWindow.Initialize(this, _graphView);
            _graphView.SetSearchWindow(_searchWindow);
        }
    }
}
