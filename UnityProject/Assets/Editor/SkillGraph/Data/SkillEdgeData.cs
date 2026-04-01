using System;

namespace TEngine.Editor.SkillGraph
{
    [Serializable]
    internal sealed class SkillEdgeData
    {
        public string outputNodeGuid;
        public string outputPortName;
        public string inputNodeGuid;
        public string inputPortName;
    }
}
