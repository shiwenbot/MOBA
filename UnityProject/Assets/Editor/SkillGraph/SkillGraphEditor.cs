using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphEditor : EditorWindow
    {
        [SerializeField]
        private string _currentGraphAssetPath;

        [SerializeField]
        private string _currentGraphName = "NewSkillGraph";

        private SkillGraphView _graphView;
        private SkillGraphSearchWindow _searchWindow;
        private Label _pathLabel;

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
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            CreateToolbar();
            CreateGraphView();
            CreateSearchWindow();
            UpdateWindowState();
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

        private void CreateToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(CreateNewGraph) { text = "New" });
            toolbar.Add(new ToolbarButton(SaveGraph) { text = "Save" });
            toolbar.Add(new ToolbarButton(SaveGraphAs) { text = "Save As" });
            toolbar.Add(new ToolbarButton(LoadGraph) { text = "Load" });

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            _pathLabel = new Label();
            _pathLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _pathLabel.style.flexShrink = 1f;
            toolbar.Add(_pathLabel);

            rootVisualElement.Add(toolbar);
        }

        private void CreateGraphView()
        {
            _graphView = new SkillGraphView();
            rootVisualElement.Add(_graphView);
        }

        private void CreateSearchWindow()
        {
            _searchWindow = CreateInstance<SkillGraphSearchWindow>();
            _searchWindow.Initialize(this, _graphView);
            _graphView.SetSearchWindow(_searchWindow);
        }

        private void CreateNewGraph()
        {
            _graphView.ClearGraph();
            _currentGraphAssetPath = string.Empty;
            _currentGraphName = "NewSkillGraph";
            UpdateWindowState();
        }

        private void SaveGraph()
        {
            if (string.IsNullOrEmpty(_currentGraphAssetPath))
            {
                SaveGraphAs();
                return;
            }

            WriteGraphFile(_currentGraphAssetPath);
        }

        private void SaveGraphAs()
        {
            SkillGraphPaths.EnsureGraphDataDirectory();

            string absolutePath = EditorUtility.SaveFilePanel(
                "Save Skill Graph",
                SkillGraphPaths.GetAbsoluteGraphDataDirectory(),
                GetDefaultGraphName(),
                "json");
            if (string.IsNullOrEmpty(absolutePath))
                return;

            string projectRelativePath = SkillGraphPaths.ToProjectRelativePath(absolutePath);
            if (string.IsNullOrEmpty(projectRelativePath) ||
                !projectRelativePath.StartsWith(SkillGraphPaths.GraphDataDirectory))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Path",
                    $"Skill graph files must be stored under {SkillGraphPaths.GraphDataDirectory}.",
                    "OK");
                return;
            }

            WriteGraphFile(projectRelativePath);
        }

        private void LoadGraph()
        {
            SkillGraphPaths.EnsureGraphDataDirectory();

            string absolutePath = EditorUtility.OpenFilePanel(
                "Load Skill Graph",
                SkillGraphPaths.GetAbsoluteGraphDataDirectory(),
                "json");
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return;

            SkillGraphData graphData = JsonUtility.FromJson<SkillGraphData>(File.ReadAllText(absolutePath));
            if (graphData == null)
            {
                EditorUtility.DisplayDialog("Load Failed", "Selected file is not a valid skill graph json.", "OK");
                return;
            }

            _graphView.DeserializeGraph(graphData);

            _currentGraphAssetPath = SkillGraphPaths.ToProjectRelativePath(absolutePath);
            _currentGraphName = string.IsNullOrEmpty(graphData.graphName)
                ? Path.GetFileNameWithoutExtension(absolutePath)
                : graphData.graphName;

            UpdateWindowState();
        }

        private void WriteGraphFile(string projectRelativePath)
        {
            string normalizedPath = SkillGraphPaths.NormalizePath(projectRelativePath);
            string graphName = Path.GetFileNameWithoutExtension(normalizedPath);
            SkillGraphData graphData = _graphView.SerializeGraph(graphName);
            string json = JsonUtility.ToJson(graphData, true);
            string absolutePath = SkillGraphPaths.ToAbsolutePath(normalizedPath);
            string directory = Path.GetDirectoryName(absolutePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(absolutePath, json, new UTF8Encoding(false));
            AssetDatabase.Refresh();

            _currentGraphAssetPath = normalizedPath;
            _currentGraphName = graphData.graphName;
            UpdateWindowState();
        }

        private string GetDefaultGraphName()
        {
            if (!string.IsNullOrEmpty(_currentGraphAssetPath))
                return Path.GetFileNameWithoutExtension(_currentGraphAssetPath);

            return string.IsNullOrWhiteSpace(_currentGraphName) ? "NewSkillGraph" : _currentGraphName;
        }

        private void UpdateWindowState()
        {
            string graphName = GetDefaultGraphName();
            titleContent = new GUIContent($"Skill Graph - {graphName}");

            if (_pathLabel != null)
            {
                _pathLabel.text = string.IsNullOrEmpty(_currentGraphAssetPath)
                    ? "Unsaved"
                    : SkillGraphPaths.NormalizePath(_currentGraphAssetPath);
            }
        }
    }
}
