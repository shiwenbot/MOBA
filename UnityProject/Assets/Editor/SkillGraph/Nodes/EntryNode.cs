namespace TEngine.Editor.SkillGraph
{
    internal sealed class EntryNode : SkillGraphNode
    {
        public EntryNode()
            : base(SkillNodeType.Entry, "Entry")
        {
            AddFlowOutput("Next");
        }
    }
}
