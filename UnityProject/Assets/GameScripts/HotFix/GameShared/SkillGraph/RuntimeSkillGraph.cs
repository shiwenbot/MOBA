using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace GameShared.SkillGraph
{
    public static class RuntimeNodeTypes
    {
        public const string Entry = "Entry";
        public const string Debug = "Debug";
        public const string Delay = "Delay";
    }

    public sealed class RuntimeSkillGraph
    {
        [JsonProperty("skillName")]
        public string SkillName { get; set; } = string.Empty;

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
}
