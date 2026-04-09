using System;
using System.Collections.Generic;
using System.Globalization;
using Fantasy.Async;
using Newtonsoft.Json;

namespace GameShared.SkillGraph
{
    public enum SkillExecutionStatus : byte
    {
        Running = 0,
        Success = 1,
        Failure = 2,
        Cancelled = 3
    }

    public interface ISkillNodeHandler
    {
        FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context);
    }

    public sealed class SkillExecuteResult
    {
        public SkillExecutionStatus Status { get; private set; }

        public string NextPort { get; private set; } = string.Empty;

        public string Message { get; private set; } = string.Empty;

        public Exception? Exception { get; private set; }

        public static SkillExecuteResult Running(string message = "")
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Running,
                Message = message ?? string.Empty
            };
        }

        public static SkillExecuteResult Success(string nextPort, string message = "")
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Success,
                NextPort = nextPort ?? string.Empty,
                Message = message ?? string.Empty
            };
        }

        public static SkillExecuteResult Failure(string message, Exception? exception = null)
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Failure,
                Message = message ?? string.Empty,
                Exception = exception
            };
        }

        public static SkillExecuteResult Cancelled(string message = "")
        {
            return new SkillExecuteResult
            {
                Status = SkillExecutionStatus.Cancelled,
                Message = message ?? string.Empty
            };
        }
    }

    public sealed class SkillGraphRunResult
    {
        public SkillExecutionStatus Status { get; private set; }

        public int LastNodeId { get; private set; }

        public int ExecutedSteps { get; private set; }

        public string Message { get; private set; } = string.Empty;

        public Exception? Exception { get; private set; }

        public bool IsSuccess => Status == SkillExecutionStatus.Success;

        public bool IsFailure => Status == SkillExecutionStatus.Failure;

        public bool IsCancelled => Status == SkillExecutionStatus.Cancelled;

        public bool IsRunning => Status == SkillExecutionStatus.Running;

        public static SkillGraphRunResult Running(int lastNodeId, int executedSteps, string message = "")
        {
            return Create(SkillExecutionStatus.Running, lastNodeId, executedSteps, message, null);
        }

        public static SkillGraphRunResult Success(int lastNodeId, int executedSteps, string message = "")
        {
            return Create(SkillExecutionStatus.Success, lastNodeId, executedSteps, message, null);
        }

        public static SkillGraphRunResult Failure(int lastNodeId, int executedSteps, string message, Exception? exception = null)
        {
            return Create(SkillExecutionStatus.Failure, lastNodeId, executedSteps, message, exception);
        }

        public static SkillGraphRunResult Cancelled(int lastNodeId, int executedSteps, string message = "")
        {
            return Create(SkillExecutionStatus.Cancelled, lastNodeId, executedSteps, message, null);
        }

        private static SkillGraphRunResult Create(
            SkillExecutionStatus status,
            int lastNodeId,
            int executedSteps,
            string message,
            Exception? exception)
        {
            return new SkillGraphRunResult
            {
                Status = status,
                LastNodeId = lastNodeId,
                ExecutedSteps = executedSteps,
                Message = message ?? string.Empty,
                Exception = exception
            };
        }
    }

    public static class SkillExecutionEventTypes
    {
        public const string NodeEnter = "NodeEnter";
        public const string NodeExit = "NodeExit";
        public const string BlackboardSet = "BlackboardSet";
        public const string BranchTaken = "BranchTaken";
        public const string CommandIssued = "CommandIssued";
        public const string ExecutionEnd = "ExecutionEnd";
    }

    public sealed class SkillExecutionEvent
    {
        [JsonProperty("frameIndex")]
        public int FrameIndex { get; set; }

        [JsonProperty("nodeId")]
        public int NodeId { get; set; }

        [JsonProperty("eventType")]
        public string EventType { get; set; } = string.Empty;

        [JsonProperty("payload")]
        public string Payload { get; set; } = string.Empty;
    }

    public sealed class SkillExecutionEventDiff
    {
        public bool IsMatch { get; private set; }

        public int EventIndex { get; private set; } = -1;

        public int FrameIndex { get; private set; } = -1;

        public int NodeId { get; private set; } = -1;

        public string FieldName { get; private set; } = string.Empty;

        public string Message { get; private set; } = string.Empty;

        public SkillExecutionEvent? ExpectedEvent { get; private set; }

        public SkillExecutionEvent? ActualEvent { get; private set; }

        public static SkillExecutionEventDiff Match()
        {
            return new SkillExecutionEventDiff
            {
                IsMatch = true,
                Message = "Execution events are identical."
            };
        }

        public static SkillExecutionEventDiff Mismatch(
            int eventIndex,
            string fieldName,
            string message,
            SkillExecutionEvent? expectedEvent,
            SkillExecutionEvent? actualEvent)
        {
            SkillExecutionEvent? anchorEvent = expectedEvent ?? actualEvent;
            return new SkillExecutionEventDiff
            {
                IsMatch = false,
                EventIndex = eventIndex,
                FrameIndex = anchorEvent?.FrameIndex ?? -1,
                NodeId = anchorEvent?.NodeId ?? -1,
                FieldName = fieldName ?? string.Empty,
                Message = message ?? string.Empty,
                ExpectedEvent = expectedEvent,
                ActualEvent = actualEvent
            };
        }
    }

    public static class SkillExecutionEventComparer
    {
        public static SkillExecutionEventDiff Compare(
            IReadOnlyList<SkillExecutionEvent> expectedEvents,
            IReadOnlyList<SkillExecutionEvent> actualEvents)
        {
            IReadOnlyList<SkillExecutionEvent> safeExpected = expectedEvents ?? Array.Empty<SkillExecutionEvent>();
            IReadOnlyList<SkillExecutionEvent> safeActual = actualEvents ?? Array.Empty<SkillExecutionEvent>();

            int compareCount = Math.Min(safeExpected.Count, safeActual.Count);
            for (int index = 0; index < compareCount; index++)
            {
                SkillExecutionEvent expected = safeExpected[index];
                SkillExecutionEvent actual = safeActual[index];
                if (!EqualsValue(expected?.FrameIndex ?? 0, actual?.FrameIndex ?? 0))
                {
                    return SkillExecutionEventDiff.Mismatch(
                        index,
                        "frameIndex",
                        BuildMessage(index, "frameIndex", expected?.FrameIndex.ToString(CultureInfo.InvariantCulture), actual?.FrameIndex.ToString(CultureInfo.InvariantCulture)),
                        expected,
                        actual);
                }

                if (!EqualsValue(expected?.NodeId ?? 0, actual?.NodeId ?? 0))
                {
                    return SkillExecutionEventDiff.Mismatch(
                        index,
                        "nodeId",
                        BuildMessage(index, "nodeId", expected?.NodeId.ToString(CultureInfo.InvariantCulture), actual?.NodeId.ToString(CultureInfo.InvariantCulture)),
                        expected,
                        actual);
                }

                string expectedType = expected?.EventType ?? string.Empty;
                string actualType = actual?.EventType ?? string.Empty;
                if (!string.Equals(expectedType, actualType, StringComparison.Ordinal))
                {
                    return SkillExecutionEventDiff.Mismatch(
                        index,
                        "eventType",
                        BuildMessage(index, "eventType", expectedType, actualType),
                        expected,
                        actual);
                }

                string expectedPayload = expected?.Payload ?? string.Empty;
                string actualPayload = actual?.Payload ?? string.Empty;
                if (!string.Equals(expectedPayload, actualPayload, StringComparison.Ordinal))
                {
                    return SkillExecutionEventDiff.Mismatch(
                        index,
                        "payload",
                        BuildMessage(index, "payload", expectedPayload, actualPayload),
                        expected,
                        actual);
                }
            }

            if (safeExpected.Count != safeActual.Count)
            {
                SkillExecutionEvent? expectedOverflow = safeExpected.Count > compareCount ? safeExpected[compareCount] : null;
                SkillExecutionEvent? actualOverflow = safeActual.Count > compareCount ? safeActual[compareCount] : null;
                return SkillExecutionEventDiff.Mismatch(
                    compareCount,
                    "count",
                    $"Event count mismatch at index {compareCount}: expected {safeExpected.Count}, actual {safeActual.Count}.",
                    expectedOverflow,
                    actualOverflow);
            }

            return SkillExecutionEventDiff.Match();
        }

        private static bool EqualsValue<TValue>(TValue left, TValue right)
        {
            return EqualityComparer<TValue>.Default.Equals(left, right);
        }

        private static string BuildMessage(int index, string fieldName, string expectedValue, string actualValue)
        {
            return
                $"Event mismatch at index {index} field '{fieldName}': expected '{expectedValue ?? string.Empty}', actual '{actualValue ?? string.Empty}'.";
        }
    }

    public sealed class SkillStepFrameInput
    {
        public int MaxNodesPerStepOverride { get; set; }
    }

    public sealed class SkillGraphRunnerOptions
    {
        public int MaxNodesPerStep { get; set; } = 1;

        public int MaxExecutionSteps { get; set; }

        public float StepDeltaSeconds { get; set; } = 1f / 60f;

        public bool? ForceLockstep { get; set; }

        public bool EnableTrace { get; set; } = true;

        public bool UseLegacyAsyncNodesInLocalOnly { get; set; }
    }

    public sealed class SkillExecutionSnapshot
    {
        [JsonProperty("currentNodeId")]
        public int CurrentNodeId { get; set; }

        [JsonProperty("status")]
        public SkillExecutionStatus Status { get; set; }

        [JsonProperty("executedSteps")]
        public int ExecutedSteps { get; set; }

        [JsonProperty("frameIndex")]
        public int FrameIndex { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("blackboard")]
        public SkillBlackboardSnapshot Blackboard { get; set; } = new SkillBlackboardSnapshot();

        [JsonProperty("delayRemainingFrames")]
        public Dictionary<int, int> DelayRemainingFrames { get; set; } = new Dictionary<int, int>();
    }

    public sealed class SkillNodeHandlerRegistry
    {
        private readonly Dictionary<string, ISkillNodeHandler> _handlers =
            new Dictionary<string, ISkillNodeHandler>(StringComparer.OrdinalIgnoreCase);

        public void Register(string nodeType, ISkillNodeHandler handler)
        {
            if (string.IsNullOrWhiteSpace(nodeType))
                throw new ArgumentException("Node type cannot be empty.", nameof(nodeType));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _handlers[nodeType] = handler;
        }

        public bool TryGet(string nodeType, out ISkillNodeHandler? handler) =>
            _handlers.TryGetValue(nodeType ?? string.Empty, out handler);
    }

    public sealed class SkillGraphRunner
    {
        private const int MinimumExecutionStepLimit = 16;

        private readonly SkillNodeHandlerRegistry _handlerRegistry;
        private readonly Dictionary<int, int> _delayRemainingFrames = new Dictionary<int, int>();
        private readonly List<SkillExecutionEvent> _events = new List<SkillExecutionEvent>();

        private RuntimeSkillGraph? _graph;
        private SkillContext? _context;
        private SkillGraphRunnerOptions _options = new SkillGraphRunnerOptions();

        private bool _isInitialized;
        private bool _executionEndEmitted;
        private int _currentNodeId;
        private int _executedSteps;
        private int _maxExecutionSteps;
        private int _lastFrameIndex;
        private SkillExecutionStatus _status;
        private string _lastMessage = string.Empty;
        private Exception? _lastException;

        public SkillGraphRunner(SkillNodeHandlerRegistry handlerRegistry)
        {
            _handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        }

        public IReadOnlyList<SkillExecutionEvent> ExecutionEvents => _events;

        public SkillGraphRunResult Initialize(
            RuntimeSkillGraph graph,
            SkillContext context,
            SkillGraphRunnerOptions? options = null)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Runtime == null)
                throw new InvalidOperationException("SkillContext.Runtime must be assigned before execution.");

            RuntimeSkillNode? entryNode = graph.FindEntryNode();
            if (entryNode == null)
            {
                _isInitialized = false;
                return SkillGraphRunResult.Failure(
                    0,
                    0,
                    $"Runtime skill graph '{graph.SkillName}' does not contain an Entry node.");
            }

            _graph = graph;
            _context = context;
            _options = options ?? new SkillGraphRunnerOptions();
            if (_options.StepDeltaSeconds <= 0f)
                _options.StepDeltaSeconds = 1f / 60f;

            _currentNodeId = entryNode.NodeId;
            _executedSteps = 0;
            _lastFrameIndex = -1;
            _status = SkillExecutionStatus.Running;
            _lastMessage = string.Empty;
            _lastException = null;
            _executionEndEmitted = false;

            _delayRemainingFrames.Clear();
            _events.Clear();

            _maxExecutionSteps = ResolveMaxExecutionSteps(graph, context, _options.MaxExecutionSteps);
            _isInitialized = true;

            if (!TryInitializeBlackboard(graph, context, out string blackboardError))
            {
                _isInitialized = false;
                return SkillGraphRunResult.Failure(0, 0, blackboardError);
            }

            return BuildCurrentResult();
        }

        public async FTask<SkillGraphRunResult> Step(int frameIndex, SkillStepFrameInput? frameInput = null)
        {
            if (!_isInitialized || _graph == null || _context == null)
            {
                return SkillGraphRunResult.Failure(0, 0, "SkillGraphRunner is not initialized.");
            }

            _lastFrameIndex = frameIndex;
            if (_status != SkillExecutionStatus.Running)
                return BuildCurrentResult();

            int nodesBudget = ResolveStepNodeBudget(frameInput);
            for (int count = 0; count < nodesBudget; count++)
            {
                if (_status != SkillExecutionStatus.Running)
                    break;

                if (_context.IsCancellationRequested)
                {
                    _context.MarkCancelled();
                    return SetCancelled(_currentNodeId, "Skill graph execution was cancelled.");
                }

                RuntimeSkillNode? currentNode = _graph.GetNode(_currentNodeId);
                if (currentNode == null)
                {
                    return SetFailure(
                        _currentNodeId,
                        $"Node {_currentNodeId} referenced by execution cursor was not found.");
                }

                EmitEvent(frameIndex, currentNode.NodeId, SkillExecutionEventTypes.NodeEnter, string.Empty);

                SkillExecuteResult result;
                try
                {
                    result = await ExecuteNodeForStep(currentNode, frameIndex);
                }
                catch (Exception exception)
                {
                    return SetFailure(
                        currentNode.NodeId,
                        $"Node {currentNode.NodeId} ('{currentNode.NodeType}') execution failed: {exception.Message}",
                        exception);
                }

                if (result == null)
                {
                    return SetFailure(
                        currentNode.NodeId,
                        $"Node {currentNode.NodeId} ('{currentNode.NodeType}') returned a null execute result.");
                }

                switch (result.Status)
                {
                    case SkillExecutionStatus.Running:
                        _lastMessage = result.Message ?? string.Empty;
                        EmitEvent(
                            frameIndex,
                            currentNode.NodeId,
                            SkillExecutionEventTypes.NodeExit,
                            $"status=Running;message={_lastMessage}");
                        return BuildCurrentResult();

                    case SkillExecutionStatus.Failure:
                        return SetFailure(
                            currentNode.NodeId,
                            BuildNodeResultMessage(currentNode, result, "failed"),
                            result.Exception);

                    case SkillExecutionStatus.Cancelled:
                        _context.MarkCancelled();
                        return SetCancelled(
                            currentNode.NodeId,
                            BuildNodeResultMessage(currentNode, result, "was cancelled"));

                    case SkillExecutionStatus.Success:
                        _lastMessage = result.Message ?? string.Empty;
                        _executedSteps++;
                        if (_executedSteps > _maxExecutionSteps)
                        {
                            return SetFailure(
                                currentNode.NodeId,
                                $"Skill graph '{_graph.SkillName}' exceeded the v0.8 execution step limit ({_maxExecutionSteps}).");
                        }

                        EmitStepSpecificEvents(frameIndex, currentNode, result);
                        EmitEvent(
                            frameIndex,
                            currentNode.NodeId,
                            SkillExecutionEventTypes.NodeExit,
                            $"status=Success;nextPort={result.NextPort};message={_lastMessage}");

                        List<RuntimeConnection> nextConnections = _graph.GetNextConnections(currentNode.NodeId, result.NextPort);
                        if (nextConnections.Count == 0)
                            return SetSuccess(currentNode.NodeId, _lastMessage);

                        if (nextConnections.Count > 1)
                        {
                            return SetFailure(
                                currentNode.NodeId,
                                $"Node {currentNode.NodeId} port '{result.NextPort}' has multiple outgoing connections.");
                        }

                        RuntimeConnection nextConnection = nextConnections[0];
                        RuntimeSkillNode? nextNode = _graph.GetNode(nextConnection.ToNodeId);
                        if (nextNode == null)
                        {
                            return SetFailure(
                                nextConnection.ToNodeId,
                                $"Node {nextConnection.ToNodeId} referenced by connection was not found.");
                        }

                        _currentNodeId = nextNode.NodeId;
                        break;

                    default:
                        return SetFailure(
                            currentNode.NodeId,
                            $"Node {currentNode.NodeId} ('{currentNode.NodeType}') returned unsupported status '{result.Status}'.");
                }
            }

            return BuildCurrentResult();
        }

        public SkillExecutionSnapshot GetSnapshot()
        {
            if (!_isInitialized || _context == null)
                throw new InvalidOperationException("SkillGraphRunner is not initialized.");

            SkillBlackboard blackboard = _context.Blackboard ?? new SkillBlackboard();
            _context.Blackboard = blackboard;

            return new SkillExecutionSnapshot
            {
                CurrentNodeId = _currentNodeId,
                Status = _status,
                ExecutedSteps = _executedSteps,
                FrameIndex = _lastFrameIndex,
                Message = _lastMessage ?? string.Empty,
                Blackboard = blackboard.CaptureSnapshot(),
                DelayRemainingFrames = new Dictionary<int, int>(_delayRemainingFrames)
            };
        }

        public SkillGraphRunResult Restore(SkillExecutionSnapshot snapshot)
        {
            if (!_isInitialized || _context == null)
                throw new InvalidOperationException("SkillGraphRunner is not initialized.");

            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            _currentNodeId = snapshot.CurrentNodeId;
            _status = snapshot.Status;
            _executedSteps = snapshot.ExecutedSteps;
            _lastFrameIndex = snapshot.FrameIndex;
            _lastMessage = snapshot.Message ?? string.Empty;
            _lastException = null;
            _executionEndEmitted = _status != SkillExecutionStatus.Running;

            _delayRemainingFrames.Clear();
            foreach (KeyValuePair<int, int> pair in snapshot.DelayRemainingFrames ?? new Dictionary<int, int>())
            {
                if (pair.Value < 0)
                    continue;

                _delayRemainingFrames[pair.Key] = pair.Value;
            }

            SkillBlackboard blackboard = _context.Blackboard ?? new SkillBlackboard();
            _context.Blackboard = blackboard;
            blackboard.RestoreSnapshot(snapshot.Blackboard);

            return BuildCurrentResult();
        }

        public async FTask<SkillGraphRunResult> Run(RuntimeSkillGraph graph, SkillContext context)
        {
            int maxSteps = ResolveMaxExecutionSteps(graph, context, 0);
            SkillGraphRunResult initializeResult = Initialize(
                graph,
                context,
                new SkillGraphRunnerOptions
                {
                    MaxNodesPerStep = maxSteps,
                    MaxExecutionSteps = maxSteps,
                    StepDeltaSeconds = 1f / 60f,
                    UseLegacyAsyncNodesInLocalOnly = true
                });

            if (!initializeResult.IsRunning)
                return initializeResult;

            int frameIndex = 0;
            int frameSafetyLimit = Math.Max(MinimumExecutionStepLimit * 32, maxSteps * 4096);
            while (frameIndex <= frameSafetyLimit)
            {
                SkillGraphRunResult stepResult = await Step(frameIndex, null);
                if (!stepResult.IsRunning)
                    return stepResult;

                frameIndex++;
            }

            return SetFailure(
                _currentNodeId,
                $"Skill graph '{_graph!.SkillName}' exceeded the v0.8 frame safety limit ({frameSafetyLimit}).");
        }

        private async FTask<SkillExecuteResult> ExecuteNodeForStep(RuntimeSkillNode node, int frameIndex)
        {
            bool isLockstep = IsLockstepMode();
            bool useLegacyAsyncNodes = !isLockstep && _options.UseLegacyAsyncNodesInLocalOnly;

            if (string.Equals(node.NodeType, RuntimeNodeTypes.Action, StringComparison.Ordinal) && isLockstep)
                return ExecuteActionNodeInLockstep(node, frameIndex);

            if (string.Equals(node.NodeType, RuntimeNodeTypes.Delay, StringComparison.Ordinal) && !useLegacyAsyncNodes)
                return ExecuteDelayNodeStep(node);

            if (!_handlerRegistry.TryGet(node.NodeType, out ISkillNodeHandler? handler) || handler == null)
            {
                throw new InvalidOperationException($"No handler registered for node type '{node.NodeType}'.");
            }

            return await handler.Execute(node, _context!);
        }

        private SkillExecuteResult ExecuteDelayNodeStep(RuntimeSkillNode node)
        {
            if (SkillHandlerUtility.IsCancellationRequested(_context!))
                return SkillExecuteResult.Cancelled($"Delay node {node.NodeId} was cancelled before it started.");

            if (!_delayRemainingFrames.TryGetValue(node.NodeId, out int remainingFrames))
            {
                float durationSeconds = node.GetFloatPropertyValue(RuntimePropertyKeys.Duration, 0f);
                remainingFrames = ConvertSecondsToFrames(durationSeconds);
                _delayRemainingFrames[node.NodeId] = remainingFrames;
            }

            if (remainingFrames > 0)
            {
                remainingFrames--;
                _delayRemainingFrames[node.NodeId] = remainingFrames;
                if (remainingFrames > 0)
                {
                    return SkillExecuteResult.Running(
                        $"Delay node {node.NodeId} waiting {remainingFrames} frame(s).");
                }
            }

            _delayRemainingFrames.Remove(node.NodeId);
            return SkillExecuteResult.Success("Out");
        }

        private SkillExecuteResult ExecuteActionNodeInLockstep(RuntimeSkillNode node, int frameIndex)
        {
            string actionType = node.GetPropertyValue(RuntimePropertyKeys.ActionType);
            if (!string.Equals(actionType, RuntimeActionTypes.PlayAnimation, StringComparison.OrdinalIgnoreCase))
            {
                return SkillExecuteResult.Failure(
                    $"Action node {node.NodeId} actionType '{actionType}' is not supported in lockstep mode.");
            }

            string prefabLocation = node.GetPropertyValue(RuntimePropertyKeys.PrefabLocation);
            float speed = node.GetFloatPropertyValue(RuntimePropertyKeys.Value, 1f);
            EmitEvent(
                frameIndex,
                node.NodeId,
                SkillExecutionEventTypes.CommandIssued,
                $"actionType={actionType};prefabLocation={prefabLocation};speed={speed.ToString(CultureInfo.InvariantCulture)}");
            return SkillExecuteResult.Success("Out");
        }

        private void EmitStepSpecificEvents(int frameIndex, RuntimeSkillNode node, SkillExecuteResult result)
        {
            if (string.Equals(node.NodeType, RuntimeNodeTypes.SetVariable, StringComparison.Ordinal))
            {
                string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
                string valueType = node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.String);
                string value = node.GetPropertyValue(RuntimePropertyKeys.Value);
                EmitEvent(
                    frameIndex,
                    node.NodeId,
                    SkillExecutionEventTypes.BlackboardSet,
                    $"key={key};valueType={valueType};value={value}");
                return;
            }

            if (string.Equals(node.NodeType, RuntimeNodeTypes.Condition, StringComparison.Ordinal) ||
                string.Equals(node.NodeType, RuntimeNodeTypes.Branch, StringComparison.Ordinal))
            {
                EmitEvent(
                    frameIndex,
                    node.NodeId,
                    SkillExecutionEventTypes.BranchTaken,
                    $"nextPort={result.NextPort}");
            }
        }

        private bool TryInitializeBlackboard(RuntimeSkillGraph graph, SkillContext context, out string errorMessage)
        {
            errorMessage = string.Empty;
            SkillBlackboard blackboard = context.Blackboard ?? new SkillBlackboard();
            context.Blackboard = blackboard;

            foreach (RuntimeVariableDef variable in graph.Variables ?? new List<RuntimeVariableDef>())
            {
                if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
                    continue;

                try
                {
                    SkillBlackboardUtility.SetValue(
                        blackboard,
                        variable.Name,
                        variable.ValueType,
                        variable.DefaultValue);
                }
                catch (Exception exception)
                {
                    errorMessage =
                        $"Failed to initialize blackboard variable '{variable.Name}' from default value '{variable.DefaultValue}': {exception.Message}";
                    return false;
                }
            }

            return true;
        }

        private bool IsLockstepMode()
        {
            if (_options.ForceLockstep.HasValue)
                return _options.ForceLockstep.Value;

            return _graph != null &&
                   string.Equals(_graph.SyncMode, RuntimeSyncModes.Lockstep, StringComparison.OrdinalIgnoreCase);
        }

        private int ConvertSecondsToFrames(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                return 0;

            if (seconds <= 0f)
                return 0;

            float delta = _options.StepDeltaSeconds <= 0f ? 1f / 60f : _options.StepDeltaSeconds;
            return Math.Max(1, (int)Math.Ceiling(seconds / delta));
        }

        private int ResolveStepNodeBudget(SkillStepFrameInput? frameInput)
        {
            int budget = _options.MaxNodesPerStep > 0 ? _options.MaxNodesPerStep : 1;
            if (frameInput != null && frameInput.MaxNodesPerStepOverride > 0)
                budget = frameInput.MaxNodesPerStepOverride;

            return Math.Max(1, budget);
        }

        private SkillGraphRunResult BuildCurrentResult()
        {
            int lastNodeId = _currentNodeId;
            switch (_status)
            {
                case SkillExecutionStatus.Success:
                    return SkillGraphRunResult.Success(lastNodeId, _executedSteps, _lastMessage);
                case SkillExecutionStatus.Failure:
                    return SkillGraphRunResult.Failure(lastNodeId, _executedSteps, _lastMessage, _lastException);
                case SkillExecutionStatus.Cancelled:
                    return SkillGraphRunResult.Cancelled(lastNodeId, _executedSteps, _lastMessage);
                default:
                    return SkillGraphRunResult.Running(lastNodeId, _executedSteps, _lastMessage);
            }
        }

        private SkillGraphRunResult SetSuccess(int lastNodeId, string message)
        {
            _status = SkillExecutionStatus.Success;
            _currentNodeId = lastNodeId;
            _lastMessage = message ?? string.Empty;
            _lastException = null;
            EmitExecutionEndIfNeeded(lastNodeId);
            return BuildCurrentResult();
        }

        private SkillGraphRunResult SetFailure(int lastNodeId, string message, Exception? exception = null)
        {
            _status = SkillExecutionStatus.Failure;
            _currentNodeId = lastNodeId;
            _lastMessage = message ?? string.Empty;
            _lastException = exception;
            EmitExecutionEndIfNeeded(lastNodeId);
            return BuildCurrentResult();
        }

        private SkillGraphRunResult SetCancelled(int lastNodeId, string message)
        {
            _status = SkillExecutionStatus.Cancelled;
            _currentNodeId = lastNodeId;
            _lastMessage = message ?? string.Empty;
            _lastException = null;
            EmitExecutionEndIfNeeded(lastNodeId);
            return BuildCurrentResult();
        }

        private void EmitExecutionEndIfNeeded(int nodeId)
        {
            if (_executionEndEmitted)
                return;

            _executionEndEmitted = true;
            EmitEvent(
                _lastFrameIndex,
                nodeId,
                SkillExecutionEventTypes.ExecutionEnd,
                $"status={_status};message={_lastMessage}");
        }

        private void EmitEvent(int frameIndex, int nodeId, string eventType, string payload)
        {
            if (!_options.EnableTrace)
                return;

            _events.Add(new SkillExecutionEvent
            {
                FrameIndex = frameIndex,
                NodeId = nodeId,
                EventType = eventType ?? string.Empty,
                Payload = payload ?? string.Empty
            });
        }

        private static int ResolveMaxExecutionSteps(RuntimeSkillGraph graph, SkillContext context, int overrideValue)
        {
            if (overrideValue > 0)
                return overrideValue;

            if (context.MaxExecutionSteps > 0)
                return context.MaxExecutionSteps;

            int nodeCount = graph.Nodes == null ? 0 : graph.Nodes.Count;
            return Math.Max(MinimumExecutionStepLimit, nodeCount * 8);
        }

        private static string BuildNodeResultMessage(RuntimeSkillNode node, SkillExecuteResult result, string fallbackVerb)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
                return result.Message;

            return $"Node {node.NodeId} ('{node.NodeType}') {fallbackVerb}.";
        }
    }
}
