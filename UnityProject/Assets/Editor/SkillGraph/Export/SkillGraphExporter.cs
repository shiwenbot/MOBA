using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameShared.SkillGraph;
using Newtonsoft.Json;
using UnityEditor;

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
                    Properties = CopyProperties(nodeData.properties)
                };

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
                default:
                    errorMessage = $"Unsupported runtime node type '{node.NodeType}'.";
                    return false;
            }
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
            string.Equals(nodeType, RuntimeNodeTypes.Delay, StringComparison.Ordinal);

        private static string NormalizeNodeType(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType))
                return string.Empty;

            if (string.Equals(nodeType, SkillNodeType.Debug.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Debug;

            if (string.Equals(nodeType, SkillNodeType.Entry.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Entry;

            if (string.Equals(nodeType, SkillNodeType.Delay.ToString(), StringComparison.OrdinalIgnoreCase))
                return RuntimeNodeTypes.Delay;

            return nodeType.Trim();
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
