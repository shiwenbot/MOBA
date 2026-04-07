using Fantasy.Async;

namespace GameShared.SkillGraph
{
    public interface ISkillRuntimeServices
    {
        void Log(string message);

        FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null);
    }

    public sealed class SkillContext
    {
        public long CasterId { get; set; }

        public long TargetId { get; set; }

        public int SkillId { get; set; }

        public bool IsCancelled { get; set; }

        public ISkillRuntimeServices? Runtime { get; set; }

        public FCancellationToken? CancellationToken { get; set; }
    }
}
