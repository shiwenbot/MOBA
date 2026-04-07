using UnityEngine;

namespace TEngine.Editor.SkillGraph
{
    internal static class SkillGraphNodeFactory
    {
        public static SkillGraphNode CreateNode(SkillNodeType nodeType, Vector2 position)
        {
            SkillGraphNode node = nodeType switch
            {
                SkillNodeType.Entry => new EntryNode(),
                SkillNodeType.Debug => new DebugNode(),
                SkillNodeType.Action => new ActionNode(),
                SkillNodeType.Condition => new ConditionNode(),
                SkillNodeType.Delay => new DelayNode(),
                _ => new ActionNode()
            };

            node.SetPosition(new Rect(position, SkillGraphNode.DefaultSize));
            return node;
        }

        public static SkillGraphNode CreateNode(string nodeType, Vector2 position)
        {
            if (!SkillNodeTypeUtility.TryParse(nodeType, out SkillNodeType parsedNodeType))
                parsedNodeType = SkillNodeType.Action;

            return CreateNode(parsedNodeType, position);
        }

        public static SkillGraphNode CreateNode(SkillNodeData nodeData)
        {
            SkillGraphNode node = CreateNode(nodeData?.nodeType, nodeData?.position ?? Vector2.zero);
            node.ApplyNodeData(nodeData);
            return node;
        }
    }
}
