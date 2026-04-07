using System;
using Fantasy.Async;

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
            registry.Register(RuntimeNodeTypes.Delay, new DelayNodeHandler());
        }
    }

    public sealed class EntryNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context) =>
            FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Continue("Next"));
    }

    public sealed class DebugNodeHandler : ISkillNodeHandler
    {
        public FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            context.Runtime.Log(node.GetPropertyValue("message"));
            return FTask<SkillExecuteResult>.FromResult(SkillExecuteResult.Continue("Out"));
        }
    }

    public sealed class DelayNodeHandler : ISkillNodeHandler
    {
        public async FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            float durationSeconds = node.GetFloatPropertyValue("duration", 0f);
            int delayMilliseconds = Math.Max(0, (int)(durationSeconds * 1000f));
            bool completed = await context.Runtime!.DelayAsync(delayMilliseconds, context.CancellationToken);
            if (!completed)
            {
                context.IsCancelled = true;
                return SkillExecuteResult.Complete();
            }

            return SkillExecuteResult.Continue("Out");
        }
    }

    public sealed class ActionNodeHandler : ISkillNodeHandler
    {
        public async FTask<SkillExecuteResult> Execute(RuntimeSkillNode node, SkillContext context)
        {
            string actionType = node.GetPropertyValue(RuntimePropertyKeys.ActionType);
            if (!string.Equals(actionType, RuntimeActionTypes.PlayAnimation, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Action node {node.NodeId} actionType '{actionType}' is not supported in v0.4.");
            }

            string prefabLocation = node.GetPropertyValue(RuntimePropertyKeys.PrefabLocation);
            if (string.IsNullOrWhiteSpace(prefabLocation))
                throw new InvalidOperationException($"Action node {node.NodeId} prefabLocation cannot be empty.");

            float speed = node.GetFloatPropertyValue(RuntimePropertyKeys.Value, 1f);
            await context.Runtime!.PlayAnimationAsync(context, prefabLocation, speed, context.CancellationToken);
            return SkillExecuteResult.Continue("Out");
        }
    }
}
