using System;
using System.Collections.Generic;
using Fantasy.Async;
using GameShared.SkillGraph;
using Log = TEngine.Log;

namespace GameLogic
{
    internal static class SkillGraphStepBSmokeTest
    {
        public static void Run()
        {
            try
            {
                Execute();
                Log.Warning("[SkillGraph][StepB][Smoke] PASS");
            }
            catch (Exception exception)
            {
                Log.Error($"[SkillGraph][StepB][Smoke] FAIL: {exception.Message}\n{exception}");
            }
        }

        private static void Execute()
        {
            RuntimeSkillGraph graph = BuildGraph();
            SkillGraphRunnerOptions options = new SkillGraphRunnerOptions
            {
                MaxNodesPerStep = 1,
                MaxExecutionSteps = 256,
                StepDeltaSeconds = 0.1f,
                UseLegacyAsyncNodesInLocalOnly = false,
                EnableTrace = true
            };

            SkillGraphRunResult baselineResult = RunToEnd(
                graph,
                options,
                out List<SkillExecutionEvent> baselineEvents,
                out SkillBlackboardSnapshot baselineBlackboard);

            SkillGraphRunner sourceRunner = CreateRunner(out SkillContext sourceContext);
            SkillGraphRunResult init = sourceRunner.Initialize(graph, sourceContext, options);
            Ensure(init.IsRunning, "Source runner failed to initialize.");

            const int delayNodeId = 2;
            SkillExecutionSnapshot? snapshot = null;
            List<SkillExecutionEvent>? snapshotPrefixEvents = null;
            int delayRunningFrameCount = 0;
            for (int frame = 0; frame < 64; frame++)
            {
                SkillGraphRunResult stepResult = Step(sourceRunner, frame);
                if (stepResult.IsRunning)
                {
                    SkillExecutionSnapshot candidate = sourceRunner.GetSnapshot();
                    if (candidate.CurrentNodeId == delayNodeId &&
                        candidate.DelayRemainingFrames.TryGetValue(delayNodeId, out int remainingFrames) &&
                        remainingFrames > 0)
                    {
                        delayRunningFrameCount++;
                        if (delayRunningFrameCount >= 2)
                        {
                            snapshot = candidate;
                            snapshotPrefixEvents = CloneEvents(sourceRunner.ExecutionEvents);
                            break;
                        }
                    }
                }

                if (!stepResult.IsRunning)
                    break;
            }

            Ensure(snapshot != null, "Failed to capture running snapshot.");
            SkillExecutionSnapshot snapshotValue = snapshot ?? throw new InvalidOperationException("Failed to capture running snapshot.");
            List<SkillExecutionEvent> prefixEvents = snapshotPrefixEvents ??
                                                     throw new InvalidOperationException("Failed to capture snapshot event prefix.");

            SkillGraphRunner replayRunner = CreateRunner(out SkillContext replayContext);
            SkillGraphRunResult replayInit = replayRunner.Initialize(graph, replayContext, options);
            Ensure(replayInit.IsRunning, "Replay runner failed to initialize.");

            SkillGraphRunResult restored = replayRunner.Restore(snapshotValue);
            Ensure(restored.IsRunning, "Replay runner restore did not return running state.");

            SkillGraphRunResult replayResult = StepUntilComplete(replayRunner, snapshotValue.FrameIndex + 1);
            List<SkillExecutionEvent> mergedEvents = new List<SkillExecutionEvent>(prefixEvents);
            mergedEvents.AddRange(CloneEvents(replayRunner.ExecutionEvents));

            AssertRunResultEquivalent(baselineResult, replayResult);
            AssertBlackboardEquivalent(baselineBlackboard, replayContext.Blackboard.CaptureSnapshot());
            AssertEventStreamEquivalent(baselineEvents, mergedEvents);
        }

