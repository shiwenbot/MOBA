using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Fantasy.Async;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using Newtonsoft.Json;

namespace GameShared.SkillGraph
{
    public static class RuntimeNodeTypes
    {
        public const string Entry = "Entry";
        public const string Debug = "Debug";
        public const string Action = "Action";
        public const string Condition = "Condition";
        public const string Branch = "Branch";
        public const string SetVariable = "SetVariable";
        public const string Delay = "Delay";
        public const string ApplyBuff = "ApplyBuff";
        public const string RemoveBuff = "RemoveBuff";
        public const string BuffCondition = "BuffCondition";
        public const string DashStart = "DashStart";
    }

    public static class RuntimeActionTypes
    {
        public const string PlayAnimation = "PlayAnimation";
    }

    public static class RuntimePropertyKeys
    {
        public const string ActionType = "actionType";
        public const string Key = "key";
        public const string Operator = "operator";
        public const string Value = "value";
        public const string ValueType = "valueType";
        public const string Duration = "duration";
        public const string PrefabAssetPath = "prefabAssetPath";
        public const string PrefabLocation = "prefabLocation";
        public const string BuffId = "buffId";
        public const string DurationFrames = "durationFrames";
        public const string StackCount = "stackCount";
        public const string TargetSelector = "targetSelector";
        public const string MinimumStackCount = "minimumStackCount";
    }

    public static class RuntimeValueTypes
    {
        public const string String = "String";
        public const string Float = "Float";
        public const string Int = "Int";
        public const string Bool = "Bool";
    }

    public static class RuntimeConditionOperators
    {
        public const string Exists = "Exists";
        public const string Equal = "Equal";
        public const string NotEqual = "NotEqual";
        public const string Greater = "Greater";
        public const string GreaterOrEqual = "GreaterOrEqual";
        public const string Less = "Less";
        public const string LessOrEqual = "LessOrEqual";
        public const string IsTrue = "IsTrue";
        public const string IsFalse = "IsFalse";
    }

    public static class RuntimeSyncModes
    {
        public const string Lockstep = "Lockstep";
        public const string LocalOnly = "LocalOnly";
    }

    public static class RuntimeBuffTargetSelectors
    {
        public const string Target = "Target";
        public const string Caster = "Caster";
    }

    public sealed class RuntimeSkillGraph
    {
        public const string CurrentVersion = "0.8";

        [JsonProperty("version")]
        public string Version { get; set; } = CurrentVersion;

        [JsonProperty("skillName")]
        public string SkillName { get; set; } = string.Empty;

        [JsonProperty("syncMode")]
        public string SyncMode { get; set; } = RuntimeSyncModes.LocalOnly;

        [JsonProperty("variables")]
        public List<RuntimeVariableDef> Variables { get; set; } = new List<RuntimeVariableDef>();

        [JsonProperty("deterministicFlags")]
        public List<string> DeterministicFlags { get; set; } = new List<string>();

        [JsonProperty("nodes")]
        public List<RuntimeSkillNode> Nodes { get; set; } = new List<RuntimeSkillNode>();

        [JsonProperty("connections")]
        public List<RuntimeConnection> Connections { get; set; } = new List<RuntimeConnection>();

        [JsonIgnore]
        private Dictionary<int, RuntimeSkillNode>? _nodeLookup;

        public RuntimeSkillNode? FindEntryNode()
        {
            foreach (RuntimeSkillNode node in Nodes)
            {
                if (node != null && string.Equals(node.NodeType, RuntimeNodeTypes.Entry, StringComparison.OrdinalIgnoreCase))
                    return node;
            }

            return null;
        }

        public RuntimeSkillNode? GetNode(int nodeId)
        {
            EnsureNodeLookup();
            _nodeLookup!.TryGetValue(nodeId, out RuntimeSkillNode? node);
            return node;
        }

