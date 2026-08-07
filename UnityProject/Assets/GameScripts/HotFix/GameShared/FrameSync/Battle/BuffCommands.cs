using GameShared.FrameSync.Command;
using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    public enum RemoveReason : byte
    {
        Expired = 1,
        Dispel = 2,
        Death = 3,
        Manual = 4
    }

    public sealed class ApplyBuffCommand : IResettable
    {
        public long CasterId { get; set; }
        public long TargetId { get; set; }
        public int BuffId { get; set; }
        public int DurationFrames { get; set; }
        public int StackCount { get; set; }
        public uint FrameIndex { get; set; }
        public BuffFlags Flags { get; set; }
        public bool HasDisplacementVelocityOverride { get; set; }
        public Fixed64 DisplacementVelocityX { get; set; }
        public Fixed64 DisplacementVelocityY { get; set; }

        public void Reset()
        {
            CasterId = default;
            TargetId = default;
            BuffId = default;
            DurationFrames = default;
            StackCount = default;
            FrameIndex = default;
            Flags = BuffFlags.None;
            HasDisplacementVelocityOverride = false;
            DisplacementVelocityX = Fixed64.Zero;
            DisplacementVelocityY = Fixed64.Zero;
        }
    }

    public sealed class RemoveBuffCommand : IResettable
    {
        public long TargetId { get; set; }
        public long RuntimeBuffId { get; set; }
        public int BuffId { get; set; }
        public RemoveReason RemoveReason { get; set; }
        public uint FrameIndex { get; set; }

        public void Reset()
        {
            TargetId = default;
            RuntimeBuffId = default;
            BuffId = default;
            RemoveReason = default;
            FrameIndex = default;
        }
    }
}
