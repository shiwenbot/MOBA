using System;
using System.Globalization;
using Fantasy.Async;
using GameShared.FrameSync.Battle;

namespace GameShared.SkillGraph
{
    public static class SkillHandlers
    {
        public static void RegisterDefaults(SkillNodeHandlerRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register(RuntimeNodeTypes.Entry, new EntryNodeHandler());
            registry.Register(RuntimeNodeTypes.Debug, new DebugNodeHandler());
            registry.Register(RuntimeNodeTypes.Action, new ActionNodeHandler());
            registry.Register(RuntimeNodeTypes.Condition, new ConditionNodeHandler());
            registry.Register(RuntimeNodeTypes.Branch, new BranchNodeHandler());
            registry.Register(RuntimeNodeTypes.SetVariable, new SetVariableNodeHandler());
            registry.Register(RuntimeNodeTypes.Delay, new DelayNodeHandler());
            registry.Register(RuntimeNodeTypes.ApplyBuff, new ApplyBuffNodeHandler());
            registry.Register(RuntimeNodeTypes.RemoveBuff, new RemoveBuffNodeHandler());
            registry.Register(RuntimeNodeTypes.BuffCondition, new BuffConditionNodeHandler());
        }
    }

    public sealed class EntryNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context) =>
            FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Next"));
    }

    public sealed class DebugNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            context.Runtime?.Log(node.GetPropertyValue("message"));
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Out"));
        }
    }

    public sealed class DelayNodeHandler : ISkillNodeHandler
    {
        public async FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            if (SkillHandlerUtility.IsCancellationRequested(context))
                return SkillExecuteResult.Cancelled($"Delay node {node.NodeId} was cancelled before it started.");

            float durationSeconds = node.GetFloatPropertyValue(RuntimePropertyKeys.Duration, 0f);
            int delayMilliseconds = Math.Max(0, (int)(durationSeconds * 1000f));
            bool completed = await context.Runtime!.DelayAsync(delayMilliseconds, context.CancellationToken);
            if (!completed)
            {
                context.MarkCancelled();
                return SkillExecuteResult.Cancelled($"Delay node {node.NodeId} was cancelled while waiting.");
            }

            return SkillExecuteResult.Success("Out");
        }
    }

    public sealed class ActionNodeHandler : ISkillNodeHandler
    {
        public async FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            if (SkillHandlerUtility.IsCancellationRequested(context))
                return SkillExecuteResult.Cancelled($"Action node {node.NodeId} was cancelled before it started.");

            string actionType = node.GetPropertyValue(RuntimePropertyKeys.ActionType);
            if (!string.Equals(actionType, RuntimeActionTypes.PlayAnimation, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Action node {node.NodeId} actionType '{actionType}' is not supported in v0.8.");
            }

            string prefabLocation = node.GetPropertyValue(RuntimePropertyKeys.PrefabLocation);
            if (string.IsNullOrWhiteSpace(prefabLocation))
                throw new InvalidOperationException($"Action node {node.NodeId} prefabLocation cannot be empty.");

            float speed = node.GetFloatPropertyValue(RuntimePropertyKeys.Value, 1f);
            bool completed = await context.Runtime!.PlayAnimationAsync(context, prefabLocation, speed, context.CancellationToken);
            if (!completed || SkillHandlerUtility.IsCancellationRequested(context))
            {
                context.MarkCancelled();
                return SkillExecuteResult.Cancelled($"Action node {node.NodeId} was cancelled before completion.");
            }

            return SkillExecuteResult.Success("Out");
        }
    }

    public sealed class ApplyBuffNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            int buffId = SkillHandlerUtility.RequirePositiveIntProperty(node, RuntimePropertyKeys.BuffId);
            int durationFrames = Math.Max(0, node.GetIntPropertyValue(RuntimePropertyKeys.DurationFrames, 0));
            int stackCount = Math.Max(1, node.GetIntPropertyValue(RuntimePropertyKeys.StackCount, 1));
            long targetId = SkillHandlerUtility.ResolveTargetId(node, context);
            if (!context.IsLockstepMode)
            {
                return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Out"));
            }

            IBuffCommandSink buffCommandSink = SkillHandlerUtility.RequireBuffCommandSink(context);
            buffCommandSink.EnqueueApplyBuff(new ApplyBuffCommand
            {
                CasterId = context.CasterId,
                TargetId = targetId,
                BuffId = buffId,
                DurationFrames = durationFrames,
                StackCount = stackCount,
                FrameIndex = SkillHandlerUtility.ResolveFrameIndex(context),
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            });
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Out"));
        }
    }

    public sealed class RemoveBuffNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            int buffId = SkillHandlerUtility.RequirePositiveIntProperty(node, RuntimePropertyKeys.BuffId);
            long targetId = SkillHandlerUtility.ResolveTargetId(node, context);
            if (!context.IsLockstepMode)
            {
                return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Out"));
            }

            IBuffCommandSink buffCommandSink = SkillHandlerUtility.RequireBuffCommandSink(context);
            buffCommandSink.EnqueueRemoveBuff(new RemoveBuffCommand
            {
                TargetId = targetId,
                RuntimeBuffId = 0,
                BuffId = buffId,
                RemoveReason = RemoveReason.Manual,
                FrameIndex = SkillHandlerUtility.ResolveFrameIndex(context)
            });
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Out"));
        }
    }

    public sealed class BuffConditionNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            int buffId = SkillHandlerUtility.RequirePositiveIntProperty(node, RuntimePropertyKeys.BuffId);
            int minimumStackCount = Math.Max(1, node.GetIntPropertyValue(RuntimePropertyKeys.MinimumStackCount, 1));
            long targetId = SkillHandlerUtility.ResolveTargetId(node, context);
            IBuffCommandSink buffCommandSink = SkillHandlerUtility.RequireBuffCommandSink(context);
            bool hasBuff = minimumStackCount <= 1
                ? buffCommandSink.HasBuff(targetId, buffId)
                : buffCommandSink.GetBuffStackCount(targetId, buffId) >= minimumStackCount;
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success(hasBuff ? "True" : "False"));
        }
    }

    public sealed class ConditionNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            SkillBlackboard blackboard = SkillHandlerUtility.EnsureBlackboard(context);

            string key = SkillHandlerUtility.RequireProperty(node, RuntimePropertyKeys.Key);
            string conditionOperator = node.GetPropertyValue(RuntimePropertyKeys.Operator, RuntimeConditionOperators.Exists);
            string valueType = node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.Bool);
            string rawValue = node.GetPropertyValue(RuntimePropertyKeys.Value);

            bool result = SkillBlackboardUtility.EvaluateCondition(
                blackboard,
                key,
                conditionOperator,
                valueType,
                rawValue);

            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success(result ? "True" : "False"));
        }
    }

    public sealed class BranchNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            SkillBlackboard blackboard = SkillHandlerUtility.EnsureBlackboard(context);
            string key = SkillHandlerUtility.RequireProperty(node, RuntimePropertyKeys.Key);
            bool result = SkillBlackboardUtility.GetBool(blackboard, key);
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success(result ? "True" : "False"));
        }
    }

    public sealed class SetVariableNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            SkillBlackboard blackboard = SkillHandlerUtility.EnsureBlackboard(context);

            string key = SkillHandlerUtility.RequireProperty(node, RuntimePropertyKeys.Key);
            string valueType = node.GetPropertyValue(RuntimePropertyKeys.ValueType, RuntimeValueTypes.String);
            string rawValue = node.GetPropertyValue(RuntimePropertyKeys.Value);

            SkillBlackboardUtility.SetValue(blackboard, key, valueType, rawValue);
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Success("Out"));
        }
    }

    internal static class SkillBlackboardUtility
    {
        public static void SetValue(SkillBlackboard blackboard, string key, string valueType, string rawValue)
        {
            switch (NormalizeValueType(valueType))
            {
                case RuntimeValueTypes.String:
                    blackboard.SetString(key, rawValue);
                    return;
                case RuntimeValueTypes.Float:
                    if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                    {
                        blackboard.SetFloat(key, floatValue);
                        return;
                    }

                    break;
                case RuntimeValueTypes.Int:
                    if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                    {
                        blackboard.SetInt(key, intValue);
                        return;
                    }

                    break;
                case RuntimeValueTypes.Bool:
                    if (bool.TryParse(rawValue, out bool boolValue))
                    {
                        blackboard.SetBool(key, boolValue);
                        return;
                    }

                    break;
            }

            throw new InvalidOperationException(
                $"Failed to assign blackboard key '{key}' as type '{valueType}' from raw value '{rawValue}'.");
        }

        public static bool EvaluateCondition(
            SkillBlackboard blackboard,
            string key,
            string conditionOperator,
            string valueType,
            string rawValue)
        {
            string normalizedOperator = NormalizeConditionOperator(conditionOperator);
            string normalizedValueType = NormalizeValueType(valueType);

            switch (normalizedOperator)
            {
                case RuntimeConditionOperators.Exists:
                    return blackboard.Contains(key);
                case RuntimeConditionOperators.IsTrue:
                    return GetBool(blackboard, key);
                case RuntimeConditionOperators.IsFalse:
                    return !GetBool(blackboard, key);
                case RuntimeConditionOperators.Equal:
                    return EvaluateEquality(blackboard, key, normalizedValueType, rawValue, true);
                case RuntimeConditionOperators.NotEqual:
                    return EvaluateEquality(blackboard, key, normalizedValueType, rawValue, false);
                case RuntimeConditionOperators.Greater:
                    return EvaluateNumericComparison(blackboard, key, normalizedValueType, rawValue, (left, right) => left > right);
                case RuntimeConditionOperators.GreaterOrEqual:
                    return EvaluateNumericComparison(blackboard, key, normalizedValueType, rawValue, (left, right) => left >= right);
                case RuntimeConditionOperators.Less:
                    return EvaluateNumericComparison(blackboard, key, normalizedValueType, rawValue, (left, right) => left < right);
                case RuntimeConditionOperators.LessOrEqual:
                    return EvaluateNumericComparison(blackboard, key, normalizedValueType, rawValue, (left, right) => left <= right);
                default:
                    throw new InvalidOperationException($"Condition operator '{conditionOperator}' is not supported.");
            }
        }

        private static bool EvaluateEquality(
            SkillBlackboard blackboard,
            string key,
            string valueType,
            string rawValue,
            bool expectedEqual)
        {
            bool isEqual = valueType switch
            {
                RuntimeValueTypes.String => string.Equals(GetString(blackboard, key), rawValue ?? string.Empty, StringComparison.Ordinal),
                RuntimeValueTypes.Float => Math.Abs(GetFloat(blackboard, key) - ParseFloat(rawValue, key)) <= 0.0001f,
                RuntimeValueTypes.Int => GetInt(blackboard, key) == ParseInt(rawValue, key),
                RuntimeValueTypes.Bool => GetBool(blackboard, key) == ParseBool(rawValue, key),
                _ => throw new InvalidOperationException($"Condition valueType '{valueType}' is not supported.")
            };

            return expectedEqual ? isEqual : !isEqual;
        }

        private static bool EvaluateNumericComparison(
            SkillBlackboard blackboard,
            string key,
            string valueType,
            string rawValue,
            Func<double, double, bool> comparer)
        {
            switch (valueType)
            {
                case RuntimeValueTypes.Float:
                    return comparer(GetFloat(blackboard, key), ParseFloat(rawValue, key));
                case RuntimeValueTypes.Int:
                    return comparer(GetInt(blackboard, key), ParseInt(rawValue, key));
                default:
                    throw new InvalidOperationException(
                        $"Condition numeric operator requires Int or Float, but got '{valueType}' for key '{key}'.");
            }
        }

        private static string GetString(SkillBlackboard blackboard, string key)
        {
            if (blackboard.TryGetString(key, out string value))
                return value;

            throw new InvalidOperationException($"Blackboard key '{key}' is missing or is not a String.");
        }

        private static float GetFloat(SkillBlackboard blackboard, string key)
        {
            if (blackboard.TryGetFloat(key, out float value))
                return value;

            throw new InvalidOperationException($"Blackboard key '{key}' is missing or is not a Float.");
        }

        private static int GetInt(SkillBlackboard blackboard, string key)
        {
            if (blackboard.TryGetInt(key, out int value))
                return value;

            throw new InvalidOperationException($"Blackboard key '{key}' is missing or is not an Int.");
        }

        public static bool GetBool(SkillBlackboard blackboard, string key)
        {
            if (blackboard.TryGetBool(key, out bool value))
                return value;

            throw new InvalidOperationException($"Blackboard key '{key}' is missing or is not a Bool.");
        }

        private static float ParseFloat(string rawValue, string key)
        {
            if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;

            throw new InvalidOperationException($"Blackboard compare value '{rawValue}' for key '{key}' is not a valid Float.");
        }

        private static int ParseInt(string rawValue, string key)
        {
            if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;

            throw new InvalidOperationException($"Blackboard compare value '{rawValue}' for key '{key}' is not a valid Int.");
        }

        private static bool ParseBool(string rawValue, string key)
        {
            if (bool.TryParse(rawValue, out bool value))
                return value;

            throw new InvalidOperationException($"Blackboard compare value '{rawValue}' for key '{key}' is not a valid Bool.");
        }

        private static string NormalizeValueType(string valueType)
        {
            if (string.IsNullOrWhiteSpace(valueType))
                return RuntimeValueTypes.String;

            if (string.Equals(valueType, RuntimeValueTypes.String, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.String;

            if (string.Equals(valueType, RuntimeValueTypes.Float, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.Float;

            if (string.Equals(valueType, RuntimeValueTypes.Int, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.Int;

            if (string.Equals(valueType, RuntimeValueTypes.Bool, StringComparison.OrdinalIgnoreCase))
                return RuntimeValueTypes.Bool;

            throw new InvalidOperationException($"Value type '{valueType}' is not supported.");
        }

        private static string NormalizeConditionOperator(string conditionOperator)
        {
            if (string.IsNullOrWhiteSpace(conditionOperator))
                return RuntimeConditionOperators.Exists;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.Exists, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.Exists;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.Equal, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.Equal;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.NotEqual, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.NotEqual;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.Greater, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.Greater;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.GreaterOrEqual, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.GreaterOrEqual;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.Less, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.Less;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.LessOrEqual, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.LessOrEqual;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.IsTrue, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.IsTrue;

            if (string.Equals(conditionOperator, RuntimeConditionOperators.IsFalse, StringComparison.OrdinalIgnoreCase))
                return RuntimeConditionOperators.IsFalse;

            throw new InvalidOperationException($"Condition operator '{conditionOperator}' is not supported.");
        }
    }

    internal static class SkillHandlerUtility
    {
        public static bool IsCancellationRequested(SkillContext context) =>
            context != null && context.IsCancellationRequested;

        public static SkillBlackboard EnsureBlackboard(SkillContext context)
        {
            if (context.Blackboard == null)
                context.Blackboard = new SkillBlackboard();

            return context.Blackboard;
        }

        public static string RequireProperty(RuntimeSkillNode node, string key)
        {
            string value = node.GetPropertyValue(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            throw new InvalidOperationException($"Node {node.NodeId} requires non-empty property '{key}'.");
        }

        public static int RequirePositiveIntProperty(RuntimeSkillNode node, string key)
        {
            int value = node.GetIntPropertyValue(key, 0);
            if (value > 0)
            {
                return value;
            }

            throw new InvalidOperationException($"Node {node.NodeId} requires positive integer property '{key}'.");
        }

        public static IBuffCommandSink RequireBuffCommandSink(SkillContext context)
        {
            if (context.BuffCommandSink != null)
            {
                return context.BuffCommandSink;
            }

            throw new InvalidOperationException("SkillContext.BuffCommandSink must be assigned before executing buff nodes.");
        }

        public static long ResolveTargetId(RuntimeSkillNode node, SkillContext context)
        {
            string selector = node.GetPropertyValue(RuntimePropertyKeys.TargetSelector, RuntimeBuffTargetSelectors.Target);
            if (string.Equals(selector, RuntimeBuffTargetSelectors.Caster, StringComparison.OrdinalIgnoreCase))
            {
                return context.CasterId;
            }

            return context.TargetId;
        }

        public static uint ResolveFrameIndex(SkillContext context)
        {
            return context.CurrentFrameIndex <= 0
                ? 0u
                : checked((uint)context.CurrentFrameIndex);
        }
    }
}
