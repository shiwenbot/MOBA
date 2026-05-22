using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    internal enum BattleAutomationBridgeMode
    {
        Builtin,
        Puerts
    }

    public interface IBattleAutomationInputSource
    {
        bool TryGetInput(uint frameIndex, out float dx, out float dy);
    }

    internal interface IBattleAutomationBridge : IBattleAutomationInputSource, IDisposable
    {
        string BridgeName { get; }
        void Initialize(BattleClientController controller, BattleAutomationConfig config, Action<string> eventSink);
        BattleAutomationEvaluation Evaluate(BattleAutomationClientSnapshot snapshot);
    }

    internal readonly struct BattleAutomationEvaluation
    {
        public BattleAutomationEvaluation(bool completed, bool passed, string reason)
        {
            Completed = completed;
            Passed = passed;
            Reason = reason ?? string.Empty;
        }

        public bool Completed { get; }
        public bool Passed { get; }
        public string Reason { get; }
    }

    internal static class BattleAutomationCommandLine
    {
        private const string CustomArgsPrefix = "-CustomArgs:";
        private static Dictionary<string, string> s_cachedArguments;

        public static Dictionary<string, string> GetArguments()
        {
            if (s_cachedArguments != null)
            {
                return s_cachedArguments;
            }

            s_cachedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith(CustomArgsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    ParseCustomArgs(arg.Substring(CustomArgsPrefix.Length), s_cachedArguments);
                    continue;
                }

                if (!arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int separatorIndex = arg.IndexOf('=');
                if (separatorIndex <= 2 || separatorIndex >= arg.Length - 1)
                {
                    continue;
                }

                string key = arg.Substring(2, separatorIndex - 2);
                string value = arg.Substring(separatorIndex + 1);
                s_cachedArguments[key] = value;
            }

            return s_cachedArguments;
        }

        private static void ParseCustomArgs(string rawCustomArgs, Dictionary<string, string> target)
        {
            if (string.IsNullOrWhiteSpace(rawCustomArgs))
            {
                return;
            }

            string[] segments = rawCustomArgs.Split(';', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i].Trim();
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                int separatorIndex = segment.IndexOf('=');
                if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
                {
                    continue;
                }

                string key = segment.Substring(0, separatorIndex).Trim();
                string value = segment.Substring(separatorIndex + 1).Trim();
                target[key] = value;
            }
        }
    }

    internal sealed class BattleAutomationConfig
    {
        private static BattleAutomationConfig s_cachedCurrent;

        private BattleAutomationConfig(
            bool enabled,
            string scenario,
            string clientId,
            string reportPath,
            string eventLogPath,
            string inputScriptPath,
            string controllerScriptPath,
            BattleAutomationBridgeMode bridgeMode,
            string battleServerAddress,
            int battleServerPort,
            int timeoutSeconds,
            int minimumPlayerCount,
            int movementFrames,
            int settleFrames,
            int disconnectFrame,
            float movementDistanceThreshold,
            bool autoOpenBattleUi,
            bool autoCloseAfterFinish)
        {
            Enabled = enabled;
            Scenario = scenario;
            ClientId = clientId;
            ReportPath = reportPath;
            EventLogPath = eventLogPath;
            InputScriptPath = inputScriptPath;
            ControllerScriptPath = controllerScriptPath;
            BridgeMode = bridgeMode;
            BattleServerAddress = battleServerAddress;
            BattleServerPort = battleServerPort;
            TimeoutSeconds = timeoutSeconds;
            MinimumPlayerCount = minimumPlayerCount;
            MovementFrames = movementFrames;
            SettleFrames = settleFrames;
            DisconnectFrame = disconnectFrame;
            MovementDistanceThreshold = movementDistanceThreshold;
            AutoOpenBattleUi = autoOpenBattleUi;
            AutoCloseAfterFinish = autoCloseAfterFinish;
        }

        public bool Enabled { get; }
        public string Scenario { get; }
        public string ClientId { get; }
        public string ReportPath { get; }
        public string EventLogPath { get; }
        public string InputScriptPath { get; }
        public string ControllerScriptPath { get; }
        public BattleAutomationBridgeMode BridgeMode { get; }
        public string BattleServerAddress { get; }
        public int BattleServerPort { get; }
        public int TimeoutSeconds { get; }
        public int MinimumPlayerCount { get; }
        public int MovementFrames { get; }
        public int SettleFrames { get; }
        public int DisconnectFrame { get; }
        public float MovementDistanceThreshold { get; }
        public bool AutoOpenBattleUi { get; }
        public bool AutoCloseAfterFinish { get; }

        public static BattleAutomationConfig Current => s_cachedCurrent ??= CreateCurrent();

        private static BattleAutomationConfig CreateCurrent()
        {
            Dictionary<string, string> args = BattleAutomationCommandLine.GetArguments();
            bool enabled = GetBool(args, "battleAutomation") || GetBool(args, "automation");
            string clientId = GetString(args, "clientId", "client");
            string scenario = GetString(args, "scenario", "two-client-join");
            string reportPath = GetString(
                args,
                "reportPath",
                Path.Combine(Application.persistentDataPath, $"battle-automation-{clientId}.json"));
            string eventLogPath = GetString(
                args,
                "eventLogPath",
                Path.Combine(Application.persistentDataPath, $"battle-automation-{clientId}.log"));
            string inputScriptPath = GetString(args, "inputScriptPath", string.Empty);
            string controllerScriptPath = GetString(args, "controllerScriptPath", string.Empty);
            string bridgeName = GetString(args, "bridge", "builtin");
            BattleAutomationBridgeMode bridgeMode = bridgeName.Equals("puerts", StringComparison.OrdinalIgnoreCase)
                ? BattleAutomationBridgeMode.Puerts
                : BattleAutomationBridgeMode.Builtin;

            return new BattleAutomationConfig(
                enabled,
                scenario,
                clientId,
                NormalizeAbsolutePath(reportPath),
                NormalizeAbsolutePath(eventLogPath),
                NormalizeAbsolutePath(inputScriptPath),
                NormalizeAbsolutePath(controllerScriptPath),
                bridgeMode,
                GetString(args, "battleServerAddress", "127.0.0.1"),
                GetInt(args, "battleServerPort", 20101),
                GetInt(args, "timeoutSeconds", 120),
                GetInt(args, "minimumPlayerCount", 2),
                GetInt(args, "movementFrames", 90),
                GetInt(args, "settleFrames", 30),
                GetInt(args, "disconnectFrame", 60),
                GetFloat(args, "movementDistanceThreshold", 1.0f),
                GetBool(args, "autoOpenBattleUi", true),
                GetBool(args, "autoCloseAfterFinish", true));
        }

        private static string NormalizeAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(path);
        }

        private static string GetString(Dictionary<string, string> args, string key, string defaultValue)
        {
            return args.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value)
                ? Uri.UnescapeDataString(value)
                : defaultValue;
        }

        private static bool GetBool(Dictionary<string, string> args, string key, bool defaultValue = false)
        {
            if (!args.TryGetValue(key, out string value))
            {
                return defaultValue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            value = value.Trim();
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetInt(Dictionary<string, string> args, string key, int defaultValue)
        {
            return args.TryGetValue(key, out string value) &&
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
                ? parsedValue
                : defaultValue;
        }

        private static float GetFloat(Dictionary<string, string> args, string key, float defaultValue)
        {
            return args.TryGetValue(key, out string value) &&
                   float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue)
                ? parsedValue
                : defaultValue;
        }
    }

    public sealed class BattleAutomationController : MonoBehaviour
    {
        private const int MaxCapturedLogs = 200;
        private readonly List<string> _eventLines = new List<string>(128);
        private readonly List<string> _capturedLogs = new List<string>(MaxCapturedLogs);

        private BattleClientController _controller;
        private BattleAutomationConfig _config;
        private IBattleAutomationBridge _bridge;
        private bool _started;
        private bool _finished;
        private float _startedAtRealtime;

        public void Initialize(BattleClientController controller)
        {
            if (_started)
            {
                return;
            }

            _controller = controller;
            _config = BattleAutomationConfig.Current;
            if (_controller == null || _config == null || !_config.Enabled)
            {
                return;
            }

            _bridge = CreateBridge(_config.BridgeMode);
            _bridge.Initialize(_controller, _config, RecordEvent);
            _controller.SetAutomationInputSource(_bridge);
            _startedAtRealtime = Time.realtimeSinceStartup;
            _started = true;

            Application.logMessageReceived += OnUnityLog;
            RecordEvent(
                $"[Automation] Started client={_config.ClientId} scenario={_config.Scenario} bridge={_bridge.BridgeName}");
        }

        private void Update()
        {
            if (!_started || _finished || _controller == null)
            {
                return;
            }

            BattleAutomationClientSnapshot snapshot = _controller.CaptureAutomationSnapshot(_config.ClientId);
            if (!snapshot.joined && !string.IsNullOrWhiteSpace(snapshot.joinFailureReason))
            {
                Finish(false, $"join-failed:{snapshot.joinFailureReason}", snapshot);
                return;
            }

            if (Time.realtimeSinceStartup - _startedAtRealtime > _config.TimeoutSeconds)
            {
                Finish(false, $"timeout-after-{_config.TimeoutSeconds}s", snapshot);
                return;
            }

            BattleAutomationEvaluation evaluation = _bridge.Evaluate(snapshot);
            if (evaluation.Completed)
            {
                Finish(evaluation.Passed, evaluation.Reason, snapshot);
            }
        }

        private void OnDestroy()
        {
            if (_started && !_finished && _controller != null)
            {
                Finish(false, "automation-controller-destroyed", _controller.CaptureAutomationSnapshot(_config?.ClientId ?? "client"));
            }

            Application.logMessageReceived -= OnUnityLog;
            _controller?.SetAutomationInputSource(null);
            _bridge?.Dispose();
            _bridge = null;
            _controller = null;
        }

        private void Finish(bool passed, string reason, BattleAutomationClientSnapshot snapshot)
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Application.logMessageReceived -= OnUnityLog;
            _controller?.SetAutomationInputSource(null);

            RecordEvent($"[Automation] Result={(passed ? "PASS" : "FAIL")} reason={reason}");
            BattleAutomationReport report = new BattleAutomationReport
            {
                passed = passed,
                reason = reason ?? string.Empty,
                scenario = _config?.Scenario ?? string.Empty,
                clientId = _config?.ClientId ?? string.Empty,
                bridge = _bridge?.BridgeName ?? "none",
                startedAtUtc = DateTime.UtcNow.AddSeconds(-(Time.realtimeSinceStartup - _startedAtRealtime))
                    .ToString("O", CultureInfo.InvariantCulture),
                finishedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                elapsedSeconds = Time.realtimeSinceStartup - _startedAtRealtime,
                events = _eventLines.ToArray(),
                capturedLogs = _capturedLogs.ToArray(),
                snapshot = snapshot
            };
            WriteOutputs(report);

            if (_config != null && _config.AutoCloseAfterFinish && !Application.isEditor)
            {
                Application.Quit();
            }
        }

        private void WriteOutputs(BattleAutomationReport report)
        {
            if (_config == null)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_config.ReportPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_config.ReportPath) ?? ".");
                    File.WriteAllText(_config.ReportPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                }

                if (!string.IsNullOrWhiteSpace(_config.EventLogPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_config.EventLogPath) ?? ".");
                    File.WriteAllLines(_config.EventLogPath, _eventLines, new UTF8Encoding(false));
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[Automation] Failed to write outputs: {exception}");
            }
        }

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (_capturedLogs.Count >= MaxCapturedLogs)
            {
                return;
            }

            bool shouldCapture = type == LogType.Warning ||
                                 type == LogType.Error ||
                                 type == LogType.Exception ||
                                 condition.IndexOf("[Battle]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 condition.IndexOf("[Consistency]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 condition.IndexOf("[Rollback]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 condition.IndexOf("[GameClient]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 condition.IndexOf("[PredictSelfTest]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 condition.IndexOf("[Automation]", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!shouldCapture)
            {
                return;
            }

            _capturedLogs.Add($"[{type}] {condition}");
        }

        private void RecordEvent(string message)
        {
            _eventLines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            Log.Info(message);
        }

        private static IBattleAutomationBridge CreateBridge(BattleAutomationBridgeMode bridgeMode)
        {
            return bridgeMode switch
            {
                BattleAutomationBridgeMode.Puerts => new PuertsBattleAutomationBridge(),
                _ => new BuiltinBattleAutomationBridge()
            };
        }
    }

    internal sealed class BuiltinBattleAutomationBridge : IBattleAutomationBridge
    {
        private BattleAutomationScenarioPlan _plan;
        private Action<string> _eventSink;
        private uint _joinedFrame;
        private bool _hasJoinedFrame;
        private bool _hasStartPosition;
        private float _startX;
        private float _startY;
        private bool _observedTargetPlayerCount;
        private bool _observedPlayerDrop;

        public string BridgeName => "builtin";

        public void Initialize(BattleClientController controller, BattleAutomationConfig config, Action<string> eventSink)
        {
            _eventSink = eventSink;
            _plan = BattleAutomationScenarioPlan.Create(config);
            _eventSink?.Invoke(
                $"[Automation] Builtin bridge loaded plan={_plan.name} inputSegments={_plan.inputSegments.Length}");
        }

        public bool TryGetInput(uint frameIndex, out float dx, out float dy)
        {
            if (!_hasJoinedFrame)
            {
                dx = 0.0f;
                dy = 0.0f;
                return false;
            }

            uint relativeFrame = frameIndex - _joinedFrame;
            return _plan.TryGetInput(relativeFrame, out dx, out dy);
        }

        public BattleAutomationEvaluation Evaluate(BattleAutomationClientSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return default;
            }

            if (snapshot.joined && !_hasJoinedFrame)
            {
                _joinedFrame = (uint)Math.Max(snapshot.localFrame, 0);
                _hasJoinedFrame = true;
                _eventSink?.Invoke($"[Automation] Joined at localFrame={snapshot.localFrame} selfPlayer={snapshot.selfPlayerId}");
            }

            if (!_hasStartPosition && snapshot.TryGetSelfPlayer(out BattleAutomationPlayerSnapshot selfPlayer))
            {
                _hasStartPosition = true;
                _startX = selfPlayer.x;
                _startY = selfPlayer.y;
            }

            if (snapshot.activePlayerCount >= _plan.minimumPlayerCount)
            {
                _observedTargetPlayerCount = true;
            }

            if (_observedTargetPlayerCount && snapshot.activePlayerCount < _plan.minimumPlayerCount)
            {
                _observedPlayerDrop = true;
            }

            if (!_hasJoinedFrame)
            {
                return default;
            }

            int elapsedFrames = snapshot.localFrame - (int)_joinedFrame;
            switch (_plan.kind)
            {
                case BattleAutomationScenarioKind.JoinOnly:
                    if (_observedTargetPlayerCount &&
                        snapshot.snapshotMessageCount > 0 &&
                        elapsedFrames >= _plan.completionFrame)
                    {
                        return new BattleAutomationEvaluation(true, true, "two-client-join-complete");
                    }

                    break;

                case BattleAutomationScenarioKind.Movement:
                    if (_hasStartPosition && snapshot.TryGetSelfPlayer(out BattleAutomationPlayerSnapshot movingSelf))
                    {
                        float deltaX = movingSelf.x - _startX;
                        float deltaY = movingSelf.y - _startY;
                        float distance = Mathf.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                        if (_observedTargetPlayerCount &&
                            elapsedFrames >= _plan.completionFrame &&
                            distance >= _plan.movementDistanceThreshold)
                        {
                            return new BattleAutomationEvaluation(
                                true,
                                true,
                                $"movement-complete distance={distance:F3}");
                        }

                        if (elapsedFrames >= _plan.completionFrame + 30 &&
                            distance < _plan.movementDistanceThreshold)
                        {
                            return new BattleAutomationEvaluation(
                                true,
                                false,
                                $"movement-too-small distance={distance:F3}");
                        }
                    }

                    break;

                case BattleAutomationScenarioKind.DisconnectActor:
                    if (elapsedFrames >= _plan.disconnectFrame)
                    {
                        return new BattleAutomationEvaluation(true, true, "disconnect-actor-finished");
                    }

                    break;

                case BattleAutomationScenarioKind.DisconnectObserver:
                    if (_observedPlayerDrop && elapsedFrames >= _plan.disconnectFrame)
                    {
                        return new BattleAutomationEvaluation(true, true, "disconnect-observer-finished");
                    }

                    if (elapsedFrames >= _plan.disconnectFrame + 480)
                    {
                        return new BattleAutomationEvaluation(true, false, "disconnect-observer-timeout");
                    }

                    break;

                case BattleAutomationScenarioKind.Scripted:
                    if (_observedTargetPlayerCount && elapsedFrames >= _plan.completionFrame)
                    {
                        return new BattleAutomationEvaluation(true, true, "scripted-plan-finished");
                    }

                    break;
            }

            return default;
        }

        public void Dispose()
        {
        }
    }

    internal sealed class PuertsBattleAutomationBridge : IBattleAutomationBridge
    {
        private readonly BuiltinBattleAutomationBridge _fallback = new BuiltinBattleAutomationBridge();
        private object _jsEnv;
        private object _controllerObject;
        private Type _scriptObjectType;
        private Func<string, string> _evaluate;
        private Func<string, string> _tryGetInput;
        private Action<string> _initialize;
        private Action _dispose;
        private Action<string> _eventSink;

        public string BridgeName => _jsEnv != null ? "puerts-runtime" : "puerts-fallback";

        public void Initialize(BattleClientController controller, BattleAutomationConfig config, Action<string> eventSink)
        {
            _eventSink = eventSink;
            if (TryInitializeRuntime(config))
            {
                _eventSink?.Invoke("[Automation] Puerts runtime bridge initialized.");
                return;
            }

            _eventSink?.Invoke(
                "[Automation] Requested puerts bridge, but runtime activation failed. Falling back to builtin bridge.");
            _fallback.Initialize(controller, config, eventSink);
        }

        public bool TryGetInput(uint frameIndex, out float dx, out float dy)
        {
            if (_jsEnv != null && _tryGetInput != null)
            {
                try
                {
                    string payload = "{\"frameIndex\":" + frameIndex.ToString(CultureInfo.InvariantCulture) + "}";
                    string result = _tryGetInput(payload);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        BattleAutomationInputResult parsed = JsonUtility.FromJson<BattleAutomationInputResult>(result);
                        if (parsed != null && parsed.hasInput)
                        {
                            dx = parsed.dx;
                            dy = parsed.dy;
                            return true;
                        }
                    }
                }
                catch (Exception exception)
                {
                    _eventSink?.Invoke("[Automation] Puerts tryGetInput failed: " + exception.Message);
                }

                dx = 0.0f;
                dy = 0.0f;
                return false;
            }

            return _fallback.TryGetInput(frameIndex, out dx, out dy);
        }

        public BattleAutomationEvaluation Evaluate(BattleAutomationClientSnapshot snapshot)
        {
            if (_jsEnv != null && _evaluate != null)
            {
                try
                {
                    string payload = JsonUtility.ToJson(snapshot);
                    string result = _evaluate(payload);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        BattleAutomationEvaluationPayload parsed = JsonUtility.FromJson<BattleAutomationEvaluationPayload>(result);
                        if (parsed != null)
                        {
                            return new BattleAutomationEvaluation(parsed.completed, parsed.passed, parsed.reason);
                        }
                    }
                }
                catch (Exception exception)
                {
                    _eventSink?.Invoke("[Automation] Puerts evaluate failed: " + exception.Message);
                    return new BattleAutomationEvaluation(true, false, "puerts-evaluate-failed:" + exception.Message);
                }
            }

            return _fallback.Evaluate(snapshot);
        }

        public void Dispose()
        {
            if (_dispose != null)
            {
                try
                {
                    _dispose();
                }
                catch
                {
                }
            }

            _fallback.Dispose();
        }

        private bool TryInitializeRuntime(BattleAutomationConfig config)
        {
            try
            {
                Type jsEnvType = FindType("Puerts.JsEnv");
                Type defaultLoaderType = FindType("Puerts.DefaultLoader");
                _scriptObjectType = FindType("Puerts.ScriptObject");
                if (jsEnvType == null || defaultLoaderType == null || _scriptObjectType == null)
                {
                    return false;
                }

                object loader = Activator.CreateInstance(defaultLoaderType);
                _jsEnv = Activator.CreateInstance(jsEnvType, loader, -1);

                InvokeUsing(jsEnvType, _jsEnv, "UsingAction", typeof(string));
                InvokeUsing(jsEnvType, _jsEnv, "UsingFunc", typeof(string), typeof(string));

                string controllerScriptPath = ResolveControllerScriptPath(config);
                string scriptContent = File.ReadAllText(controllerScriptPath, Encoding.UTF8);
                string wrappedScript =
                    "(function(){ const module = { exports: {} }; const exports = module.exports; " +
                    scriptContent +
                    "; return module.exports; })()";
                MethodInfo evalGenericDefinition = null;
                foreach (MethodInfo candidate in jsEnvType.GetMethods())
                {
                    if (candidate.Name != "Eval" || !candidate.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string) &&
                        parameters[1].ParameterType == typeof(string))
                    {
                        evalGenericDefinition = candidate;
                        break;
                    }
                }

                if (evalGenericDefinition == null)
                {
                    return false;
                }

                _controllerObject = evalGenericDefinition.MakeGenericMethod(_scriptObjectType)
                    .Invoke(_jsEnv, new object[] { wrappedScript, Path.GetFileName(controllerScriptPath) });
                if (_controllerObject == null)
                {
                    return false;
                }

                _initialize = GetDelegate<Action<string>>("initialize");
                _tryGetInput = GetDelegate<Func<string, string>>("tryGetInput");
                _evaluate = GetDelegate<Func<string, string>>("evaluate");
                _dispose = GetDelegate<Action>("dispose");

                _initialize?.Invoke(JsonUtility.ToJson(new BattleAutomationControllerConfig
                {
                    clientId = config.ClientId,
                    scenario = config.Scenario,
                    battleServerAddress = config.BattleServerAddress,
                    battleServerPort = config.BattleServerPort,
                    timeoutSeconds = config.TimeoutSeconds
                }));
                return _tryGetInput != null && _evaluate != null;
            }
            catch (Exception exception)
            {
                _eventSink?.Invoke("[Automation] Puerts runtime init failed: " + exception.Message);
                _jsEnv = null;
                _controllerObject = null;
                _scriptObjectType = null;
                return false;
            }
        }

        private string ResolveControllerScriptPath(BattleAutomationConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.ControllerScriptPath) && File.Exists(config.ControllerScriptPath))
            {
                return config.ControllerScriptPath;
            }

            return Path.Combine(Application.streamingAssetsPath, "BattleAutomation", "Puerts", "sample-controller.js.txt");
        }

        private T GetDelegate<T>(string key) where T : class
        {
            // 使用更精确的查找避免 "Ambiguous match found"
            foreach (MethodInfo candidate in _scriptObjectType.GetMethods())
            {
                if (candidate.Name != "Get" || !candidate.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }

                object value = candidate.MakeGenericMethod(typeof(T)).Invoke(_controllerObject, new object[] { key });
                return value as T;
            }

            return null;
        }

        private static void InvokeUsing(Type jsEnvType, object instance, string methodName, params Type[] genericTypes)
        {
            foreach (MethodInfo candidate in jsEnvType.GetMethods())
            {
                if (!candidate.IsGenericMethodDefinition || candidate.Name != methodName)
                {
                    continue;
                }

                if (candidate.GetGenericArguments().Length != genericTypes.Length)
                {
                    continue;
                }

                candidate.MakeGenericMethod(genericTypes).Invoke(instance, Array.Empty<object>());
                return;
            }
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(fullName, false);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    internal enum BattleAutomationScenarioKind
    {
        JoinOnly,
        Movement,
        DisconnectActor,
        DisconnectObserver,
        Scripted
    }

    internal sealed class BattleAutomationScenarioPlan
    {
        public string name;
        public BattleAutomationScenarioKind kind;
        public int minimumPlayerCount;
        public int completionFrame;
        public int disconnectFrame;
        public float movementDistanceThreshold;
        public BattleAutomationInputSegment[] inputSegments;

        public static BattleAutomationScenarioPlan Create(BattleAutomationConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.InputScriptPath) && File.Exists(config.InputScriptPath))
            {
                BattleAutomationInputSegment[] scriptedSegments = LoadInputScript(config.InputScriptPath);
                return new BattleAutomationScenarioPlan
                {
                    name = string.IsNullOrWhiteSpace(config.Scenario) ? "scripted-smoke" : config.Scenario,
                    kind = BattleAutomationScenarioKind.Scripted,
                    minimumPlayerCount = config.MinimumPlayerCount,
                    completionFrame = GetLastEndFrame(scriptedSegments) + config.SettleFrames,
                    disconnectFrame = config.DisconnectFrame,
                    movementDistanceThreshold = config.MovementDistanceThreshold,
                    inputSegments = scriptedSegments
                };
            }

            bool isClientB = IsClientB(config.ClientId);
            string scenario = (config.Scenario ?? string.Empty).Trim().ToLowerInvariant();
            switch (scenario)
            {
                case "two-client-basic-move":
                    return new BattleAutomationScenarioPlan
                    {
                        name = "two-client-basic-move",
                        kind = BattleAutomationScenarioKind.Movement,
                        minimumPlayerCount = config.MinimumPlayerCount,
                        completionFrame = config.MovementFrames,
                        disconnectFrame = config.DisconnectFrame,
                        movementDistanceThreshold = config.MovementDistanceThreshold,
                        inputSegments = isClientB
                            ? new[]
                            {
                                new BattleAutomationInputSegment(0, (uint)Math.Max(0, config.MovementFrames - 1), 0.0f, 1.0f)
                            }
                            : new[]
                            {
                                new BattleAutomationInputSegment(0, (uint)Math.Max(0, config.MovementFrames - 1), 1.0f, 0.0f)
                            }
                    };

                case "two-client-disconnect":
                    return new BattleAutomationScenarioPlan
                    {
                        name = "two-client-disconnect",
                        kind = isClientB
                            ? BattleAutomationScenarioKind.DisconnectActor
                            : BattleAutomationScenarioKind.DisconnectObserver,
                        minimumPlayerCount = config.MinimumPlayerCount,
                        completionFrame = config.MovementFrames,
                        disconnectFrame = config.DisconnectFrame,
                        movementDistanceThreshold = config.MovementDistanceThreshold,
                        inputSegments = isClientB
                            ? new[]
                            {
                                new BattleAutomationInputSegment(0, (uint)Math.Max(0, config.DisconnectFrame - 1), -1.0f, 0.0f)
                            }
                            : new[]
                            {
                                new BattleAutomationInputSegment(0, (uint)Math.Min(30, Math.Max(0, config.DisconnectFrame - 1)), 1.0f, 0.0f)
                            }
                    };

                default:
                    return new BattleAutomationScenarioPlan
                    {
                        name = "two-client-join",
                        kind = BattleAutomationScenarioKind.JoinOnly,
                        minimumPlayerCount = config.MinimumPlayerCount,
                        completionFrame = config.SettleFrames,
                        disconnectFrame = config.DisconnectFrame,
                        movementDistanceThreshold = config.MovementDistanceThreshold,
                        inputSegments = Array.Empty<BattleAutomationInputSegment>()
                    };
            }
        }

        public bool TryGetInput(uint relativeFrame, out float dx, out float dy)
        {
            for (int i = 0; i < inputSegments.Length; i++)
            {
                BattleAutomationInputSegment segment = inputSegments[i];
                if (segment.Contains(relativeFrame))
                {
                    dx = segment.dx;
                    dy = segment.dy;
                    return true;
                }
            }

            dx = 0.0f;
            dy = 0.0f;
            return false;
        }

        private static BattleAutomationInputSegment[] LoadInputScript(string scriptPath)
        {
            List<BattleAutomationInputSegment> segments = new List<BattleAutomationInputSegment>();
            string[] lines = File.ReadAllLines(scriptPath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 4)
                {
                    continue;
                }

                if (!uint.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint startFrame) ||
                    !uint.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint endFrame) ||
                    !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dx) ||
                    !float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dy))
                {
                    continue;
                }

                segments.Add(new BattleAutomationInputSegment(startFrame, endFrame, dx, dy));
            }

            return segments.ToArray();
        }

        private static int GetLastEndFrame(BattleAutomationInputSegment[] segments)
        {
            int endFrame = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                endFrame = Math.Max(endFrame, (int)segments[i].endFrame);
            }

            return endFrame;
        }

        private static bool IsClientB(string clientId)
        {
            string normalized = clientId?.Trim().ToLowerInvariant() ?? string.Empty;
            return normalized.IndexOf("client-b", StringComparison.Ordinal) >= 0 ||
                   normalized.IndexOf("clone", StringComparison.Ordinal) >= 0 ||
                   normalized.EndsWith("b", StringComparison.Ordinal) ||
                   normalized.EndsWith("2", StringComparison.Ordinal);
        }
    }

    internal readonly struct BattleAutomationInputSegment
    {
        public BattleAutomationInputSegment(uint startFrame, uint endFrame, float dx, float dy)
        {
            this.startFrame = startFrame;
            this.endFrame = endFrame;
            this.dx = dx;
            this.dy = dy;
        }

        public readonly uint startFrame;
        public readonly uint endFrame;
        public readonly float dx;
        public readonly float dy;

        public bool Contains(uint frameIndex)
        {
            return frameIndex >= startFrame && frameIndex <= endFrame;
        }
    }

    [Serializable]
    public sealed class BattleAutomationReport
    {
        public bool passed;
        public string reason;
        public string scenario;
        public string clientId;
        public string bridge;
        public string startedAtUtc;
        public string finishedAtUtc;
        public float elapsedSeconds;
        public string[] events;
        public string[] capturedLogs;
        public BattleAutomationClientSnapshot snapshot;
    }

    [Serializable]
    public sealed class BattleAutomationClientSnapshot
    {
        public string clientId;
        public bool joined;
        public string joinFailureReason;
        public long selfPlayerId;
        public int localFrame;
        public int lastAppliedFrame;
        public int leadFrames;
        public int activePlayerCount;
        public int snapshotMessageCount;
        public int pongMessageCount;
        public int consistencyChecked;
        public int consistencyHits;
        public int consistencyMisses;
        public int consistencySkippedNoRecord;
        public int consistencySkippedEvicted;
        public int rollbackCount;
        public int lastRollbackReplayFrames;
        public float lastRollbackElapsedMs;
        public BattleAutomationPlayerSnapshot[] players;

        public bool TryGetSelfPlayer(out BattleAutomationPlayerSnapshot snapshot)
        {
            if (players != null)
            {
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i] != null && players[i].isSelf)
                    {
                        snapshot = players[i];
                        return true;
                    }
                }
            }

            snapshot = null;
            return false;
        }
    }

    [Serializable]
    public sealed class BattleAutomationPlayerSnapshot
    {
        public long playerId;
        public bool isSelf;
        public float x;
        public float y;
        public int health;
        public int maxHealth;
        public int mana;
        public int maxMana;
        public int attack;
    }

    [Serializable]
    internal sealed class BattleAutomationInputResult
    {
        public bool hasInput;
        public float dx;
        public float dy;
    }

    [Serializable]
    internal sealed class BattleAutomationEvaluationPayload
    {
        public bool completed;
        public bool passed;
        public string reason;
    }

    [Serializable]
    internal sealed class BattleAutomationControllerConfig
    {
        public string clientId;
        public string scenario;
        public string battleServerAddress;
        public int battleServerPort;
        public int timeoutSeconds;
    }
}
