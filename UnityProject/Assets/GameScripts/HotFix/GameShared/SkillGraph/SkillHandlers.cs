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
}
