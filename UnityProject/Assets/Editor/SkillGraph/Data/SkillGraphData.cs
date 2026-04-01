using System;
using System.Collections.Generic;

namespace TEngine.Editor.SkillGraph
{
    [Serializable]
    internal sealed class SkillGraphData
    {
        public string graphName;
        public List<SkillNodeData> nodes = new List<SkillNodeData>();
        public List<SkillEdgeData> edges = new List<SkillEdgeData>();
    }
}