        public List<RuntimeConnection> GetNextConnections(int nodeId, string fromPort)
        {
            List<RuntimeConnection> connections = new List<RuntimeConnection>();
            foreach (RuntimeConnection connection in Connections)
            {
                if (connection == null)
                    continue;

                if (connection.FromNodeId != nodeId)
                    continue;

                if (!string.Equals(connection.FromPort, fromPort, StringComparison.Ordinal))
                    continue;

                connections.Add(connection);
            }

            return connections;
        }

        private void EnsureNodeLookup()
        {
            if (_nodeLookup != null)
                return;

            _nodeLookup = new Dictionary<int, RuntimeSkillNode>();
            foreach (RuntimeSkillNode node in Nodes)
            {
                if (node == null)
                    continue;

                _nodeLookup[node.NodeId] = node;
            }
        }
    }

    public sealed class RuntimeVariableDef
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("valueType")]
        public string ValueType { get; set; } = RuntimeValueTypes.String;

        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; } = string.Empty;
    }

    public sealed class RuntimeSkillNode
    {
        [JsonProperty("nodeId")]
        public int NodeId { get; set; }

        [JsonProperty("nodeType")]
        public string NodeType { get; set; } = string.Empty;

        [JsonProperty("properties")]
        public List<RuntimeProperty> Properties { get; set; } = new List<RuntimeProperty>();

        public string GetPropertyValue(string key, string fallbackValue = "")
        {
            foreach (RuntimeProperty property in Properties)
            {
                if (property != null && string.Equals(property.Key, key, StringComparison.Ordinal))
                    return property.Value ?? fallbackValue;
            }

            return fallbackValue;
        }

        public float GetFloatPropertyValue(string key, float fallbackValue)
        {
            string rawValue = GetPropertyValue(key, fallbackValue.ToString(CultureInfo.InvariantCulture));
            return float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue)
                ? parsedValue
                : fallbackValue;
        }

        public int GetIntPropertyValue(string key, int fallbackValue)
        {
            string rawValue = GetPropertyValue(key, fallbackValue.ToString(CultureInfo.InvariantCulture));
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
                ? parsedValue
                : fallbackValue;
        }
    }

    public sealed class RuntimeConnection
    {
        [JsonProperty("fromNodeId")]
        public int FromNodeId { get; set; }

        [JsonProperty("fromPort")]
        public string FromPort { get; set; } = string.Empty;

        [JsonProperty("toNodeId")]
        public int ToNodeId { get; set; }
    }

    public sealed class RuntimeProperty
    {
        [JsonProperty("key")]
        public string Key { get; set; } = string.Empty;

        [JsonProperty("value")]
        public string Value { get; set; } = string.Empty;
    }

    public static class BattleSkillGraphLibrary
    {
        public const int DefaultSkillId = 1001;
        public const int DefaultBuffId = 9001;
        public const int DefaultBuffDurationFrames = 45;
        public const int DefaultBuffStackCount = 1;

        private const string RuntimeGraphDirectoryEnvVar = "BATTLE_SKILL_GRAPH_DIR";
        private const string SkillIdEnvVar = "BATTLE_AUTOMATION_SKILL_ID";
        private const string BuffIdEnvVar = "BATTLE_AUTOMATION_BUFF_ID";
        private const string BuffDurationEnvVar = "BATTLE_AUTOMATION_BUFF_DURATION_FRAMES";
        private const string BuffStackEnvVar = "BATTLE_AUTOMATION_BUFF_STACK_COUNT";

        public static int ResolveConfiguredSkillId() => GetEnvInt(SkillIdEnvVar, DefaultSkillId);

        public static int ResolveConfiguredBuffId() => GetEnvInt(BuffIdEnvVar, DefaultBuffId);

        public static int ResolveConfiguredBuffDurationFrames() =>
            Math.Max(1, GetEnvInt(BuffDurationEnvVar, DefaultBuffDurationFrames));

        public static int ResolveConfiguredBuffStackCount() =>
            Math.Max(1, GetEnvInt(BuffStackEnvVar, DefaultBuffStackCount));

        public static IReadOnlyDictionary<int, RuntimeSkillGraph> LoadDefaultGraphs()
        {
            Dictionary<int, RuntimeSkillGraph> graphs = CreateBuiltInGraphs();
            foreach (string directory in EnumerateRuntimeGraphDirectories())
            {
                LoadDirectoryInto(directory, graphs);
            }

            return graphs;
        }

        public static Dictionary<int, RuntimeSkillGraph> CreateBuiltInGraphs()
        {
            int skillId = ResolveConfiguredSkillId();
            int buffId = ResolveConfiguredBuffId();
            int durationFrames = ResolveConfiguredBuffDurationFrames();
            int stackCount = ResolveConfiguredBuffStackCount();

            Dictionary<int, RuntimeSkillGraph> graphs = new Dictionary<int, RuntimeSkillGraph>
            {
                [skillId] = CreateSelfBuffGraph(skillId, buffId, durationFrames, stackCount),
                [DashTuning.DashSkillId] = CreateDashGraph()
            };
            return graphs;
        }

        public static RuntimeSkillGraph CreateDashGraph()
        {
            return new RuntimeSkillGraph
            {
                Version = RuntimeSkillGraph.CurrentVersion,
                SkillName = "Dash",
                SyncMode = RuntimeSyncModes.Lockstep,
                DeterministicFlags = CreateLockstepDeterministicFlags(),
                Nodes = new List<RuntimeSkillNode>
                {
                    new RuntimeSkillNode { NodeId = 0, NodeType = RuntimeNodeTypes.Entry },
                    new RuntimeSkillNode { NodeId = 1, NodeType = RuntimeNodeTypes.DashStart },
                    new RuntimeSkillNode
                    {
                        NodeId = 2,
                        NodeType = RuntimeNodeTypes.Delay,
                        Properties = new List<RuntimeProperty>
                        {
                            new RuntimeProperty
                            {
                                Key = RuntimePropertyKeys.DurationFrames,
                                Value = DashTuning.DashFrames.ToString(CultureInfo.InvariantCulture)
                            }
                        }
                    },
                    new RuntimeSkillNode
                    {
                        NodeId = 3,
                        NodeType = RuntimeNodeTypes.ApplyBuff,
                        Properties = new List<RuntimeProperty>
                        {
                            new RuntimeProperty { Key = RuntimePropertyKeys.TargetSelector, Value = RuntimeBuffTargetSelectors.Caster },
                            new RuntimeProperty { Key = RuntimePropertyKeys.BuffId, Value = DashTuning.RecoverBuffId.ToString(CultureInfo.InvariantCulture) },
                            new RuntimeProperty { Key = RuntimePropertyKeys.DurationFrames, Value = DashTuning.RecoverFrames.ToString(CultureInfo.InvariantCulture) },
                            new RuntimeProperty { Key = RuntimePropertyKeys.StackCount, Value = "1" }
                        }
                    }
                },
                Connections = new List<RuntimeConnection>
                {
                    new RuntimeConnection { FromNodeId = 0, FromPort = "Next", ToNodeId = 1 },
                    new RuntimeConnection { FromNodeId = 1, FromPort = "Out", ToNodeId = 2 },
                    new RuntimeConnection { FromNodeId = 2, FromPort = "Out", ToNodeId = 3 }
                }
            };
        }

        public static RuntimeSkillGraph CreateSelfBuffGraph(
            int skillId,
            int buffId,
            int durationFrames,
            int stackCount)
        {
            return new RuntimeSkillGraph
            {
                Version = RuntimeSkillGraph.CurrentVersion,
                SkillName = skillId.ToString(CultureInfo.InvariantCulture),
                SyncMode = RuntimeSyncModes.Lockstep,
                DeterministicFlags = CreateLockstepDeterministicFlags(),
                Nodes = new List<RuntimeSkillNode>
                {
                    new RuntimeSkillNode
                    {
                        NodeId = 0,
                        NodeType = RuntimeNodeTypes.Entry
                    },
                    new RuntimeSkillNode
                    {
                        NodeId = 1,
                        NodeType = RuntimeNodeTypes.ApplyBuff,
                        Properties = new List<RuntimeProperty>
                        {
                            new RuntimeProperty
                            {
                                Key = RuntimePropertyKeys.TargetSelector,
                                Value = RuntimeBuffTargetSelectors.Caster
                            },
                            new RuntimeProperty
                            {
                                Key = RuntimePropertyKeys.BuffId,
                                Value = buffId.ToString(CultureInfo.InvariantCulture)
                            },
                            new RuntimeProperty
                            {
                                Key = RuntimePropertyKeys.DurationFrames,
                                Value = Math.Max(0, durationFrames).ToString(CultureInfo.InvariantCulture)
                            },
                            new RuntimeProperty
                            {
                                Key = RuntimePropertyKeys.StackCount,
                                Value = Math.Max(1, stackCount).ToString(CultureInfo.InvariantCulture)
                            }
                        }
                    }
                },
                Connections = new List<RuntimeConnection>
                {
                    new RuntimeConnection
                    {
                        FromNodeId = 0,
                        FromPort = "Next",
                        ToNodeId = 1
                    }
                }
            };
        }

        private static void LoadDirectoryInto(string directory, IDictionary<int, RuntimeSkillGraph> graphs)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string filePath in files)
            {
                if (!TryGetSkillIdFromPath(filePath, out int skillId))
                {
                    continue;
                }

                RuntimeSkillGraph graph = LoadGraph(filePath);
                graphs[skillId] = graph;
            }
        }

        private static RuntimeSkillGraph LoadGraph(string filePath)
        {
            string json = File.ReadAllText(filePath);
            RuntimeSkillGraph? graph = JsonConvert.DeserializeObject<RuntimeSkillGraph>(json);
            if (graph != null)
            {
                return graph;
            }

            throw new InvalidDataException($"Failed to deserialize runtime skill graph '{filePath}'.");
        }

        private static bool TryGetSkillIdFromPath(string filePath, out int skillId)
        {
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            return int.TryParse(fileNameWithoutExtension, NumberStyles.Integer, CultureInfo.InvariantCulture, out skillId) &&
                   skillId > 0;
        }

        private static IEnumerable<string> EnumerateRuntimeGraphDirectories()
        {
            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string explicitDirectory = Environment.GetEnvironmentVariable(RuntimeGraphDirectoryEnvVar) ?? string.Empty;
            AddDirectoryIfExists(explicitDirectory, directories);

            foreach (string root in EnumerateSearchRoots())
            {
                AddDirectoryIfExists(Path.Combine(root, "Assets", "AssetRaw", "Configs", "SkillGraphs"), directories);
                AddDirectoryIfExists(Path.Combine(root, "UnityProject", "Assets", "AssetRaw", "Configs", "SkillGraphs"), directories);
            }

            return directories;
        }

        private static IEnumerable<string> EnumerateSearchRoots()
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSearchRoots(Directory.GetCurrentDirectory(), roots);
            AddSearchRoots(AppContext.BaseDirectory, roots);
            return roots;
        }

        private static void AddSearchRoots(string path, ISet<string> roots)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(path));
            while (current != null)
            {
                roots.Add(current.FullName);
                current = current.Parent;
            }
        }

        private static void AddDirectoryIfExists(string path, ISet<string> directories)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                directories.Add(fullPath);
            }
        }

        private static List<string> CreateLockstepDeterministicFlags()
        {
            return new List<string>
            {
                "Delay.FrameStep",
                "Action.CommandOnly",
                "Trace.ExecutionEventsV1"
            };
        }

        private static int GetEnvInt(string variableName, int fallbackValue)
        {
            string? rawValue = Environment.GetEnvironmentVariable(variableName);
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)
                ? parsedValue
                : fallbackValue;
        }
    }

    public sealed class BattleSkillGraphRuntime
    {
        private readonly SkillNodeHandlerRegistry _handlerRegistry = new SkillNodeHandlerRegistry();
        private readonly Dictionary<long, ActiveSkillExecution> _activeExecutionsByCasterId = new Dictionary<long, ActiveSkillExecution>();
        private readonly List<QueuedSkillRequest> _queuedRequests = new List<QueuedSkillRequest>();
        private readonly List<long> _sortedCasterIds = new List<long>();
        private readonly IReadOnlyDictionary<int, RuntimeSkillGraph> _graphsBySkillId;
        private readonly IBuffCommandSink _buffCommandSink;
        private readonly ISkillRuntimeServices _runtimeServices;

        public BattleSkillGraphRuntime(
            IBuffCommandSink buffCommandSink,
            IReadOnlyDictionary<int, RuntimeSkillGraph>? graphsBySkillId = null,
            ISkillRuntimeServices? runtimeServices = null)
        {
            _buffCommandSink = buffCommandSink ?? throw new ArgumentNullException(nameof(buffCommandSink));
            _graphsBySkillId = graphsBySkillId ?? new Dictionary<int, RuntimeSkillGraph>();
            _runtimeServices = runtimeServices ?? new NullSkillRuntimeServices();
            SkillHandlers.RegisterDefaults(_handlerRegistry);
        }

        public int PreloadedSkillCount => _graphsBySkillId.Count;
        public int ActiveExecutionCount => _activeExecutionsByCasterId.Count;
        public int QueuedSkillRequestCount => _queuedRequests.Count;

        public bool HasSkill(int skillId)
        {
            return _graphsBySkillId.ContainsKey(skillId);
        }

        public void QueueSkillRequest(long casterId, long targetId, int skillId, uint frameIndex)
        {
            QueueSkillRequest(casterId, targetId, skillId, frameIndex, Fixed64.Zero, Fixed64.Zero);
        }

        public void QueueSkillRequest(
            long casterId,
            long targetId,
            int skillId,
            uint frameIndex,
            Fixed64 directionX,
            Fixed64 directionY)
        {
            if (casterId <= 0 || skillId <= 0)
            {
                return;
            }

            _queuedRequests.Add(new QueuedSkillRequest(
                casterId,
                targetId,
                skillId,
                frameIndex,
                directionX,
                directionY));
        }

        public Dictionary<long, ActiveSkillExecutionSnapshot> CaptureExecutions()
        {
            Dictionary<long, ActiveSkillExecutionSnapshot> snapshots =
                new Dictionary<long, ActiveSkillExecutionSnapshot>();
            _sortedCasterIds.Clear();
            foreach (long casterId in _activeExecutionsByCasterId.Keys)
            {
                _sortedCasterIds.Add(casterId);
            }

            _sortedCasterIds.Sort();
            for (int i = 0; i < _sortedCasterIds.Count; i++)
            {
                long casterId = _sortedCasterIds[i];
                if (!_activeExecutionsByCasterId.TryGetValue(casterId, out ActiveSkillExecution? execution))
                {
                    continue;
                }

                snapshots[casterId] = new ActiveSkillExecutionSnapshot(
                    execution.CasterId,
                    execution.TargetId,
                    execution.SkillId,
                    execution.Runner.GetSnapshot(),
                    execution.DirectionX,
                    execution.DirectionY);
            }

            return snapshots;
        }

        public void RestoreExecutions(
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> snapshots)
        {
            _queuedRequests.Clear();
            _activeExecutionsByCasterId.Clear();
            if (snapshots == null || snapshots.Count == 0)
            {
                return;
            }

            _sortedCasterIds.Clear();
            foreach (long casterId in snapshots.Keys)
            {
                _sortedCasterIds.Add(casterId);
            }

            _sortedCasterIds.Sort();
            for (int i = 0; i < _sortedCasterIds.Count; i++)
            {
                ActiveSkillExecutionSnapshot snapshot = snapshots[_sortedCasterIds[i]];
                if (!_graphsBySkillId.TryGetValue(snapshot.SkillId, out RuntimeSkillGraph? graph) || graph == null ||
                    snapshot.RunnerSnapshot == null)
                {
                    continue;
                }

                QueuedSkillRequest request = new QueuedSkillRequest(
                    snapshot.CasterId,
                    snapshot.TargetId,
                    snapshot.SkillId,
                    unchecked((uint)Math.Max(0, snapshot.RunnerSnapshot.FrameIndex)),
                    snapshot.DirectionX,
                    snapshot.DirectionY);
                SkillContext context = CreateContext(request, graph);
                SkillGraphRunner runner = new SkillGraphRunner(_handlerRegistry);
                SkillGraphRunResult initializeResult = runner.Initialize(
                    graph,
                    context,
                    CreateRunnerOptions(graph));
                if (!initializeResult.IsRunning)
                {
                    continue;
                }

                SkillGraphRunResult restoreResult = runner.Restore(snapshot.RunnerSnapshot);
                if (restoreResult.Status == SkillExecutionStatus.Failure ||
                    restoreResult.Status == SkillExecutionStatus.Cancelled)
                {
                    continue;
                }

                _activeExecutionsByCasterId[snapshot.CasterId] = new ActiveSkillExecution(
                    snapshot.CasterId,
                    snapshot.TargetId,
                    snapshot.SkillId,
                    snapshot.DirectionX,
                    snapshot.DirectionY,
                    runner);
            }
        }

        public bool HasActiveExecution(long casterId, int skillId = 0)
        {
            return _activeExecutionsByCasterId.TryGetValue(casterId, out ActiveSkillExecution? execution) &&
                   (skillId <= 0 || execution.SkillId == skillId);
        }

        public bool CancelExecution(long casterId, int skillId = 0)
        {
            if (!_activeExecutionsByCasterId.TryGetValue(casterId, out ActiveSkillExecution? execution) ||
                (skillId > 0 && execution.SkillId != skillId))
            {
                return false;
            }

            return _activeExecutionsByCasterId.Remove(casterId);
        }

        public void Step(uint frameIndex)
        {
            StartQueuedExecutions(frameIndex);
            if (_activeExecutionsByCasterId.Count == 0)
            {
                return;
            }

            _sortedCasterIds.Clear();
            foreach (long casterId in _activeExecutionsByCasterId.Keys)
            {
                _sortedCasterIds.Add(casterId);
            }

            _sortedCasterIds.Sort();
            for (int i = 0; i < _sortedCasterIds.Count; i++)
            {
                long casterId = _sortedCasterIds[i];
                if (!_activeExecutionsByCasterId.TryGetValue(casterId, out ActiveSkillExecution? execution))
                {
                    continue;
                }

                SkillGraphRunResult result = execution.Runner.Step(unchecked((int)frameIndex), null).GetAwaiter().GetResult();
                if (!result.IsRunning)
                {
                    _activeExecutionsByCasterId.Remove(casterId);
                }
            }
        }

        public void RemovePlayer(long playerId)
        {
            if (_activeExecutionsByCasterId.Count == 0)
            {
                return;
            }

            _sortedCasterIds.Clear();
            foreach (KeyValuePair<long, ActiveSkillExecution> pair in _activeExecutionsByCasterId)
            {
                if (pair.Key == playerId || pair.Value.TargetId == playerId)
                {
                    _sortedCasterIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < _sortedCasterIds.Count; i++)
            {
                _activeExecutionsByCasterId.Remove(_sortedCasterIds[i]);
            }
        }

        public void Clear()
        {
            _queuedRequests.Clear();
            _activeExecutionsByCasterId.Clear();
            _sortedCasterIds.Clear();
        }

        private void StartQueuedExecutions(uint frameIndex)
        {
            if (_queuedRequests.Count == 0)
            {
                return;
            }

            _queuedRequests.Sort(QueuedSkillRequestComparer.Instance);
            for (int i = 0; i < _queuedRequests.Count; i++)
            {
                QueuedSkillRequest request = _queuedRequests[i];
                if (request.FrameIndex > frameIndex)
                {
                    continue;
                }

                if (!_graphsBySkillId.TryGetValue(request.SkillId, out RuntimeSkillGraph? graph) || graph == null)
                {
                    continue;
                }

                // A caster owns at most one active lockstep execution. In
                // particular this prevents a second Dash from resetting the
                // first runner and re-consuming stamina.
                if (_activeExecutionsByCasterId.ContainsKey(request.CasterId))
                {
                    continue;
                }

                if (request.SkillId == DashTuning.DashSkillId &&
                    (_buffCommandSink.HasBuff(request.CasterId, DashTuning.DashBuffId) ||
                     _buffCommandSink.HasBuff(request.CasterId, DashTuning.RecoverBuffId)))
                {
                    continue;
                }

                SkillContext context = CreateContext(request, graph);
                SkillGraphRunner runner = new SkillGraphRunner(_handlerRegistry);
                SkillGraphRunResult initializeResult = runner.Initialize(
                    graph,
                    context,
                    CreateRunnerOptions(graph));
                if (initializeResult.IsRunning)
                {
                    _activeExecutionsByCasterId[request.CasterId] = new ActiveSkillExecution(
                        request.CasterId,
                        request.TargetId,
                        request.SkillId,
                        request.DirectionX,
                        request.DirectionY,
                        runner);
                }
            }

            _queuedRequests.Clear();
        }

        private SkillContext CreateContext(QueuedSkillRequest request, RuntimeSkillGraph graph)
        {
            return new SkillContext
            {
                CasterId = request.CasterId,
                TargetId = request.TargetId,
                SkillId = request.SkillId,
                DirectionX = request.DirectionX,
                DirectionY = request.DirectionY,
                MaxExecutionSteps = ResolveExecutionStepLimit(graph),
                Runtime = _runtimeServices,
                BuffCommandSink = _buffCommandSink
            };
        }

        private static SkillGraphRunnerOptions CreateRunnerOptions(RuntimeSkillGraph graph)
        {
            int maxExecutionSteps = ResolveExecutionStepLimit(graph);
            return new SkillGraphRunnerOptions
            {
                MaxNodesPerStep = maxExecutionSteps,
                MaxExecutionSteps = maxExecutionSteps,
                StepDeltaSeconds = DeterminismRules.FixedDeltaTime,
                UseLegacyAsyncNodesInLocalOnly = false,
                EnableTrace = true
            };
        }

        private static int ResolveExecutionStepLimit(RuntimeSkillGraph graph)
        {
            int nodeCount = graph?.Nodes?.Count ?? 0;
            return Math.Max(16, nodeCount * 8);
        }

        private sealed class ActiveSkillExecution
        {
            public ActiveSkillExecution(
                long casterId,
                long targetId,
                int skillId,
                Fixed64 directionX,
                Fixed64 directionY,
                SkillGraphRunner runner)
            {
                CasterId = casterId;
                TargetId = targetId;
                SkillId = skillId;
                DirectionX = directionX;
                DirectionY = directionY;
                Runner = runner ?? throw new ArgumentNullException(nameof(runner));
            }

            public long CasterId { get; }
            public long TargetId { get; }
            public int SkillId { get; }
            public Fixed64 DirectionX { get; }
            public Fixed64 DirectionY { get; }

            public SkillGraphRunner Runner { get; }
        }

        private readonly struct QueuedSkillRequest
        {
            public QueuedSkillRequest(
                long casterId,
                long targetId,
                int skillId,
                uint frameIndex,
                Fixed64 directionX,
                Fixed64 directionY)
            {
                CasterId = casterId;
                TargetId = targetId;
                SkillId = skillId;
                FrameIndex = frameIndex;
                DirectionX = directionX;
                DirectionY = directionY;
            }

            public long CasterId { get; }

            public long TargetId { get; }

            public int SkillId { get; }

            public uint FrameIndex { get; }
            public Fixed64 DirectionX { get; }
            public Fixed64 DirectionY { get; }
        }

        private sealed class QueuedSkillRequestComparer : IComparer<QueuedSkillRequest>
        {
            public static readonly QueuedSkillRequestComparer Instance = new QueuedSkillRequestComparer();

            public int Compare(QueuedSkillRequest left, QueuedSkillRequest right)
            {
                int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
                if (byFrame != 0)
                {
                    return byFrame;
                }

                int byCaster = left.CasterId.CompareTo(right.CasterId);
                if (byCaster != 0)
                {
                    return byCaster;
                }

                return left.SkillId.CompareTo(right.SkillId);
            }
        }
    }
}
