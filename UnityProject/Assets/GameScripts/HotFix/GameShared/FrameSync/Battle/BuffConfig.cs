namespace GameShared.FrameSync.Battle
{
    public readonly struct BuffConfig
    {
        public BuffConfig(
            int buffId,
            BuffOverlayType overlayType,
            int maxStack,
            int mutexGroupId,
            int priority,
            int durationFrames,
            BuffFlags defaultFlags,
            BuffEffect[] effects)
        {
            BuffId = buffId;
            OverlayType = overlayType;
            MaxStack = maxStack;
            MutexGroupId = mutexGroupId;
            Priority = priority;
            DurationFrames = durationFrames;
            DefaultFlags = defaultFlags;
            Effects = effects;
        }

        public int BuffId { get; }
        public BuffOverlayType OverlayType { get; }
        public int MaxStack { get; }
        public int MutexGroupId { get; }
        public int Priority { get; }
        public int DurationFrames { get; }
        public BuffFlags DefaultFlags { get; }
        public BuffEffect[] Effects { get; }
    }
}
