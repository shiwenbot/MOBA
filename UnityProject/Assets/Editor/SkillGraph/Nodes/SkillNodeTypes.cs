using System;

namespace TEngine.Editor.SkillGraph
{
    internal enum SkillNodeType
    {
        Entry,
        Action,
        Condition,
        Delay
    }

    internal enum SkillActionType
    {
        PlayAnimation,
        SpawnEffect,
        ApplyDamage,
        Custom
    }

    internal enum SkillConditionType
    {
        AlwaysTrue,
        TargetInRange,
        ResourceEnough,
        Custom
    }

    internal static class SkillNodeTypeUtility
    {
        public static string ToTypeName(SkillNodeType nodeType) => nodeType.ToString();

        public static bool TryParse(string value, out SkillNodeType nodeType) =>
            Enum.TryParse(value, true, out nodeType);
    }
}
