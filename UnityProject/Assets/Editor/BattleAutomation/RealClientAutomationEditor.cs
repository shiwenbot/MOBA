using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ParrelSync;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RealClientAutomationEditor
{
    private const string SessionPrefix = "BattleAutomation.Editor.";
    private const string ActiveKey = SessionPrefix + "Active";
    private const string ReportPathKey = SessionPrefix + "ReportPath";
    private const string ScenarioKey = SessionPrefix + "Scenario";
    private const string ClientIdKey = SessionPrefix + "ClientId";
    private const string TimeoutSecondsKey = SessionPrefix + "TimeoutSeconds";
    private const string StartedAtTicksKey = SessionPrefix + "StartedAtTicks";
    private const string ExitCodeKey = SessionPrefix + "ExitCode";
    private const string ReportDetectedTicksKey = SessionPrefix + "ReportDetectedTicks";
    private const string LingerSecondsKey = SessionPrefix + "LingerSeconds";
    private const string ShouldExitKey = SessionPrefix + "ShouldExit";
    private const string ScenePathKey = SessionPrefix + "ScenePath";

    static RealClientAutomationEditor()
    {
        EditorApplication.update -= Pump;
        EditorApplication.update += Pump;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static void EnsureParrelSyncClone()
    {
        try
        {
            Dictionary<string, string> args = CommandLineReader.GetCustomArguments();
            string clonePathFile = RequireArgument(args, "clonePathFile");
            string cloneProjectPath = ResolveCloneProjectPath();
            Directory.CreateDirectory(Path.GetDirectoryName(clonePathFile) ?? ".");
            File.WriteAllText(clonePathFile, cloneProjectPath, new UTF8Encoding(false));
            Debug.Log($"[Automation][Editor] Clone ready: {cloneProjectPath}");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void RunAutomationClient()
    {
        try
        {
            Dictionary<string, string> args = CommandLineReader.GetCustomArguments();
            string reportPath = RequireArgument(args, "reportPath");
            string scenePath = GetArgument(args, "scenePath", "Assets/Scenes/main.unity");
            string scenario = GetArgument(args, "scenario", "two-client-join");
            string clientId = GetArgument(args, "clientId", "client");
            int timeoutSeconds = ParseInt(args, "timeoutSeconds", 120);
            int lingerSeconds = ParseInt(args, "lingerSeconds", 30);

            if (File.Exists(reportPath))
            {
                File.Delete(reportPath);
            }

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(ShouldExitKey, false);
            SessionState.SetInt(ExitCodeKey, 1);
            SessionState.SetString(ReportPathKey, reportPath);
            SessionState.SetString(ScenePathKey, scenePath);
            SessionState.SetString(ScenarioKey, scenario);
            SessionState.SetString(ClientIdKey, clientId);
            SessionState.SetInt(TimeoutSecondsKey, timeoutSeconds);
            SessionState.SetString(StartedAtTicksKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetInt(LingerSecondsKey, lingerSeconds);

            if (!File.Exists(scenePath))
            {
                throw new FileNotFoundException($"Scene not found: {scenePath}", scenePath);
            }

            EditorSceneManager.OpenScene(scenePath);
            Debug.Log(
                $"[Automation][Editor] Entering PlayMode. client={clientId} scenario={scenario} report={reportPath}");
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Pump()
    {
        if (!SessionState.GetBool(ActiveKey, false))
        {
            return;
        }

        string reportPath = SessionState.GetString(ReportPathKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(reportPath) && File.Exists(reportPath))
        {
            long detectedTicks = ParseLong(SessionState.GetString(ReportDetectedTicksKey, string.Empty), 0);
            if (detectedTicks == 0)
            {
                SessionState.SetString(ReportDetectedTicksKey, DateTime.UtcNow.Ticks.ToString());
                detectedTicks = DateTime.UtcNow.Ticks;
            }

            int lingerSeconds = SessionState.GetInt(LingerSecondsKey, 30);
            double lingeredSeconds = new TimeSpan(DateTime.UtcNow.Ticks - detectedTicks).TotalSeconds;
            if (lingeredSeconds < lingerSeconds)
            {
                return;
            }

            bool passed = TryReadPassed(reportPath, out bool parsedValue) && parsedValue;
            RequestExit(passed ? 0 : 1);
            return;
        }

        long startedAtTicks = ParseLong(SessionState.GetString(StartedAtTicksKey, string.Empty), DateTime.UtcNow.Ticks);
        int timeoutSeconds = SessionState.GetInt(TimeoutSecondsKey, 120);
        double elapsedSeconds = new TimeSpan(DateTime.UtcNow.Ticks - startedAtTicks).TotalSeconds;
        if (elapsedSeconds <= timeoutSeconds)
        {
            return;
        }

        WriteFailureReport(reportPath, "timeout");
        RequestExit(1);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
        {
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (!SessionState.GetBool(ShouldExitKey, false))
            {
                string reportPath = SessionState.GetString(ReportPathKey, string.Empty);
                WriteFailureReport(reportPath, "playmode-exited-without-report");
                SessionState.SetInt(ExitCodeKey, 1);
            }

            ExitNow();
        }
    }

    private static void RequestExit(int exitCode)
    {
        if (SessionState.GetBool(ShouldExitKey, false))
        {
            return;
        }

        SessionState.SetBool(ShouldExitKey, true);
        SessionState.SetInt(ExitCodeKey, exitCode);
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }

        ExitNow();
    }

    private static void ExitNow()
    {
        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        ClearSession();
        EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }

    private static void ClearSession()
    {
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(ShouldExitKey);
        SessionState.EraseInt(ExitCodeKey);
        SessionState.EraseInt(TimeoutSecondsKey);
        SessionState.EraseString(ReportPathKey);
        SessionState.EraseString(ScenePathKey);
        SessionState.EraseString(ScenarioKey);
        SessionState.EraseString(ClientIdKey);
        SessionState.EraseString(StartedAtTicksKey);
        SessionState.EraseString(ReportDetectedTicksKey);
        SessionState.EraseInt(LingerSecondsKey);
    }

    private static bool TryReadPassed(string reportPath, out bool passed)
    {
        try
        {
            string json = File.ReadAllText(reportPath, Encoding.UTF8);
            PassedState payload = JsonUtility.FromJson<PassedState>(json);
            passed = payload != null && payload.passed;
            return true;
        }
        catch
        {
            passed = false;
            return false;
        }
    }

    private static void WriteFailureReport(string reportPath, string reason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? ".");
            EditorFailureReport report = new EditorFailureReport
            {
                passed = false,
                reason = reason,
                scenario = SessionState.GetString(ScenarioKey, string.Empty),
                clientId = SessionState.GetString(ClientIdKey, string.Empty)
            };
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static string ResolveCloneProjectPath()
    {
        List<string> clonePaths = ClonesManager.GetCloneProjectsPath();
        if (clonePaths.Count > 0)
        {
            return clonePaths[0];
        }

        Project cloneProject = ClonesManager.CreateCloneFromCurrent();
        if (cloneProject == null || string.IsNullOrWhiteSpace(cloneProject.projectPath))
        {
            throw new InvalidOperationException("Failed to create ParrelSync clone project.");
        }

        return cloneProject.projectPath;
    }

    private static string RequireArgument(Dictionary<string, string> args, string key)
    {
        string value = GetArgument(args, key, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required custom argument: {key}");
        }

        return value;
    }

    private static string GetArgument(Dictionary<string, string> args, string key, string defaultValue)
    {
        return args.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)
            ? Uri.UnescapeDataString(value)
            : defaultValue;
    }

    private static int ParseInt(Dictionary<string, string> args, string key, int defaultValue)
    {
        return args.TryGetValue(key, out string value) && int.TryParse(value, out int parsedValue)
            ? parsedValue
            : defaultValue;
    }

    private static long ParseLong(string value, long defaultValue)
    {
        return long.TryParse(value, out long parsedValue) ? parsedValue : defaultValue;
    }

    [Serializable]
    private sealed class PassedState
    {
        public bool passed;
    }

    [Serializable]
    private sealed class EditorFailureReport
    {
        public bool passed;
        public string reason;
        public string scenario;
        public string clientId;
    }
}
