using System;

namespace TEngine.Editor.SkillGraph
{
    internal enum SkillNodeType
    {
        Entry,
        Debug,
        Action,
        Condition,
        Branch,
        SetVariable,
        Delay
    }

    internal enum SkillActionType
    {
        PlayAnimation,
        SpawnEffect,
        ApplyDamage,
        Custom
    }

    internal enum SkillBlackboardValueType
    {
        String,
        Float,
        Int,
        Bool
    }

    internal enum SkillConditionOperator
    {
        Exists,
        Equal,
        NotEqual,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
        IsTrue,
        IsFalse
    }

    internal static class SkillNodeTypeUtility
    {
        public static string ToTypeName(SkillNodeType nodeType) => nodeType.ToString();

        public static bool TryParse(string value, out SkillNodeType nodeType) =>
            Enum.TryParse(value, true, out nodeType);
    }
}
