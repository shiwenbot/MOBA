using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameShared.SkillGraph;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphEditor : EditorWindow
    {
        private const int UndoRecordDebounceMilliseconds = 200;

        [SerializeField]
        private string _currentGraphAssetPath;

        [SerializeField]
        private string _currentGraphName = "NewSkillGraph";

        [SerializeField]
        private List<SkillVariableDef> _variables = new List<SkillVariableDef>();

        [SerializeField]
        private string _currentSyncMode = RuntimeSyncModes.LocalOnly;

        private static readonly List<string> SyncModeOptions = new List<string>
        {
            RuntimeSyncModes.LocalOnly,
            RuntimeSyncModes.Lockstep
        };

        private SkillGraphView _graphView;
        private SkillGraphSearchWindow _searchWindow;
        private SkillGraphBlackboardPanel _blackboardPanel;
        private SkillGraphUndoSystem _undoSystem;
        private VisualElement _contentRoot;
        private Label _pathLabel;
        private PopupField<string> _syncModePopup;
        private HelpBox _lockstepRiskHelpBox;
        private IVisualElementScheduledItem _pendingRecordSchedule;
        private bool _isRestoringSnapshot;

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

            _undoSystem = new SkillGraphUndoSystem();

            CreateToolbar();
            CreateContentLayout();
            CreateBlackboardPanel();
            CreateGraphView();
            CreateSearchWindow();
            RegisterCallbacks();
            ApplyVariablesToUi();
            ResetUndoHistoryToCurrentState();
            UpdateWindowState();
            UpdateLockstepRiskHint();
        }

        private void OnDisable()
        {
            _pendingRecordSchedule?.Pause();
            _pendingRecordSchedule = null;

            UnregisterCallbacks();

            if (_searchWindow != null)
            {
                DestroyImmediate(_searchWindow);
                _searchWindow = null;
            }

            if (_contentRoot != null)
            {
                rootVisualElement.Remove(_contentRoot);
                _contentRoot = null;
            }

            _graphView = null;
            _blackboardPanel = null;
            _syncModePopup = null;
            _lockstepRiskHelpBox = null;
        }

        private void CreateToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(CreateNewGraph) { text = "New" });
            toolbar.Add(new ToolbarButton(SaveGraph) { text = "Save" });
            toolbar.Add(new ToolbarButton(SaveGraphAs) { text = "Save As" });
            toolbar.Add(new ToolbarButton(LoadGraph) { text = "Load" });
            toolbar.Add(new ToolbarButton(ExportGraph) { text = "Export" });
            _syncModePopup = new PopupField<string>("Sync", SyncModeOptions, ResolveSyncModeIndex(_currentSyncMode));
            _syncModePopup.style.minWidth = 170f;
            _syncModePopup.RegisterValueChangedCallback(OnSyncModeChanged);
            toolbar.Add(_syncModePopup);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            _pathLabel = new Label();
            _pathLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _pathLabel.style.flexShrink = 1f;
            toolbar.Add(_pathLabel);

            rootVisualElement.Add(toolbar);
        }

        private void CreateContentLayout()
        {
            _lockstepRiskHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _lockstepRiskHelpBox.style.display = DisplayStyle.None;
            rootVisualElement.Add(_lockstepRiskHelpBox);

            _contentRoot = new VisualElement();
            _contentRoot.style.flexDirection = FlexDirection.Row;
            _contentRoot.style.flexGrow = 1f;
            rootVisualElement.Add(_contentRoot);
        }

        private void CreateBlackboardPanel()
        {
            _blackboardPanel = new SkillGraphBlackboardPanel();
            _contentRoot.Add(_blackboardPanel);
        }

        private void CreateGraphView()
        {
            _graphView = new SkillGraphView();
            _contentRoot.Add(_graphView);
        }

        private void CreateSearchWindow()
        {
            _searchWindow = CreateInstance<SkillGraphSearchWindow>();
            _searchWindow.Initialize(this, _graphView);
            _graphView.SetSearchWindow(_searchWindow);
        }

        private void CreateNewGraph()
        {
            _currentGraphAssetPath = string.Empty;
            _currentGraphName = "NewSkillGraph";

            SkillGraphData graphData = new SkillGraphData
            {
                graphName = _currentGraphName,
                syncMode = RuntimeSyncModes.LocalOnly
            };

            ApplyGraphData(graphData);
            ResetUndoHistoryToCurrentState();
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

            _currentGraphAssetPath = SkillGraphPaths.ToProjectRelativePath(absolutePath);
            _currentGraphName = string.IsNullOrEmpty(graphData.graphName)
                ? Path.GetFileNameWithoutExtension(absolutePath)
                : graphData.graphName;
            _currentSyncMode = NormalizeSyncMode(graphData.syncMode);

            ApplyGraphData(graphData);
            ResetUndoHistoryToCurrentState();
            UpdateWindowState();
        }

        private void WriteGraphFile(string projectRelativePath)
        {
            string normalizedPath = SkillGraphPaths.NormalizePath(projectRelativePath);
            string graphName = Path.GetFileNameWithoutExtension(normalizedPath);
            SkillGraphData graphData = BuildGraphData(graphName);
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

        private void ExportGraph()
        {
            string graphName = GetDefaultGraphName();
            SkillGraphData graphData = BuildGraphData(graphName);
            if (SkillGraphExporter.Export(graphData, out string exportPath, out string errorMessage))
            {
                _currentGraphName = graphData.graphName;
                UpdateWindowState();
                EditorUtility.DisplayDialog("Export Success", $"Exported runtime graph to:\n{exportPath}", "OK");
                return;
            }

            EditorUtility.DisplayDialog("Export Failed", errorMessage, "OK");
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

        private void RegisterCallbacks()
        {
            if (_graphView != null)
                _graphView.GraphModified += OnGraphModified;

            if (_blackboardPanel != null)
                _blackboardPanel.VariablesChanged += OnVariablesChanged;

            rootVisualElement.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
        }

        private void UnregisterCallbacks()
        {
            if (_graphView != null)
                _graphView.GraphModified -= OnGraphModified;

            if (_blackboardPanel != null)
                _blackboardPanel.VariablesChanged -= OnVariablesChanged;

            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
        }

        private void OnGraphModified()
        {
            if (_isRestoringSnapshot)
                return;

            QueueSnapshotRecord();
            UpdateLockstepRiskHint();
        }

        private void OnVariablesChanged()
        {
            if (_isRestoringSnapshot)
                return;

            _variables = _blackboardPanel.GetVariablesSnapshot();
            _graphView.SetVariables(_variables);
            QueueSnapshotRecord();
            UpdateLockstepRiskHint();
        }

        private void OnSyncModeChanged(ChangeEvent<string> evt)
        {
            _currentSyncMode = NormalizeSyncMode(evt.newValue);
            if (_isRestoringSnapshot)
                return;

            QueueSnapshotRecord();
            UpdateLockstepRiskHint();
        }

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (_isRestoringSnapshot || IsEditingTextInput())
                return;

            bool isCommandPressed = evt.ctrlKey || evt.commandKey;
            if (!isCommandPressed)
                return;

            if (evt.keyCode == KeyCode.Z && !evt.shiftKey)
            {
                TryUndo();
                evt.StopImmediatePropagation();
                evt.PreventDefault();
                return;
            }

            if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shiftKey))
            {
                TryRedo();
                evt.StopImmediatePropagation();
                evt.PreventDefault();
            }
        }

        private void TryUndo()
        {
            FlushPendingSnapshotRecord();
            if (_undoSystem == null || !_undoSystem.TryUndo(out string snapshotJson))
                return;

            RestoreSnapshot(snapshotJson);
        }

        private void TryRedo()
        {
            FlushPendingSnapshotRecord();
            if (_undoSystem == null || !_undoSystem.TryRedo(out string snapshotJson))
                return;

            RestoreSnapshot(snapshotJson);
        }

        private void QueueSnapshotRecord()
        {
            if (_isRestoringSnapshot)
                return;

            _pendingRecordSchedule?.Pause();
            _pendingRecordSchedule = rootVisualElement.schedule
                .Execute(RecordSnapshotNow)
                .StartingIn(UndoRecordDebounceMilliseconds);
        }

        private void FlushPendingSnapshotRecord()
        {
            if (_pendingRecordSchedule == null)
                return;

            _pendingRecordSchedule.Pause();
            _pendingRecordSchedule = null;
            RecordSnapshotNow();
        }

        private void RecordSnapshotNow()
        {
            _pendingRecordSchedule = null;
            if (_isRestoringSnapshot || _undoSystem == null || _graphView == null)
                return;

            _undoSystem.Record(CaptureSnapshotJson());
        }

        private void ResetUndoHistoryToCurrentState()
        {
            if (_undoSystem == null)
                return;

            _undoSystem.Clear();
            RecordSnapshotNow();
        }

        private string CaptureSnapshotJson()
        {
            SkillGraphData graphData = BuildGraphData(GetDefaultGraphName());
            return JsonUtility.ToJson(graphData);
        }

        private SkillGraphData BuildGraphData(string graphName)
        {
            SkillGraphData graphData = _graphView.SerializeGraph(graphName);
            graphData.syncMode = NormalizeSyncMode(_currentSyncMode);
            graphData.variables = CloneVariables(_variables);
            return graphData;
        }

        private void RestoreSnapshot(string snapshotJson)
        {
            SkillGraphData snapshotData = string.IsNullOrEmpty(snapshotJson)
                ? new SkillGraphData()
                : JsonUtility.FromJson<SkillGraphData>(snapshotJson) ?? new SkillGraphData();

            ApplyGraphData(snapshotData, false);
            UpdateWindowState();
        }

        private void ApplyGraphData(SkillGraphData graphData, bool frameGraph = true)
        {
            SkillGraphData safeGraphData = graphData ?? new SkillGraphData();
            _variables = CloneVariables(safeGraphData.variables);
            _currentSyncMode = NormalizeSyncMode(safeGraphData.syncMode);

            if (!string.IsNullOrWhiteSpace(safeGraphData.graphName))
                _currentGraphName = safeGraphData.graphName;

            _isRestoringSnapshot = true;
            _graphView.SetRestoring(true);
            try
            {
                ApplyVariablesToUi();
                _graphView.DeserializeGraph(safeGraphData, frameGraph);
            }
            finally
            {
                _graphView.SetRestoring(false);
                _isRestoringSnapshot = false;
            }

            UpdateSyncModePopup();
            UpdateLockstepRiskHint();
        }

        private void ApplyVariablesToUi()
        {
            _blackboardPanel.SetVariables(_variables);
            _graphView.SetVariables(_variables);
        }

        private bool IsEditingTextInput()
        {
            VisualElement focusedElement = rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
            if (focusedElement == null)
                return false;

            return focusedElement is TextField ||
                   focusedElement is IntegerField ||
                   focusedElement is FloatField ||
                   focusedElement.GetFirstAncestorOfType<TextField>() != null ||
                   focusedElement.GetFirstAncestorOfType<IntegerField>() != null ||
                   focusedElement.GetFirstAncestorOfType<FloatField>() != null;
        }

        private static List<SkillVariableDef> CloneVariables(IReadOnlyList<SkillVariableDef> variables)
        {
            List<SkillVariableDef> clonedVariables = new List<SkillVariableDef>();
            if (variables == null)
                return clonedVariables;

            foreach (SkillVariableDef variable in variables.Where(variable => variable != null))
            {
                clonedVariables.Add(new SkillVariableDef
                {
                    name = variable.name ?? string.Empty,
                    type = variable.type,
                    defaultValue = variable.defaultValue ?? string.Empty
                });
            }

            return clonedVariables;
        }

        private void UpdateSyncModePopup()
        {
            if (_syncModePopup == null)
                return;

            string syncMode = SyncModeOptions[ResolveSyncModeIndex(_currentSyncMode)];
            _syncModePopup.SetValueWithoutNotify(syncMode);
        }

        private void UpdateLockstepRiskHint()
        {
            if (_lockstepRiskHelpBox == null || _graphView == null || _blackboardPanel == null)
                return;

            if (!string.Equals(_currentSyncMode, RuntimeSyncModes.Lockstep, StringComparison.Ordinal))
            {
                _lockstepRiskHelpBox.style.display = DisplayStyle.None;
                _lockstepRiskHelpBox.text = string.Empty;
                return;
            }

            SkillGraphData graphData = BuildGraphData(GetDefaultGraphName());
            IReadOnlyList<string> riskMessages = SkillGraphExporter.CollectLockstepRiskMessages(graphData);
            if (riskMessages.Count == 0)
            {
                _lockstepRiskHelpBox.messageType = HelpBoxMessageType.Info;
                _lockstepRiskHelpBox.text = "Lockstep static checks passed.";
                _lockstepRiskHelpBox.style.display = DisplayStyle.Flex;
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Lockstep risks:");
            foreach (string message in riskMessages)
                builder.AppendLine($"- {message}");

            _lockstepRiskHelpBox.messageType = HelpBoxMessageType.Warning;
            _lockstepRiskHelpBox.text = builder.ToString().TrimEnd();
            _lockstepRiskHelpBox.style.display = DisplayStyle.Flex;
        }

        private static int ResolveSyncModeIndex(string syncMode)
        {
            string normalized = NormalizeSyncMode(syncMode);
            int index = SyncModeOptions.IndexOf(normalized);
            return index < 0 ? 0 : index;
        }

        private static string NormalizeSyncMode(string syncMode)
        {
            if (string.Equals(syncMode, RuntimeSyncModes.Lockstep, StringComparison.OrdinalIgnoreCase))
                return RuntimeSyncModes.Lockstep;

            return RuntimeSyncModes.LocalOnly;
        }
    }
}
