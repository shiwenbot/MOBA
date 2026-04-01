using System;
using System.Collections.Generic;
using UnityEngine;

namespace TEngine.Editor.SkillGraph
{
    [Serializable]
    internal sealed class SkillNodeData
    {
        public string guid;
        public string title;
        public string nodeType;
        public Vector2 position;
        public List<SkillNodePropertyData> properties = new List<SkillNodePropertyData>();
    }
}
