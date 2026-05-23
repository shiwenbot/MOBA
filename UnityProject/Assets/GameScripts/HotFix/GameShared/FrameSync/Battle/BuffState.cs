namespace GameShared.FrameSync.Battle
{
    public readonly struct BuffState
    {
        public BuffState(
            long runtimeBuffId,
            int buffId,
            long casterId,
            long targetId,
            int stackCount,
            int remainingFrames,
            uint appliedFrame,
            BuffFlags flags)
        {
            RuntimeBuffId = runtimeBuffId;
            BuffId = buffId;
            CasterId = casterId;
            TargetId = targetId;
            StackCount = stackCount < 1 ? 1 : stackCount;
            RemainingFrames = remainingFrames;
            AppliedFrame = appliedFrame;
            Flags = flags;
        }

        public long RuntimeBuffId { get; }
        public int BuffId { get; }
        public long CasterId { get; }
        public long TargetId { get; }
        public int StackCount { get; }
        public int RemainingFrames { get; }
        public uint AppliedFrame { get; }
        public BuffFlags Flags { get; }
        public bool HasDuration => (Flags & BuffFlags.Duration) != 0;
        public bool IsPassive => (Flags & BuffFlags.Passive) != 0 || RemainingFrames < 0;

        public BuffState WithRemainingFrames(int remainingFrames)
        {
            return new BuffState(RuntimeBuffId, BuffId, CasterId, TargetId, StackCount, remainingFrames, AppliedFrame, Flags);
        }

        public BuffState WithStackCount(int stackCount)
        {
            return new BuffState(RuntimeBuffId, BuffId, CasterId, TargetId, stackCount, RemainingFrames, AppliedFrame, Flags);
        }
    }
}
