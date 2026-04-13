using System;

namespace GameShared.FrameSync.Timer
{
    public sealed class FrameTimer
    {
        public FrameTimer(uint id, uint triggerFrame, uint intervalFrames, bool repeat, Action<uint> callback)
        {
            Id = id;
            TriggerFrame = triggerFrame;
            IntervalFrames = intervalFrames;
            Repeat = repeat;
            Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public uint Id { get; }
        public uint TriggerFrame { get; set; }
        public uint IntervalFrames { get; }
        public bool Repeat { get; }
        public Action<uint> Callback { get; }
        public bool IsRemoved { get; set; }
    }
}
