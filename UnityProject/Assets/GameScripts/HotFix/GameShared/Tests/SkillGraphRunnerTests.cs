#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Fantasy.Async;
using GameShared.FrameSync.Battle;
using NUnit.Framework;

namespace GameShared.SkillGraph.Tests
{
    [TestFixture]
    public sealed class SkillGraphRunnerTests
    {
        [Test]
        public void Run_ReturnsSuccess_ForBranchGraph()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "BranchGraph",
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "flag"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool),
                        CreateProperty(RuntimePropertyKeys.Value, "true")),
                    CreateNode(3, RuntimeNodeTypes.Branch,
                        CreateProperty(RuntimePropertyKeys.Key, "flag")),
                    CreateNode(4, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "True path")),
                    CreateNode(5, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "False path"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3),
                    CreateConnection(3, "True", 4),
                    CreateConnection(3, "False", 5)
                }
            };

            TestRuntimeServices runtime = new TestRuntimeServices();
            SkillGraphRunResult result = RunGraph(graph, runtime);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.LastNodeId, Is.EqualTo(4));
            Assert.That(result.ExecutedSteps, Is.EqualTo(4));
            Assert.That(runtime.Logs, Is.EqualTo(new[] { "True path" }));
        }

        [Test]
        public void Run_ReturnsCancelled_WhenDelayIsCancelled()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "DelayCancelled",
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Delay,
                        CreateProperty("duration", "1")),
                    CreateNode(3, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "Should not run"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3)
                }
            };

            TestRuntimeServices runtime = new TestRuntimeServices
            {
                DelayResult = false
            };
            SkillGraphRunResult result = RunGraph(graph, runtime);

            Assert.That(result.IsCancelled, Is.True);
            Assert.That(result.LastNodeId, Is.EqualTo(2));
            Assert.That(runtime.Logs, Is.Empty);
        }

        [Test]
        public void Run_ReturnsFailure_WhenActionThrows()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "ActionFailure",
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Action,
                        CreateProperty(RuntimePropertyKeys.ActionType, RuntimeActionTypes.PlayAnimation),
                        CreateProperty(RuntimePropertyKeys.PrefabLocation, "Test/SkillPrefab"),
                        CreateProperty(RuntimePropertyKeys.Value, "1"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2)
                }
            };

            TestRuntimeServices runtime = new TestRuntimeServices
            {
                PlayAnimationException = new InvalidOperationException("Animation failed")
            };
            SkillGraphRunResult result = RunGraph(graph, runtime);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.LastNodeId, Is.EqualTo(2));
            Assert.That(result.Message, Does.Contain("Animation failed"));
            Assert.That(runtime.PlayAnimationCalls, Is.EqualTo(1));
        }

        [Test]
        public void Run_QueuesApplyBuffCommand_InLockstep()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "ApplyBuffLockstep",
                SyncMode = RuntimeSyncModes.Lockstep,
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.ApplyBuff,
                        CreateProperty(RuntimePropertyKeys.TargetSelector, RuntimeBuffTargetSelectors.Caster),
                        CreateProperty(RuntimePropertyKeys.BuffId, "9001"),
                        CreateProperty(RuntimePropertyKeys.DurationFrames, "45"),
                        CreateProperty(RuntimePropertyKeys.StackCount, "2"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2)
                }
            };

            TestBuffCommandSink buffCommandSink = new TestBuffCommandSink();
            TestRuntimeServices runtime = new TestRuntimeServices();
            SkillGraphRunResult result = RunGraph(
                graph,
                runtime,
                context =>
                {
                    context.CasterId = 7;
                    context.TargetId = 99;
                    context.BuffCommandSink = buffCommandSink;
                });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(buffCommandSink.ApplyCommands.Count, Is.EqualTo(1));
            ApplyBuffCommand command = buffCommandSink.ApplyCommands[0];
            Assert.That(command.CasterId, Is.EqualTo(7));
            Assert.That(command.TargetId, Is.EqualTo(7));
            Assert.That(command.BuffId, Is.EqualTo(9001));
            Assert.That(command.DurationFrames, Is.EqualTo(45));
            Assert.That(command.StackCount, Is.EqualTo(2));
            Assert.That(command.FrameIndex, Is.EqualTo(0u));
        }

        [Test]
        public void Run_SkipsApplyBuffQueue_InLocalOnly()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "ApplyBuffLocalOnly",
                SyncMode = RuntimeSyncModes.LocalOnly,
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.ApplyBuff,
                        CreateProperty(RuntimePropertyKeys.TargetSelector, RuntimeBuffTargetSelectors.Target),
                        CreateProperty(RuntimePropertyKeys.BuffId, "9001"),
                        CreateProperty(RuntimePropertyKeys.DurationFrames, "30"),
                        CreateProperty(RuntimePropertyKeys.StackCount, "1"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2)
                }
            };

            TestBuffCommandSink buffCommandSink = new TestBuffCommandSink();
            SkillGraphRunResult result = RunGraph(
                graph,
                new TestRuntimeServices(),
                context =>
                {
                    context.CasterId = 7;
                    context.TargetId = 9;
                    context.BuffCommandSink = buffCommandSink;
                });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(buffCommandSink.ApplyCommands, Is.Empty);
        }

        [Test]
        public void Run_BuffConditionRoutesByStackCount()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "BuffConditionBranch",
                SyncMode = RuntimeSyncModes.Lockstep,
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.BuffCondition,
                        CreateProperty(RuntimePropertyKeys.TargetSelector, RuntimeBuffTargetSelectors.Target),
                        CreateProperty(RuntimePropertyKeys.BuffId, "9001"),
                        CreateProperty(RuntimePropertyKeys.MinimumStackCount, "2")),
                    CreateNode(3, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "True path")),
                    CreateNode(4, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "False path"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "True", 3),
                    CreateConnection(2, "False", 4)
                }
            };

            TestBuffCommandSink successSink = new TestBuffCommandSink();
            successSink.SetBuffStackCount(9, 9001, 3);
            TestRuntimeServices successRuntime = new TestRuntimeServices();
            SkillGraphRunResult successResult = RunGraph(
                graph,
                successRuntime,
                context =>
                {
                    context.TargetId = 9;
                    context.BuffCommandSink = successSink;
                });

            Assert.That(successResult.IsSuccess, Is.True);
            Assert.That(successResult.LastNodeId, Is.EqualTo(3));
            Assert.That(successRuntime.Logs, Is.EqualTo(new[] { "True path" }));

            TestBuffCommandSink failSink = new TestBuffCommandSink();
            failSink.SetBuffStackCount(9, 9001, 1);
            TestRuntimeServices failRuntime = new TestRuntimeServices();
            SkillGraphRunResult failResult = RunGraph(
                graph,
                failRuntime,
                context =>
                {
                    context.TargetId = 9;
                    context.BuffCommandSink = failSink;
                });

            Assert.That(failResult.IsSuccess, Is.True);
            Assert.That(failResult.LastNodeId, Is.EqualTo(4));
            Assert.That(failRuntime.Logs, Is.EqualTo(new[] { "False path" }));
        }

        [Test]
        public void Run_ReturnsFailure_WhenStepLimitIsExceeded()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "LoopGraph",
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "A")),
                    CreateNode(3, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "B"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3),
                    CreateConnection(3, "Out", 2)
                }
            };

            TestRuntimeServices runtime = new TestRuntimeServices();
            SkillGraphRunResult result = RunGraph(graph, runtime, context => context.MaxExecutionSteps = 3);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Message, Does.Contain("step limit"));
            Assert.That(result.ExecutedSteps, Is.EqualTo(4));
        }

        [Test]
        public void Step_SnapshotRestore_ProducesSameResultAndEventStream()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "SnapshotRestoreDeterminism",
                SyncMode = RuntimeSyncModes.LocalOnly,
                Variables = new List<RuntimeVariableDef>
                {
                    new RuntimeVariableDef
                    {
                        Name = "counter",
                        ValueType = RuntimeValueTypes.Int,
                        DefaultValue = "0"
                    }
                },
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Delay,
                        CreateProperty(RuntimePropertyKeys.Duration, "0.3")),
                    CreateNode(3, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "counter"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "2")),
                    CreateNode(4, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "Done"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3),
                    CreateConnection(3, "Out", 4)
                }
            };

            SkillGraphRunnerOptions options = CreateStepOptions();
            SkillGraphRunResult baselineResult = RunGraphByStep(
                graph,
                options,
                out List<SkillExecutionEvent> baselineEvents,
                out SkillBlackboardSnapshot baselineBlackboard);

            SkillGraphRunner snapshotRunner = CreateRunner(out SkillContext snapshotContext, out _);
            SkillGraphRunResult init = snapshotRunner.Initialize(graph, snapshotContext, options);
            Assert.That(init.IsRunning, Is.True);

            SkillGraphRunResult frame0 = Step(snapshotRunner, 0);
            SkillGraphRunResult frame1 = Step(snapshotRunner, 1);
            Assert.That(frame0.IsRunning, Is.True);
            Assert.That(frame1.IsRunning, Is.True);

            SkillExecutionSnapshot snapshot = snapshotRunner.GetSnapshot();
            List<SkillExecutionEvent> prefixEvents = CloneEvents(snapshotRunner.ExecutionEvents);

            SkillGraphRunner restoredRunner = CreateRunner(out SkillContext restoredContext, out _);
            SkillGraphRunResult restoreInit = restoredRunner.Initialize(graph, restoredContext, options);
            Assert.That(restoreInit.IsRunning, Is.True);

            SkillGraphRunResult restoreState = restoredRunner.Restore(snapshot);
            Assert.That(restoreState.IsRunning, Is.True);

            SkillGraphRunResult restoredResult = StepUntilCompleted(restoredRunner, 2);

            List<SkillExecutionEvent> combinedEvents = new List<SkillExecutionEvent>(prefixEvents);
            combinedEvents.AddRange(CloneEvents(restoredRunner.ExecutionEvents));

            AssertRunResultEquivalent(baselineResult, restoredResult);
            AssertBlackboardEqual(baselineBlackboard, restoredContext.Blackboard.CaptureSnapshot());
            AssertEventStreamsEqual(baselineEvents, combinedEvents);
        }

        [Test]
        public void SnapshotRestore_PreservesDelayRemainingFrames()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "DelaySnapshot",
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Delay,
                        CreateProperty(RuntimePropertyKeys.Duration, "0.5"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2)
                }
            };

            SkillGraphRunnerOptions options = CreateStepOptions();
            SkillGraphRunner sourceRunner = CreateRunner(out SkillContext sourceContext, out _);
            SkillGraphRunResult init = sourceRunner.Initialize(graph, sourceContext, options);
            Assert.That(init.IsRunning, Is.True);

            SkillGraphRunResult frame0 = Step(sourceRunner, 0);
            SkillGraphRunResult frame1 = Step(sourceRunner, 1);
            SkillGraphRunResult frame2 = Step(sourceRunner, 2);
            Assert.That(frame0.IsRunning, Is.True);
            Assert.That(frame1.IsRunning, Is.True);
            Assert.That(frame2.IsRunning, Is.True);

            SkillExecutionSnapshot snapshot = sourceRunner.GetSnapshot();
            Assert.That(snapshot.DelayRemainingFrames.TryGetValue(2, out int remainingFrames), Is.True);
            Assert.That(remainingFrames, Is.EqualTo(3));

            SkillGraphRunner restoredRunner = CreateRunner(out SkillContext restoredContext, out _);
            SkillGraphRunResult restoreInit = restoredRunner.Initialize(graph, restoredContext, options);
            Assert.That(restoreInit.IsRunning, Is.True);
            SkillGraphRunResult restoreState = restoredRunner.Restore(snapshot);
            Assert.That(restoreState.IsRunning, Is.True);

            SkillGraphRunResult replay1 = Step(restoredRunner, 3);
            SkillGraphRunResult replay2 = Step(restoredRunner, 4);
            SkillGraphRunResult replay3 = Step(restoredRunner, 5);

            Assert.That(replay1.IsRunning, Is.True);
            Assert.That(replay2.IsRunning, Is.True);
            Assert.That(replay3.IsSuccess, Is.True);
            Assert.That(replay3.LastNodeId, Is.EqualTo(2));
            Assert.That(replay3.ExecutedSteps, Is.EqualTo(2));
        }

        [Test]
        public void SnapshotRestore_RestoresBlackboardBeforeContinuing()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "SetVariableSnapshot",
                Variables = new List<RuntimeVariableDef>
                {
                    new RuntimeVariableDef
                    {
                        Name = "score",
                        ValueType = RuntimeValueTypes.Int,
                        DefaultValue = "0"
                    }
                },
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "score"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "1")),
                    CreateNode(3, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "score"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "2"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3)
                }
            };

            SkillGraphRunner snapshotRunner = CreateRunner(out SkillContext context, out _);
            SkillGraphRunResult init = snapshotRunner.Initialize(graph, context, CreateStepOptions());
            Assert.That(init.IsRunning, Is.True);

            Step(snapshotRunner, 0);
            Step(snapshotRunner, 1);

            SkillExecutionSnapshot snapshot = snapshotRunner.GetSnapshot();
            AssertBlackboardInt(context.Blackboard, "score", 1);

            context.Blackboard.SetInt("score", 99);
            SkillGraphRunResult restoredState = snapshotRunner.Restore(snapshot);
            Assert.That(restoredState.IsRunning, Is.True);
            AssertBlackboardInt(context.Blackboard, "score", 1);

            SkillGraphRunResult finalResult = Step(snapshotRunner, 2);
            Assert.That(finalResult.IsSuccess, Is.True);
            AssertBlackboardInt(context.Blackboard, "score", 2);
        }

        [Test]
        public void SnapshotRestore_SupportsNestedRestores()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "NestedSnapshots",
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Delay,
                        CreateProperty(RuntimePropertyKeys.Duration, "0.3"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2)
                }
            };

            SkillGraphRunnerOptions options = CreateStepOptions();
            SkillGraphRunner sourceRunner = CreateRunner(out SkillContext sourceContext, out _);
            SkillGraphRunResult init = sourceRunner.Initialize(graph, sourceContext, options);
            Assert.That(init.IsRunning, Is.True);

            Step(sourceRunner, 0);
            Step(sourceRunner, 1);
            SkillExecutionSnapshot snapshotA = sourceRunner.GetSnapshot();

            Step(sourceRunner, 2);
            SkillExecutionSnapshot snapshotB = sourceRunner.GetSnapshot();

            SkillGraphRunner replayRunner = CreateRunner(out SkillContext replayContext, out _);
            SkillGraphRunResult replayInit = replayRunner.Initialize(graph, replayContext, options);
            Assert.That(replayInit.IsRunning, Is.True);

            SkillGraphRunResult afterRestoreA = replayRunner.Restore(snapshotA);
            Assert.That(afterRestoreA.IsRunning, Is.True);
            SkillGraphRunResult afterStepA = Step(replayRunner, 3);
            Assert.That(afterStepA.IsRunning, Is.True);

            SkillGraphRunResult afterRestoreB = replayRunner.Restore(snapshotB);
            Assert.That(afterRestoreB.IsRunning, Is.True);
            SkillGraphRunResult afterStepB = StepUntilCompleted(replayRunner, 4);
            Assert.That(afterStepB.IsSuccess, Is.True);
            Assert.That(afterStepB.LastNodeId, Is.EqualTo(2));
        }

        [Test]
        public void Step_Replay_IsDeterministic_ForSameInput()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "DeterministicReplay",
                SyncMode = RuntimeSyncModes.Lockstep,
                Variables = new List<RuntimeVariableDef>
                {
                    new RuntimeVariableDef
                    {
                        Name = "ready",
                        ValueType = RuntimeValueTypes.Bool,
                        DefaultValue = "true"
                    }
                },
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "ready"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool),
                        CreateProperty(RuntimePropertyKeys.Value, "true")),
                    CreateNode(3, RuntimeNodeTypes.Branch,
                        CreateProperty(RuntimePropertyKeys.Key, "ready"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool)),
                    CreateNode(4, RuntimeNodeTypes.Action,
                        CreateProperty(RuntimePropertyKeys.ActionType, RuntimeActionTypes.PlayAnimation),
                        CreateProperty(RuntimePropertyKeys.PrefabLocation, "UI/TestUI"),
                        CreateProperty(RuntimePropertyKeys.Value, "1")),
                    CreateNode(5, RuntimeNodeTypes.Debug,
                        CreateProperty("message", "unexpected"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3),
                    CreateConnection(3, "True", 4),
                    CreateConnection(3, "False", 5)
                }
            };

            SkillGraphRunnerOptions options = CreateStepOptions();
            SkillGraphRunResult run1 = RunGraphByStep(
                graph,
                options,
                out List<SkillExecutionEvent> events1,
                out SkillBlackboardSnapshot blackboard1);
            SkillGraphRunResult run2 = RunGraphByStep(
                graph,
                options,
                out List<SkillExecutionEvent> events2,
                out SkillBlackboardSnapshot blackboard2);

            AssertRunResultEquivalent(run1, run2);
            AssertBlackboardEqual(blackboard1, blackboard2);
            AssertEventStreamsEqual(events1, events2);
        }

        [Test]
        public void Step_RollbackReplay_RemainsConsistentAcrossFramesAndRounds()
        {
            RuntimeSkillGraph graph = new RuntimeSkillGraph
            {
                SkillName = "RollbackMatrix",
                Variables = new List<RuntimeVariableDef>
                {
                    new RuntimeVariableDef
                    {
                        Name = "progress",
                        ValueType = RuntimeValueTypes.Int,
                        DefaultValue = "0"
                    }
                },
                Nodes = new List<RuntimeSkillNode>
                {
                    CreateNode(1, RuntimeNodeTypes.Entry),
                    CreateNode(2, RuntimeNodeTypes.Delay,
                        CreateProperty(RuntimePropertyKeys.Duration, "0.2")),
                    CreateNode(3, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "progress"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "1")),
                    CreateNode(4, RuntimeNodeTypes.Delay,
                        CreateProperty(RuntimePropertyKeys.Duration, "0.2")),
                    CreateNode(5, RuntimeNodeTypes.SetVariable,
                        CreateProperty(RuntimePropertyKeys.Key, "progress"),
                        CreateProperty(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Int),
                        CreateProperty(RuntimePropertyKeys.Value, "2"))
                },
                Connections = new List<RuntimeConnection>
                {
                    CreateConnection(1, "Next", 2),
                    CreateConnection(2, "Out", 3),
                    CreateConnection(3, "Out", 4),
                    CreateConnection(4, "Out", 5)
                }
            };

            SkillGraphRunnerOptions options = CreateStepOptions();
            SkillGraphRunResult baselineResult = RunGraphByStep(
                graph,
                options,
                out List<SkillExecutionEvent> baselineEvents,
                out SkillBlackboardSnapshot baselineBlackboard);

            int[] snapshotFrames = { 0, 1, 2, 3, 4 };
            Dictionary<int, SkillExecutionSnapshot> snapshots = new Dictionary<int, SkillExecutionSnapshot>();
            Dictionary<int, List<SkillExecutionEvent>> prefixes = new Dictionary<int, List<SkillExecutionEvent>>();

            SkillGraphRunner sourceRunner = CreateRunner(out SkillContext sourceContext, out _);
            SkillGraphRunResult init = sourceRunner.Initialize(graph, sourceContext, options);
            Assert.That(init.IsRunning, Is.True);

            for (int frame = 0; frame <= 64; frame++)
            {
                SkillGraphRunResult stepResult = Step(sourceRunner, frame);
                if (snapshotFrames.Contains(frame) && stepResult.IsRunning)
                {
                    snapshots[frame] = sourceRunner.GetSnapshot();
                    prefixes[frame] = CloneEvents(sourceRunner.ExecutionEvents);
                }

                if (!stepResult.IsRunning)
                    break;
            }

            foreach (int frame in snapshotFrames)
            {
                Assert.That(snapshots.ContainsKey(frame), Is.True, $"Snapshot for frame {frame} was not captured.");

                for (int round = 0; round < 3; round++)
                {
                    SkillGraphRunner replayRunner = CreateRunner(out SkillContext replayContext, out _);
                    SkillGraphRunResult replayInit = replayRunner.Initialize(graph, replayContext, options);
                    Assert.That(replayInit.IsRunning, Is.True);

                    SkillGraphRunResult restoredState = replayRunner.Restore(snapshots[frame]);
                    Assert.That(restoredState.IsRunning, Is.True);

                    SkillGraphRunResult replayResult = StepUntilCompleted(replayRunner, frame + 1);

                    List<SkillExecutionEvent> mergedEvents = new List<SkillExecutionEvent>(prefixes[frame]);
                    mergedEvents.AddRange(CloneEvents(replayRunner.ExecutionEvents));

                    AssertRunResultEquivalent(baselineResult, replayResult);
                    AssertBlackboardEqual(baselineBlackboard, replayContext.Blackboard.CaptureSnapshot());
                    AssertEventStreamsEqual(baselineEvents, mergedEvents);
                }
            }
        }

        [Test]
        public void EventComparer_ReportsFirstMismatchWithFrameNodeAndField()
        {
            List<SkillExecutionEvent> expectedEvents = new List<SkillExecutionEvent>
            {
                new SkillExecutionEvent
                {
                    FrameIndex = 7,
                    NodeId = 12,
                    EventType = SkillExecutionEventTypes.CommandIssued,
                    Payload = "speed=1"
                }
            };

            List<SkillExecutionEvent> actualEvents = new List<SkillExecutionEvent>
            {
                new SkillExecutionEvent
                {
                    FrameIndex = 7,
                    NodeId = 12,
                    EventType = SkillExecutionEventTypes.CommandIssued,
                    Payload = "speed=2"
                }
            };

            SkillExecutionEventDiff diff = SkillExecutionEventComparer.Compare(expectedEvents, actualEvents);
            Assert.That(diff.IsMatch, Is.False);
            Assert.That(diff.EventIndex, Is.EqualTo(0));
            Assert.That(diff.FrameIndex, Is.EqualTo(7));
            Assert.That(diff.NodeId, Is.EqualTo(12));
            Assert.That(diff.FieldName, Is.EqualTo("payload"));
        }

        private static SkillGraphRunResult RunGraph(
            RuntimeSkillGraph graph,
            TestRuntimeServices runtime,
            Action<SkillContext>? configureContext = null)
        {
            SkillNodeHandlerRegistry registry = new SkillNodeHandlerRegistry();
            SkillHandlers.RegisterDefaults(registry);

            SkillContext context = new SkillContext
            {
                Runtime = runtime
            };

            configureContext?.Invoke(context);

            SkillGraphRunner runner = new SkillGraphRunner(registry);
            return runner.Run(graph, context).GetAwaiter().GetResult();
        }

        private static SkillGraphRunResult RunGraphByStep(
            RuntimeSkillGraph graph,
            SkillGraphRunnerOptions options,
            out List<SkillExecutionEvent> events,
            out SkillBlackboardSnapshot blackboardSnapshot)
        {
            SkillGraphRunner runner = CreateRunner(out SkillContext context, out _);
            SkillGraphRunResult init = runner.Initialize(graph, context, options);
            Assert.That(init.IsRunning, Is.True);

            SkillGraphRunResult result = StepUntilCompleted(runner, 0);
            events = CloneEvents(runner.ExecutionEvents);
            blackboardSnapshot = context.Blackboard.CaptureSnapshot();
            return result;
        }

        private static SkillGraphRunner CreateRunner(out SkillContext context, out TestRuntimeServices runtime)
        {
            SkillNodeHandlerRegistry registry = new SkillNodeHandlerRegistry();
            SkillHandlers.RegisterDefaults(registry);

            runtime = new TestRuntimeServices();
            context = new SkillContext
            {
                Runtime = runtime
            };

            return new SkillGraphRunner(registry);
        }

        private static SkillGraphRunnerOptions CreateStepOptions()
        {
            return new SkillGraphRunnerOptions
            {
                MaxNodesPerStep = 1,
                MaxExecutionSteps = 256,
                StepDeltaSeconds = 0.1f,
                UseLegacyAsyncNodesInLocalOnly = false,
                EnableTrace = true
            };
        }

        private static SkillGraphRunResult Step(SkillGraphRunner runner, int frameIndex)
        {
            return runner.Step(frameIndex, null).GetAwaiter().GetResult();
        }

        private static SkillGraphRunResult StepUntilCompleted(SkillGraphRunner runner, int startFrame)
        {
            const int frameLimit = 512;
            for (int offset = 0; offset < frameLimit; offset++)
            {
                SkillGraphRunResult result = Step(runner, startFrame + offset);
                if (!result.IsRunning)
                    return result;
            }

            Assert.Fail($"Skill graph did not complete within {frameLimit} frames.");
            return SkillGraphRunResult.Failure(0, 0, "Frame limit exceeded.");
        }

        private static List<SkillExecutionEvent> CloneEvents(IEnumerable<SkillExecutionEvent> events)
        {
            List<SkillExecutionEvent> cloned = new List<SkillExecutionEvent>();
            if (events == null)
                return cloned;

            foreach (SkillExecutionEvent evt in events)
            {
                if (evt == null)
                    continue;

                cloned.Add(new SkillExecutionEvent
                {
                    FrameIndex = evt.FrameIndex,
                    NodeId = evt.NodeId,
                    EventType = evt.EventType ?? string.Empty,
                    Payload = evt.Payload ?? string.Empty
                });
            }

            return cloned;
        }

        private static void AssertRunResultEquivalent(SkillGraphRunResult expected, SkillGraphRunResult actual)
        {
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.LastNodeId, Is.EqualTo(expected.LastNodeId));
            Assert.That(actual.ExecutedSteps, Is.EqualTo(expected.ExecutedSteps));
        }

        private static void AssertBlackboardEqual(SkillBlackboardSnapshot expected, SkillBlackboardSnapshot actual)
        {
            Assert.That(actual.Strings, Is.EqualTo(expected.Strings));
            Assert.That(actual.Floats, Is.EqualTo(expected.Floats));
            Assert.That(actual.Ints, Is.EqualTo(expected.Ints));
            Assert.That(actual.Bools, Is.EqualTo(expected.Bools));
        }

        private static void AssertBlackboardInt(SkillBlackboard blackboard, string key, int expectedValue)
        {
            Assert.That(blackboard.TryGetInt(key, out int actualValue), Is.True);
            Assert.That(actualValue, Is.EqualTo(expectedValue));
        }

        private static void AssertEventStreamsEqual(
            IReadOnlyList<SkillExecutionEvent> expectedEvents,
            IReadOnlyList<SkillExecutionEvent> actualEvents)
        {
            SkillExecutionEventDiff diff = SkillExecutionEventComparer.Compare(expectedEvents, actualEvents);
            Assert.That(
                diff.IsMatch,
                Is.True,
                $"Event stream mismatch. index={diff.EventIndex}, frame={diff.FrameIndex}, node={diff.NodeId}, field={diff.FieldName}, message={diff.Message}");
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

        private sealed class TestRuntimeServices : ISkillRuntimeServices
        {
            public readonly List<string> Logs = new List<string>();

            public bool DelayResult { get; set; } = true;

            public bool PlayAnimationResult { get; set; } = true;

            public int PlayAnimationCalls { get; private set; }

            public Exception? PlayAnimationException { get; set; }

            public void Log(string message)
            {
                Logs.Add(message ?? string.Empty);
            }

            public FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null)
            {
                return FTask<bool>.FromResult(DelayResult);
            }

            public FTask<bool> PlayAnimationAsync(
                SkillContext context,
                string prefabLocation,
                float speed,
                FCancellationToken? cancellationToken = null)
            {
                PlayAnimationCalls++;

                if (PlayAnimationException != null)
                {
                    FTask<bool> failedTask = FTask<bool>.Create(false);
                    failedTask.SetException(PlayAnimationException);
                    return failedTask;
                }

                return FTask<bool>.FromResult(PlayAnimationResult);
            }
        }

        private sealed class TestBuffCommandSink : IBuffCommandSink
        {
            private readonly Dictionary<string, int> _stackCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            public List<ApplyBuffCommand> ApplyCommands { get; } = new List<ApplyBuffCommand>();

            public List<RemoveBuffCommand> RemoveCommands { get; } = new List<RemoveBuffCommand>();

            public void SetBuffStackCount(long targetId, int buffId, int stackCount)
            {
                _stackCounts[BuildKey(targetId, buffId)] = Math.Max(0, stackCount);
            }

            public void EnqueueApplyBuff(ApplyBuffCommand command)
            {
                ApplyCommands.Add(new ApplyBuffCommand
                {
                    CasterId = command.CasterId,
                    TargetId = command.TargetId,
                    BuffId = command.BuffId,
                    DurationFrames = command.DurationFrames,
                    StackCount = command.StackCount,
                    FrameIndex = command.FrameIndex,
                    Flags = command.Flags
                });
            }

            public void EnqueueRemoveBuff(RemoveBuffCommand command)
            {
                RemoveCommands.Add(new RemoveBuffCommand
                {
                    TargetId = command.TargetId,
                    RuntimeBuffId = command.RuntimeBuffId,
                    BuffId = command.BuffId,
                    RemoveReason = command.RemoveReason,
                    FrameIndex = command.FrameIndex
                });
            }

            public bool HasBuff(long targetId, int buffId)
            {
                return GetBuffStackCount(targetId, buffId) > 0;
            }

            public int GetBuffStackCount(long targetId, int buffId)
            {
                return _stackCounts.TryGetValue(BuildKey(targetId, buffId), out int stackCount)
                    ? stackCount
                    : 0;
            }

            private static string BuildKey(long targetId, int buffId)
            {
                return $"{targetId}:{buffId}";
            }
        }
    }
}
#endif