        private static RuntimeSkillGraph BuildGraph()
        {
            return new RuntimeSkillGraph
            {
                SkillName = "StepBSmokeGraph",
                SyncMode = RuntimeSyncModes.Lockstep,
                Variables = new List<RuntimeVariableDef>
                {
                    new RuntimeVariableDef
                    {
                        Name = "ready",
                        ValueType = RuntimeValueTypes.Bool,
                        DefaultValue = "true"
                    },
                    new RuntimeVariableDef
                    {
                        Name = "phase",
                        ValueType = RuntimeValueTypes.Int,
                        DefaultValue = "0"
                    }
                },
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Delay,
                        CreateProperty(RuntimePropertyKeys.Duration, "0.25")),
                    CreateNode(3, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "phase"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "1")),
                    CreateNode(4, RuntimeNodeTypes.Branch,
                        CreateProperty(RuntimePropertyKeys.Key, "ready"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool)),
                    CreateNode(5, RuntimeNodeTypes.Action,
                        CreateProperty(RuntimePropertyKeys.ActionType, RuntimeActionTypes.PlayAnimation),
                        CreateProperty(RuntimePropertyKeys.PrefabLocation, "UI/TestUI"),
                        CreateProperty(RuntimePropertyKeys.Value, "1")),
                    CreateNode(6, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "phase"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "2")),
                    CreateNode(7, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "Unexpected false branch"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3),
                    CreateConnection(3, "Out", 4),
                    CreateConnection(4, "True", 5),
                    CreateConnection(4, "False", 7),
                    CreateConnection(5, "Out", 6)
                }
            };
        }

        private static SkillGraphRunResult RunToEnd(
            RuntimeSkillGraph graph,
            SkillGraphRunnerOptions options,
            out List<SkillExecutionEvent> events,
            out SkillBlackboardSnapshot blackboard)
        {
            SkillGraphRunner runner = CreateRunner(out SkillContext context);
            SkillGraphRunResult init = runner.Initialize(graph, context, options);
            Ensure(init.IsRunning, "Baseline runner failed to initialize.");

            SkillGraphRunResult result = StepUntilComplete(runner, 0);
            events = CloneEvents(runner.ExecutionEvents);
            blackboard = context.Blackboard.CaptureSnapshot();
            return result;
        }

        private static SkillGraphRunner CreateRunner(out SkillContext context)
        {
            SkillNodeHandlerRegistry registry = new SkillNodeHandlerRegistry();
            SkillHandlers.RegisterDefaults(registry);

            context = new SkillContext
            {
                Runtime = new SmokeRuntimeServices()
            };

            return new SkillGraphRunner(registry);
        }

        private static SkillGraphRunResult StepUntilComplete(SkillGraphRunner runner, int startFrame)
        {
            const int frameLimit = 512;
            for (int offset = 0; offset < frameLimit; offset++)
            {
                SkillGraphRunResult result = Step(runner, startFrame + offset);
                if (!result.IsRunning)
                    return result;
            }

            throw new InvalidOperationException($"Runner did not complete within {frameLimit} frames.");
        }

        private static SkillGraphRunResult Step(SkillGraphRunner runner, int frameIndex)
        {
            return runner.Step(frameIndex, null).GetAwaiter().GetResult();
        }

        private static void AssertRunResultEquivalent(SkillGraphRunResult expected, SkillGraphRunResult actual)
        {
            Ensure(actual.Status == expected.Status, $"Run status mismatch. expected={expected.Status}, actual={actual.Status}");
            Ensure(actual.LastNodeId == expected.LastNodeId, $"LastNodeId mismatch. expected={expected.LastNodeId}, actual={actual.LastNodeId}");
            Ensure(actual.ExecutedSteps == expected.ExecutedSteps, $"ExecutedSteps mismatch. expected={expected.ExecutedSteps}, actual={actual.ExecutedSteps}");
        }

        private static void AssertBlackboardEquivalent(SkillBlackboardSnapshot expected, SkillBlackboardSnapshot actual)
        {
            Ensure(DictionaryEquals(expected.Strings, actual.Strings), "Blackboard.Strings mismatch.");
            Ensure(DictionaryEquals(expected.Ints, actual.Ints), "Blackboard.Ints mismatch.");
            Ensure(DictionaryEquals(expected.Floats, actual.Floats), "Blackboard.Floats mismatch.");
            Ensure(DictionaryEquals(expected.Bools, actual.Bools), "Blackboard.Bools mismatch.");
        }

        private static void AssertEventStreamEquivalent(
            IReadOnlyList<SkillExecutionEvent> expectedEvents,
            IReadOnlyList<SkillExecutionEvent> actualEvents)
        {
            SkillExecutionEventDiff diff = SkillExecutionEventComparer.Compare(expectedEvents, actualEvents);
            if (diff.IsMatch)
                return;

            throw new InvalidOperationException(
                $"Event mismatch index={diff.EventIndex}, frame={diff.FrameIndex}, node={diff.NodeId}, field={diff.FieldName}, message={diff.Message}");
        }

        private static List<SkillExecutionEvent> CloneEvents(IEnumerable<SkillExecutionEvent> events)
        {
            List<SkillExecutionEvent> result = new List<SkillExecutionEvent>();
            if (events == null)
                return result;

            foreach (SkillExecutionEvent evt in events)
            {
                if (evt == null)
                    continue;

                result.Add(new SkillExecutionEvent
                {
                    FrameIndex = evt.FrameIndex,
                    NodeId = evt.NodeId,
                    EventType = evt.EventType ?? string.Empty,
                    Payload = evt.Payload ?? string.Empty
                });
            }

            return result;
        }

        private static bool DictionaryEquals<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> left,
            IReadOnlyDictionary<TKey, TValue> right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null || left.Count != right.Count)
                return false;

            foreach (KeyValuePair<TKey, TValue> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out TValue value))
                    return false;

                if (!EqualityComparer<TValue>.Default.Equals(pair.Value, value))
                    return false;
            }

            return true;
        }

        private static RuntimeSkillNode CreateNode(int nodeId, string nodeType, params RuntimeProperty[] properties)
        {
            return new RuntimeSkillNode
            {
                NodeId = nodeId,
                NodeType = nodeType,
                Properties = new List<RuntimeProperty>(properties ?? Array.Empty<RuntimeProperty>())
            };
        }

        private static RuntimeConnection CreateConnection(int fromNodeId, string fromPort, int toNodeId)
        {
            return new RuntimeConnection
            {
                FromNodeId = fromNodeId,
                FromPort = fromPort,
                ToNodeId = toNodeId
            };
        }

        private static RuntimeProperty CreateProperty(string key, string value)
        {
            return new RuntimeProperty
            {
                Key = key,
                Value = value
            };
        }

        private static void Ensure(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class SmokeRuntimeServices : ISkillRuntimeServices
        {
            public void Log(string message)
            {
                global::TEngine.Log.Info($"[SkillGraph][StepB][Smoke] {message}");
            }

            public bool TryConsumeStamina(long playerId, int amount)
            {
                return true;
            }

            public FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null)
            {
                return FTask<bool>.FromResult(true);
            }

            public FTask<bool> PlayAnimationAsync(
                SkillContext context,
                string prefabLocation,
                float speed,
                FCancellationToken? cancellationToken = null)
            {
                return FTask<bool>.FromResult(true);
            }
        }
    }
}
