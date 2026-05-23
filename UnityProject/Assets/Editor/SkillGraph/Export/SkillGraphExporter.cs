using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameLogic;
using GameShared.SkillGraph;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor.SkillGraph
{
    internal static class SkillGraphExporter
    {
        private static readonly string[] LockstepDeterministicFlags =
        {
            "Delay.FrameStep",
            "Action.CommandOnly",
            "Trace.ExecutionEventsV1"
        };

        public static bool Export(SkillGraphData graphData, out string exportPath, out string errorMessage)
        {
            exportPath = string.Empty;
            errorMessage = string.Empty;

            if (graphData == null)
            {
                errorMessage = "Skill graph data is null.";
                return false;
            }

            if (!TryBuildRuntimeGraph(graphData, out RuntimeSkillGraph runtimeGraph, out errorMessage))
                return false;

            SkillGraphPaths.EnsureRuntimeGraphExportDirectory();
            exportPath = SkillGraphPaths.NormalizePath(Path.Combine(
                SkillGraphPaths.RuntimeGraphExportDirectory,
                $"{runtimeGraph.SkillName}.json"));

            string absolutePath = SkillGraphPaths.ToAbsolutePath(exportPath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonConvert.SerializeObject(runtimeGraph, Formatting.Indented);
            File.WriteAllText(absolutePath, json, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            return true;
        }

        internal static IReadOnlyList<string> CollectLockstepRiskMessages(SkillGraphData graphData)
        {
            if (graphData == null)
                return new[] { "Skill graph data is null." };

            if (!string.Equals(NormalizeSyncMode(graphData.syncMode), RuntimeSyncModes.Lockstep, StringComparison.Ordinal))
                return Array.Empty<string>();

            if (!TryBuildRuntimeGraph(graphData, out RuntimeSkillGraph runtimeGraph, out string errorMessage))
                return new[] { errorMessage };

            return AnalyzeLockstepRisks(runtimeGraph);
        }

        private static bool TryBuildRuntimeGraph(
            SkillGraphData graphData,
            out RuntimeSkillGraph runtimeGraph,
            out string errorMessage)
        {
            runtimeGraph = null;
            errorMessage = string.Empty;

            List<SkillNodeData> nodes = graphData.nodes ?? new List<SkillNodeData>();
            if (nodes.Count == 0)
            {
                errorMessage = "Skill graph is empty.";
                return false;
            }

            string skillName = string.IsNullOrWhiteSpace(graphData.graphName)
                ? "NewSkillGraph"
                : graphData.graphName.Trim();

            runtimeGraph = new RuntimeSkillGraph
            {
                Version = RuntimeSkillGraph.CurrentVersion,
                SkillName = skillName,
                SyncMode = NormalizeSyncMode(graphData.syncMode),
                DeterministicFlags = new List<string>(),
                Variables = new List<RuntimeVariableDef>(),
                Nodes = new List<RuntimeSkillNode>(nodes.Count),
                Connections = new List<RuntimeConnection>()
            };

            if (!TryBuildRuntimeVariables(graphData, runtimeGraph, out errorMessage))
                return false;

            Dictionary<string, int> nodeIdsByGuid = new Dictionary<string, int>(StringComparer.Ordinal);
            int entryNodeCount = 0;
            for (int index = 0; index < nodes.Count; index++)
            {
                SkillNodeData nodeData = nodes[index];
                if (nodeData == null)
                {
                    errorMessage = $"Node at index {index} is null.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(nodeData.guid))
                {
                    errorMessage = $"Node '{GetNodeLabel(nodeData, index)}' is missing guid.";
                    return false;
                }

                if (nodeIdsByGuid.ContainsKey(nodeData.guid))
                {
                    errorMessage = $"Duplicate node guid found on '{GetNodeLabel(nodeData, index)}'.";
                    return false;
                }

                string nodeType = NormalizeNodeType(nodeData.nodeType);
                if (!IsSupportedRuntimeNode(nodeType))
                {
                    errorMessage = $"Node '{GetNodeLabel(nodeData, index)}' uses unsupported runtime type '{nodeData.nodeType}'.";
                    return false;
                }

                if (string.Equals(nodeType, RuntimeNodeTypes.Entry, StringComparison.Ordinal))
                    entryNodeCount++;

                RuntimeSkillNode runtimeNode = new RuntimeSkillNode
                {
                    NodeId = index,
                    NodeType = nodeType,
                    Properties = BuildRuntimeProperties(nodeData, nodeType, out errorMessage)
                };

                if (!string.IsNullOrEmpty(errorMessage))
                    return false;

                if (!ValidateNode(runtimeNode, out errorMessage))
                    return false;

                nodeIdsByGuid.Add(nodeData.guid, runtimeNode.NodeId);
                runtimeGraph.Nodes.Add(runtimeNode);
            }

            if (entryNodeCount != 1)
            {
                errorMessage = $"Runtime graph must contain exactly one Entry node, but found {entryNodeCount}.";
                return false;
            }

            if (!TryBuildConnections(graphData, runtimeGraph, nodeIdsByGuid, out errorMessage))
                return false;

            if (!ValidateRequiredConnections(runtimeGraph, out errorMessage))
                return false;

            if (!ValidateLockstepSafety(runtimeGraph, out errorMessage))
                return false;

            runtimeGraph.DeterministicFlags = BuildDeterministicFlags(runtimeGraph.SyncMode);

            return true;
        }

        private static bool TryBuildRuntimeVariables(
            SkillGraphData graphData,
            RuntimeSkillGraph runtimeGraph,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            runtimeGraph.Variables.Clear();

            HashSet<string> variableNames = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<SkillVariableDef> variables = graphData.variables ?? new List<SkillVariableDef>();
            for (int index = 0; index < variables.Count; index++)
            {
                SkillVariableDef variable = variables[index];
                if (variable == null)
                    continue;

                string variableName = (variable.name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    errorMessage = $"Variable at index {index} has an empty name.";
                    return false;
                }

                if (!variableNames.Add(variableName))
                {
                    errorMessage = $"Variable '{variableName}' is duplicated.";
                    return false;
                }

                string valueType = NormalizeVariableValueType(variable.type);
                string defaultValue = variable.defaultValue ?? string.Empty;
                if (!TryValidateRawValue(valueType, defaultValue, out string variableError))
                {
                    errorMessage = $"Variable '{variableName}' defaultValue is invalid: {variableError}";
                    return false;
                }

                runtimeGraph.Variables.Add(new RuntimeVariableDef
                {
                    Name = variableName,
                    ValueType = valueType,
                    DefaultValue = defaultValue
                });
            }

            return true;
        }

        private static bool TryBuildConnections(
            SkillGraphData graphData,
            RuntimeSkillGraph runtimeGraph,
            IReadOnlyDictionary<string, int> nodeIdsByGuid,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            Dictionary<string, RuntimeConnection> outgoingPorts = new Dictionary<string, RuntimeConnection>(StringComparer.Ordinal);
            Dictionary<string, RuntimeConnection> incomingPorts = new Dictionary<string, RuntimeConnection>(StringComparer.Ordinal);

            foreach (SkillEdgeData edgeData in graphData.edges ?? new List<SkillEdgeData>())
            {
                if (edgeData == null)
                    continue;

                if (!nodeIdsByGuid.TryGetValue(edgeData.outputNodeGuid ?? string.Empty, out int fromNodeId) ||
                    !nodeIdsByGuid.TryGetValue(edgeData.inputNodeGuid ?? string.Empty, out int toNodeId))
                {
                    errorMessage = "Found edge referencing a missing node.";
                    return false;
                }

                RuntimeSkillNode fromNode = runtimeGraph.GetNode(fromNodeId);
                RuntimeSkillNode toNode = runtimeGraph.GetNode(toNodeId);
                if (fromNode == null || toNode == null)
                {
                    errorMessage = "Failed to resolve nodes for an exported connection.";
                    return false;
                }

                string fromPort = edgeData.outputPortName ?? string.Empty;
                string inputPort = edgeData.inputPortName ?? string.Empty;
                if (!IsValidOutputPort(fromNode.NodeType, fromPort))
                {
                    errorMessage = $"Node {fromNodeId} type '{fromNode.NodeType}' does not define output port '{fromPort}'.";
                    return false;
                }

                if (!IsValidInputPort(toNode.NodeType, inputPort))
                {
                    errorMessage = $"Node {toNodeId} type '{toNode.NodeType}' does not define input port '{inputPort}'.";
                    return false;
                }

                string outgoingKey = $"{fromNodeId}:{fromPort}";
                if (outgoingPorts.ContainsKey(outgoingKey))
                {
                    errorMessage = $"Node {fromNodeId} port '{fromPort}' has multiple outgoing connections.";
                    return false;
                }

                string incomingKey = $"{toNodeId}:{inputPort}";
                if (incomingPorts.ContainsKey(incomingKey))
                {
                    errorMessage = $"Node {toNodeId} port '{inputPort}' has multiple incoming connections.";
                    return false;
                }

                RuntimeConnection connection = new RuntimeConnection
                {
                    FromNodeId = fromNodeId,
                    FromPort = fromPort,
                    ToNodeId = toNodeId
                };

                outgoingPorts.Add(outgoingKey, connection);
                incomingPorts.Add(incomingKey, connection);
                runtimeGraph.Connections.Add(connection);
            }

            return true;
        }

        private static bool ValidateRequiredConnections(RuntimeSkillGraph runtimeGraph, out string errorMessage)
        {
            errorMessage = string.Empty;

            foreach (RuntimeSkillNode node in runtimeGraph.Nodes)
            {
                if (node == null)
                    continue;

                if (string.Equals(node.NodeType, RuntimeNodeTypes.Entry, StringComparison.Ordinal) &&
                    runtimeGraph.GetNextConnections(node.NodeId, "Next").Count == 0)
                {
                    errorMessage = $"Entry node {node.NodeId} must connect to a Next node.";
                    return false;
                }

                if (string.Equals(node.NodeType, RuntimeNodeTypes.Condition, StringComparison.Ordinal) ||
                    string.Equals(node.NodeType, RuntimeNodeTypes.Branch, StringComparison.Ordinal) ||
                    string.Equals(node.NodeType, RuntimeNodeTypes.BuffCondition, StringComparison.Ordinal))
                {
                    if (runtimeGraph.GetNextConnections(node.NodeId, "True").Count == 0 ||
                        runtimeGraph.GetNextConnections(node.NodeId, "False").Count == 0)
                    {
                        errorMessage = $"{node.NodeType} node {node.NodeId} must connect both True and False outputs.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool ValidateNode(RuntimeSkillNode node, out string errorMessage)
        {
            errorMessage = string.Empty;

            switch (node.NodeType)
            {
                case RuntimeNodeTypes.Entry:
                    return true;
                case RuntimeNodeTypes.Debug:
                    return ValidateDebugNode(node, out errorMessage);
                case RuntimeNodeTypes.Delay:
                    return ValidateDelayNode(node, out errorMessage);
                case RuntimeNodeTypes.Action:
                    return ValidateActionNode(node, out errorMessage);
                case RuntimeNodeTypes.Condition:
                    return ValidateConditionNode(node, out errorMessage);
                case RuntimeNodeTypes.Branch:
                    return ValidateBranchNode(node, out errorMessage);
                case RuntimeNodeTypes.SetVariable:
                    return ValidateSetVariableNode(node, out errorMessage);
                case RuntimeNodeTypes.ApplyBuff:
                    return ValidateApplyBuffNode(node, out errorMessage);
                case RuntimeNodeTypes.RemoveBuff:
                    return ValidateRemoveBuffNode(node, out errorMessage);
                case RuntimeNodeTypes.BuffCondition:
                    return ValidateBuffConditionNode(node, out errorMessage);
                default:
                    errorMessage = $"Unsupported runtime node type '{node.NodeType}'.";
                    return false;
            }
        }

        private static bool ValidateDebugNode(RuntimeSkillNode node, out string errorMessage)
        {
            string message = node.GetPropertyValue("message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"Debug node {node.NodeId} message cannot be empty.";
            return false;
        }

        private static bool ValidateDelayNode(RuntimeSkillNode node, out string errorMessage)
        {
            string duration = node.GetPropertyValue(RuntimePropertyKeys.Duration);
            if (float.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out float durationSeconds) &&
                !float.IsNaN(durationSeconds) &&
                !float.IsInfinity(durationSeconds) &&
                durationSeconds >= 0f)
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"Delay node {node.NodeId} duration '{duration}' is invalid. Expected a non-negative finite number.";
            return false;
        }

        private static bool ValidateActionNode(RuntimeSkillNode node, out string errorMessage)
        {
            string actionType = node.GetPropertyValue(RuntimePropertyKeys.ActionType);
            if (!string.Equals(actionType, RuntimeActionTypes.PlayAnimation, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Action node {node.NodeId} actionType '{actionType}' is not supported in v0.8.";
                return false;
            }

            string prefabLocation = node.GetPropertyValue(RuntimePropertyKeys.PrefabLocation);
            if (string.IsNullOrWhiteSpace(prefabLocation))
            {
                errorMessage = $"Action node {node.NodeId} prefabLocation cannot be empty.";
                return false;
            }

            string speedValue = node.GetPropertyValue(RuntimePropertyKeys.Value, "1");
            if (float.TryParse(speedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"Action node {node.NodeId} speed '{speedValue}' is invalid.";
            return false;
        }

        private static bool ValidateConditionNode(RuntimeSkillNode node, out string errorMessage)
        {
            string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                errorMessage = $"Condition node {node.NodeId} key cannot be empty.";
                return false;
            }

            string conditionOperator = node.GetPropertyValue(RuntimePropertyKeys.Operator, RuntimeConditionOperators.Exists);
            if (!IsSupportedConditionOperator(conditionOperator))
            {
                errorMessage = $"Condition node {node.NodeId} operator '{conditionOperator}' is invalid.";
                return false;
            }

            string valueType = node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool);
            if (!IsSupportedValueType(valueType))
            {
                errorMessage = $"Condition node {node.NodeId} valueType '{valueType}' is invalid.";
                return false;
            }

            if (string.Equals(conditionOperator, RuntimeConditionOperators.Exists, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = string.Empty;
                return true;
            }

            if (string.Equals(conditionOperator, RuntimeConditionOperators.IsTrue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(conditionOperator, RuntimeConditionOperators.IsFalse, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(valueType, RuntimeValueTypes.Bool, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = string.Empty;
                    return true;
                }

                errorMessage = $"Condition node {node.NodeId} operator '{conditionOperator}' requires Bool valueType.";
                return false;
            }

            string rawValue = node.GetPropertyValue(RuntimePropertyKeys.Value);
            if (string.IsNullOrWhiteSpace(rawValue) && !string.Equals(valueType, RuntimeValueTypes.String, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Condition node {node.NodeId} compare value cannot be empty.";
                return false;
            }

            if ((string.Equals(conditionOperator, RuntimeConditionOperators.Greater, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(conditionOperator, RuntimeConditionOperators.GreaterOrEqual, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(conditionOperator, RuntimeConditionOperators.Less, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(conditionOperator, RuntimeConditionOperators.LessOrEqual, StringComparison.OrdinalIgnoreCase)) &&
                !IsNumericValueType(valueType))
            {
                errorMessage = $"Condition node {node.NodeId} numeric operator '{conditionOperator}' requires Int or Float valueType.";
                return false;
            }

            if (TryValidateRawValue(valueType, rawValue, out errorMessage))
                return true;

            errorMessage = $"Condition node {node.NodeId} compare value is invalid: {errorMessage}";
            return false;
        }

        private static bool ValidateBranchNode(RuntimeSkillNode node, out string errorMessage)
        {
            string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                errorMessage = $"Branch node {node.NodeId} key cannot be empty.";
                return false;
            }

            string valueType = node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool);
            if (string.Equals(valueType, RuntimeValueTypes.Bool, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"Branch node {node.NodeId} only supports Bool valueType, but got '{valueType}'.";
            return false;
        }

        private static bool ValidateSetVariableNode(RuntimeSkillNode node, out string errorMessage)
        {
            string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                errorMessage = $"SetVariable node {node.NodeId} key cannot be empty.";
                return false;
            }

            string valueType = node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.String);
            if (!IsSupportedValueType(valueType))
            {
                errorMessage = $"SetVariable node {node.NodeId} valueType '{valueType}' is invalid.";
                return false;
            }

            string rawValue = node.GetPropertyValue(RuntimePropertyKeys.Value);
            if (TryValidateRawValue(valueType, rawValue, out errorMessage))
                return true;

            errorMessage = $"SetVariable node {node.NodeId} value is invalid: {errorMessage}";
            return false;
        }

        private static bool ValidateApplyBuffNode(RuntimeSkillNode node, out string errorMessage)
        {
            if (!TryValidateBuffTargetSelector(node, out errorMessage))
            {
                return false;
            }

            if (!TryParsePositiveInt(node.GetPropertyValue(RuntimePropertyKeys.BuffId), out _))
            {
                errorMessage = $"ApplyBuff node {node.NodeId} buffId must be a positive integer.";
                return false;
            }

            if (!TryParseNonNegativeInt(node.GetPropertyValue(RuntimePropertyKeys.DurationFrames, "0"), out _))
            {
                errorMessage = $"ApplyBuff node {node.NodeId} durationFrames must be a non-negative integer.";
                return false;
            }

            if (!TryParsePositiveInt(node.GetPropertyValue(RuntimePropertyKeys.StackCount, "1"), out _))
            {
                errorMessage = $"ApplyBuff node {node.NodeId} stackCount must be a positive integer.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool ValidateRemoveBuffNode(RuntimeSkillNode node, out string errorMessage)
        {
            if (!TryValidateBuffTargetSelector(node, out errorMessage))
            {
                return false;
            }

            if (!TryParsePositiveInt(node.GetPropertyValue(RuntimePropertyKeys.BuffId), out _))
            {
                errorMessage = $"RemoveBuff node {node.NodeId} buffId must be a positive integer.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool ValidateBuffConditionNode(RuntimeSkillNode node, out string errorMessage)
        {
            if (!TryValidateBuffTargetSelector(node, out errorMessage))
            {
                return false;
            }

            if (!TryParsePositiveInt(node.GetPropertyValue(RuntimePropertyKeys.BuffId), out _))
            {
                errorMessage = $"BuffCondition node {node.NodeId} buffId must be a positive integer.";
                return false;
            }

            if (!TryParsePositiveInt(node.GetPropertyValue(RuntimePropertyKeys.MinimumStackCount, "1"), out _))
            {
                errorMessage = $"BuffCondition node {node.NodeId} minimumStackCount must be a positive integer.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static List<RuntimeProperty> BuildRuntimeProperties(
            SkillNodeData nodeData,
            string nodeType,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!string.Equals(nodeType, RuntimeNodeTypes.Action, StringComparison.Ordinal))
                return CopyProperties(nodeData.properties);

            List<RuntimeProperty> result = new List<RuntimeProperty>();
            foreach (SkillNodePropertyData property in nodeData.properties ?? new List<SkillNodePropertyData>())
            {
                if (property == null || string.IsNullOrEmpty(property.key))
                    continue;

                if (string.Equals(property.key, RuntimePropertyKeys.PrefabAssetPath, StringComparison.Ordinal))
                {
                    if (!TryConvertPrefabAssetPathToLocation(property.value, out string prefabLocation, out errorMessage))
                        return new List<RuntimeProperty>();

                    result.Add(new RuntimeProperty
                    {
                        Key = RuntimePropertyKeys.PrefabLocation,
                        Value = prefabLocation
                    });
                    continue;
                }

                result.Add(new RuntimeProperty
                {
                    Key = property.key,
                    Value = property.value ?? string.Empty
                });
            }

            return result;
        }

        private static List<RuntimeProperty> CopyProperties(IReadOnlyList<SkillNodePropertyData> properties)
        {
            List<RuntimeProperty> result = new List<RuntimeProperty>();
            if (properties == null)
                return result;

            foreach (SkillNodePropertyData property in properties)
            {
                if (property == null || string.IsNullOrEmpty(property.key))
                    continue;

                result.Add(new RuntimeProperty
                {
                    Key = property.key,
                    Value = property.value ?? string.Empty
                });
            }

            return result;
        }

        private static bool TryValidateRawValue(string valueType, string rawValue, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.Equals(valueType, RuntimeValueTypes.String, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(valueType, RuntimeValueTypes.Float, StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return true;

                errorMessage = $"'{rawValue}' is not a valid Float.";
                return false;
            }

            if (string.Equals(valueType, RuntimeValueTypes.Int, StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    return true;

                errorMessage = $"'{rawValue}' is not a valid Int.";
                return false;
            }

            if (string.Equals(valueType, RuntimeValueTypes.Bool, StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(rawValue, out _))
                    return true;

                errorMessage = $"'{rawValue}' is not a valid Bool.";
                return false;
            }

            errorMessage = $"Unsupported valueType '{valueType}'.";
            return false;
        }

        private static bool TryValidateBuffTargetSelector(RuntimeSkillNode node, out string errorMessage)
        {
            string targetSelector = node.GetPropertyValue(RuntimePropertyKeys.TargetSelector, RuntimeBuffTargetSelectors.Target);
            if (IsSupportedBuffTargetSelector(targetSelector))
            {
                errorMessage = string.Empty;
                return true;
            }

            errorMessage = $"Node {node.NodeId} targetSelector '{targetSelector}' is invalid.";
            return false;
        }

        private static bool TryParsePositiveInt(string rawValue, out int value)
        {
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static bool TryParseNonNegativeInt(string rawValue, out int value)
        {
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static bool IsSupportedRuntimeNode(string nodeType) =>
            string.Equals(nodeType, RuntimeNodeTypes.Entry, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Debug, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Action, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Condition, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Branch, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.SetVariable, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Delay, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.ApplyBuff, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.RemoveBuff, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.BuffCondition, StringComparison.Ordinal);

        private static bool IsSupportedValueType(string valueType) =>
            string.Equals(valueType, RuntimeValueTypes.String, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(valueType, RuntimeValueTypes.Float, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(valueType, RuntimeValueTypes.Int, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(valueType, RuntimeValueTypes.Bool, StringComparison.OrdinalIgnoreCase);

        private static bool IsNumericValueType(string valueType) =>
            string.Equals(valueType, RuntimeValueTypes.Float, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(valueType, RuntimeValueTypes.Int, StringComparison.OrdinalIgnoreCase);

        private static bool IsSupportedConditionOperator(string conditionOperator) =>
            string.Equals(conditionOperator, RuntimeConditionOperators.Exists, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.Equal, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.NotEqual, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.Greater, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.GreaterOrEqual, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.Less, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.LessOrEqual, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.IsTrue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(conditionOperator, RuntimeConditionOperators.IsFalse, StringComparison.OrdinalIgnoreCase);

        private static bool ValidateLockstepSafety(RuntimeSkillGraph runtimeGraph, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!string.Equals(runtimeGraph.SyncMode, RuntimeSyncModes.Lockstep, StringComparison.Ordinal))
                return true;

            List<string> risks = AnalyzeLockstepRisks(runtimeGraph);
            if (risks.Count == 0)
                return true;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Lockstep export validation failed:");
            foreach (string risk in risks)
            {
                builder.Append(" - ");
                builder.AppendLine(risk);
            }

            errorMessage = builder.ToString().TrimEnd();
            return false;
        }

        private static List<string> AnalyzeLockstepRisks(RuntimeSkillGraph runtimeGraph)
        {
            Dictionary<string, string> variableTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (RuntimeVariableDef variable in runtimeGraph.Variables ?? new List<RuntimeVariableDef>())
            {
                if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
                    continue;

                variableTypes[variable.Name] = NormalizeRuntimeValueType(variable.ValueType);
            }

            List<string> risks = new List<string>();
            foreach (RuntimeSkillNode node in runtimeGraph.Nodes ?? new List<RuntimeSkillNode>())
            {
                if (node == null)
                    continue;

                if (string.Equals(node.NodeType, RuntimeNodeTypes.SetVariable, StringComparison.Ordinal))
                {
                    ValidateLockstepSetVariableNode(node, variableTypes, risks);
                    continue;
                }

                if (string.Equals(node.NodeType, RuntimeNodeTypes.Condition, StringComparison.Ordinal))
                {
                    ValidateLockstepConditionNode(node, variableTypes, risks);
                    continue;
                }

                if (string.Equals(node.NodeType, RuntimeNodeTypes.Branch, StringComparison.Ordinal))
                {
                    ValidateLockstepBranchNode(node, variableTypes, risks);
                }
            }

            return risks;
        }

        private static void ValidateLockstepSetVariableNode(
            RuntimeSkillNode node,
            IReadOnlyDictionary<string, string> variableTypes,
            IList<string> risks)
        {
            string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
            if (!TryGetDeclaredVariableType(variableTypes, key, out string declaredType))
            {
                risks.Add(
                    $"SetVariable node {node.NodeId} writes '{key}', but this variable is not declared in blackboard definitions.");
                return;
            }

            string writeType = NormalizeRuntimeValueType(
                node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.String));
            if (!string.Equals(writeType, declaredType, StringComparison.Ordinal))
            {
                risks.Add(
                    $"SetVariable node {node.NodeId} writes '{key}' as {writeType}, but variable is declared as {declaredType}.");
            }
        }

        private static void ValidateLockstepConditionNode(
            RuntimeSkillNode node,
            IReadOnlyDictionary<string, string> variableTypes,
            IList<string> risks)
        {
            string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
            if (!TryGetDeclaredVariableType(variableTypes, key, out string declaredType))
            {
                risks.Add(
                    $"Condition node {node.NodeId} reads '{key}', but this variable is not declared in blackboard definitions.");
                return;
            }

            string conditionOperator = node.GetPropertyValue(RuntimePropertyKeys.Operator, RuntimeConditionOperators.Exists);
            if (string.Equals(conditionOperator, RuntimeConditionOperators.Exists, StringComparison.OrdinalIgnoreCase))
                return;

            string readType = NormalizeRuntimeValueType(
                node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool));
            if (!string.Equals(readType, declaredType, StringComparison.Ordinal))
            {
                risks.Add(
                    $"Condition node {node.NodeId} reads '{key}' as {readType}, but variable is declared as {declaredType}.");
            }
        }

        private static void ValidateLockstepBranchNode(
            RuntimeSkillNode node,
            IReadOnlyDictionary<string, string> variableTypes,
            IList<string> risks)
        {
            string key = node.GetPropertyValue(RuntimePropertyKeys.Key);
            if (!TryGetDeclaredVariableType(variableTypes, key, out string declaredType))
            {
                risks.Add(
                    $"Branch node {node.NodeId} reads '{key}', but this variable is not declared in blackboard definitions.");
                return;
            }

            if (!string.Equals(declaredType, RuntimeValueTypes.Bool, StringComparison.Ordinal))
            {
                risks.Add(
                    $"Branch node {node.NodeId} requires Bool variable '{key}', but it is declared as {declaredType}.");
            }
        }

        private static bool TryGetDeclaredVariableType(
            IReadOnlyDictionary<string, string> variableTypes,
            string key,
            out string declaredType)
        {
            declaredType = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return variableTypes.TryGetValue(key, out declaredType);
        }

        private static string NormalizeRuntimeValueType(string valueType)
        {
            if (string.Equals(valueType, RuntimeValueTypes.Float, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.Float;

            if (string.Equals(valueType, RuntimeValueTypes.Int, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.Int;

            if (string.Equals(valueType, RuntimeValueTypes.Bool, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.Bool;

            return RuntimeValueTypes.String;
        }

        private static List<string> BuildDeterministicFlags(string syncMode)
        {
            if (!string.Equals(syncMode, RuntimeSyncModes.Lockstep, StringComparison.Ordinal))
                return new List<string>();

            return new List<string>(LockstepDeterministicFlags);
        }

        private static string NormalizeSyncMode(string syncMode)
        {
            if (string.Equals(syncMode, RuntimeSyncModes.Lockstep, StringComparison.OrdinalIgnoreCase))
                return RuntimeSyncModes.Lockstep;

            return RuntimeSyncModes.LocalOnly;
        }

        private static string NormalizeVariableValueType(SkillBlackboardValueType variableType)
        {
            switch (variableType)
            {
                case SkillBlackboardValueType.Float:
                    return RuntimeValueTypes.Float;
                case SkillBlackboardValueType.Int:
                    return RuntimeValueTypes.Int;
                case SkillBlackboardValueType.Bool:
                    return RuntimeValueTypes.Bool;
                case SkillBlackboardValueType.String:
                default:
                    return RuntimeValueTypes.String;
            }
        }

        private static string NormalizeNodeType(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType))
                return string.Empty;

            if (string.Equals(nodeType, SkillNodeType.Debug.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Debug;

            if (string.Equals(nodeType, SkillNodeType.Entry.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Entry;

            if (string.Equals(nodeType, SkillNodeType.Action.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Action;

            if (string.Equals(nodeType, SkillNodeType.Condition.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Condition;

            if (string.Equals(nodeType, SkillNodeType.Branch.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Branch;

            if (string.Equals(nodeType, SkillNodeType.SetVariable.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.SetVariable;

            if (string.Equals(nodeType, SkillNodeType.Delay.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Delay;

            if (string.Equals(nodeType, SkillNodeType.ApplyBuff.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.ApplyBuff;

            if (string.Equals(nodeType, SkillNodeType.RemoveBuff.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.RemoveBuff;

            if (string.Equals(nodeType, SkillNodeType.BuffCondition.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.BuffCondition;

            return nodeType.Trim();
        }

        private static bool IsValidInputPort(string nodeType, string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
                return false;

            switch (nodeType)
            {
                case RuntimeNodeTypes.Debug:
                case RuntimeNodeTypes.Action:
                case RuntimeNodeTypes.Condition:
                case RuntimeNodeTypes.Branch:
                case RuntimeNodeTypes.SetVariable:
                case RuntimeNodeTypes.Delay:
                case RuntimeNodeTypes.ApplyBuff:
                case RuntimeNodeTypes.RemoveBuff:
                case RuntimeNodeTypes.BuffCondition:
                    return string.Equals(portName, "In", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static bool IsValidOutputPort(string nodeType, string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
                return false;

            switch (nodeType)
            {
                case RuntimeNodeTypes.Entry:
                    return string.Equals(portName, "Next", StringComparison.Ordinal);
                case RuntimeNodeTypes.Debug:
                case RuntimeNodeTypes.Action:
                case RuntimeNodeTypes.SetVariable:
                case RuntimeNodeTypes.Delay:
                case RuntimeNodeTypes.ApplyBuff:
                case RuntimeNodeTypes.RemoveBuff:
                    return string.Equals(portName, "Out", StringComparison.Ordinal);
                case RuntimeNodeTypes.Condition:
                case RuntimeNodeTypes.Branch:
                case RuntimeNodeTypes.BuffCondition:
                    return string.Equals(portName, "True", StringComparison.Ordinal) ||
                           string.Equals(portName, "False", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static bool TryConvertPrefabAssetPathToLocation(
            string prefabAssetPath,
            out string prefabLocation,
            out string errorMessage)
        {
            prefabLocation = string.Empty;
            errorMessage = string.Empty;

            string normalizedPath = SkillGraphPaths.NormalizePath(prefabAssetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                errorMessage = "PlayAnimation action requires a prefab reference.";
                return false;
            }

            const string assetRoot = "Assets/AssetRaw/";
            if (!normalizedPath.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"PlayAnimation prefab must be under {assetRoot}, but got '{normalizedPath}'.";
                return false;
            }

            if (!TryValidatePlayAnimationPrefab(normalizedPath, out errorMessage))
                return false;

            prefabLocation = Path.GetFileNameWithoutExtension(normalizedPath);
            prefabLocation = SkillGraphPaths.NormalizePath(prefabLocation);
            if (!string.IsNullOrWhiteSpace(prefabLocation))
                return true;

            errorMessage = $"PlayAnimation prefab path '{normalizedPath}' could not be converted to a runtime location.";
            return false;
        }

        private static bool TryValidatePlayAnimationPrefab(string prefabAssetPath, out string errorMessage)
        {
            errorMessage = string.Empty;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
            if (prefab == null)
            {
                errorMessage = $"PlayAnimation prefab '{prefabAssetPath}' could not be loaded.";
                return false;
            }

            SkillGraphAnimancerPlayer player = prefab.GetComponentInChildren<SkillGraphAnimancerPlayer>(true);
            if (player == null)
            {
                errorMessage = $"PlayAnimation prefab '{prefabAssetPath}' must contain {nameof(SkillGraphAnimancerPlayer)}.";
                return false;
            }

            if (!player.HasConfiguredClip)
            {
                errorMessage =
                    $"PlayAnimation prefab '{prefabAssetPath}' must assign an AnimationClip on {nameof(SkillGraphAnimancerPlayer)}.";
                return false;
            }

            return true;
        }

        private static string GetNodeLabel(SkillNodeData nodeData, int index)
        {
            if (!string.IsNullOrWhiteSpace(nodeData.title))
                return nodeData.title;

            if (!string.IsNullOrWhiteSpace(nodeData.nodeType))
                return $"{nodeData.nodeType}#{index}";

            return $"Node#{index}";
        }

        private static bool IsSupportedBuffTargetSelector(string targetSelector) =>
            string.Equals(targetSelector, RuntimeBuffTargetSelectors.Target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetSelector, RuntimeBuffTargetSelectors.Caster, StringComparison.OrdinalIgnoreCase);
    }
}
