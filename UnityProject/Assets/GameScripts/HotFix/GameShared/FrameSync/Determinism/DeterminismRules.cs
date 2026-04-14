using System;
using System.Diagnostics;
using GameShared.FrameSync.Core;

namespace GameShared.FrameSync.Determinism
{
    public static class DeterminismRules
    {
        public const float FixedDeltaTime = TickAccumulator.DefaultFixedDeltaTime;
        public const float MoveSpeed = 5.0f;
        private static readonly int FixedDeltaBits = BitConverter.SingleToInt32Bits(FixedDeltaTime);

        public static int FloatToBits(float value)
        {
            return BitConverter.SingleToInt32Bits(value);
        }

        [Conditional("DEBUG")]
        public static void AssertFixedDt(float dt)
        {
            if (FloatToBits(dt) != FixedDeltaBits)
            {
                throw new InvalidOperationException(
                    $"FrameSync fixedDt mismatch. expected={FixedDeltaTime}, actual={dt}.");
            }
        }

        [Conditional("DEBUG")]
        public static void AssertFinite(float value, string valueName)
        {
            if (string.IsNullOrEmpty(valueName))
            {
                valueName = "value";
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(valueName, value, "FrameSync value must be finite.");
            }
        }
    }
}
