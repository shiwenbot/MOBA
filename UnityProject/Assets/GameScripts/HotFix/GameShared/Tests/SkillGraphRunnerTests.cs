#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using Fantasy.Async;
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
    }
}
#endif
