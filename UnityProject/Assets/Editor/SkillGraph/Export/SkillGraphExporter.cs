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
                SkillName = skillName,
                Nodes = new List<RuntimeSkillNode>(nodes.Count),
                Connections = new List<RuntimeConnection>()
            };

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

            Dictionary<string, RuntimeConnection> outgoingPorts = new Dictionary<string, RuntimeConnection>(StringComparer.Ordinal);
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

                string fromPort = edgeData.outputPortName ?? string.Empty;
                string connectionKey = $"{fromNodeId}:{fromPort}";
                if (outgoingPorts.ContainsKey(connectionKey))
                {
                    errorMessage = $"Node {fromNodeId} port '{fromPort}' has multiple outgoing connections. v0.3 only supports a single sequential chain.";
                    return false;
                }

                RuntimeConnection connection = new RuntimeConnection
                {
                    FromNodeId = fromNodeId,
                    FromPort = fromPort,
                    ToNodeId = toNodeId
                };

                outgoingPorts.Add(connectionKey, connection);
                runtimeGraph.Connections.Add(connection);
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
                {
                    string message = node.GetPropertyValue("message");
                    if (!string.IsNullOrWhiteSpace(message))
                        return true;

                    errorMessage = $"Debug node {node.NodeId} message cannot be empty.";
                    return false;
                }
                case RuntimeNodeTypes.Delay:
                {
                    string duration = node.GetPropertyValue("duration");
                    if (float.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        return true;

                    errorMessage = $"Delay node {node.NodeId} duration '{duration}' is invalid.";
                    return false;
                }
                case RuntimeNodeTypes.Action:
                {
                    string actionType = node.GetPropertyValue(RuntimePropertyKeys.ActionType);
                    if (!string.Equals(actionType, RuntimeActionTypes.PlayAnimation, StringComparison.OrdinalIgnoreCase))
                    {
                        errorMessage = $"Action node {node.NodeId} actionType '{actionType}' is not supported in v0.4.";
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
                        return true;

                    errorMessage = $"Action node {node.NodeId} speed '{speedValue}' is invalid.";
                    return false;
                }
                default:
                    errorMessage = $"Unsupported runtime node type '{node.NodeType}'.";
                    return false;
            }
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

        private static bool IsSupportedRuntimeNode(string nodeType) =>
            string.Equals(nodeType, RuntimeNodeTypes.Entry, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Debug, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Action, StringComparison.Ordinal) ||
            string.Equals(nodeType, RuntimeNodeTypes.Delay, StringComparison.Ordinal);

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

            if (string.Equals(nodeType, SkillNodeType.Delay.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Delay;

            return nodeType.Trim();
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
                errorMessage =
                    $"PlayAnimation prefab '{prefabAssetPath}' must contain {nameof(SkillGraphAnimancerPlayer)}.";
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
    }
}
