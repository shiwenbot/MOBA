using System;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Core
{
    public interface IBattleClock
    {
        long NowMs { get; }
    }

    public sealed class SystemBattleClock : IBattleClock
    {
        public static readonly SystemBattleClock Instance = new SystemBattleClock();

        private SystemBattleClock()
        {
        }

        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public sealed class FrameBattleClock : IBattleClock
    {
        public FrameBattleClock()
            : this(DeterminismRules.FixedDeltaTime * 1000.0d)
        {
        }

        public FrameBattleClock(double fixedDeltaMilliseconds)
        {
            if (fixedDeltaMilliseconds <= 0.0d ||
                double.IsNaN(fixedDeltaMilliseconds) ||
                double.IsInfinity(fixedDeltaMilliseconds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedDeltaMilliseconds),
                    fixedDeltaMilliseconds,
                    "Fixed delta milliseconds must be finite and positive.");
            }

            FixedDeltaMilliseconds = fixedDeltaMilliseconds;
        }

        public uint FrameIndex { get; private set; }
        public double FixedDeltaMilliseconds { get; }
        public long NowMs => checked((long)Math.Round(
            FrameIndex * FixedDeltaMilliseconds,
            MidpointRounding.AwayFromZero));

        public void SetFrame(uint frameIndex)
        {
            FrameIndex = frameIndex;
        }
    }
}
