using System;
using System.Diagnostics;
using FixedMathSharp;
using GameShared.FrameSync.Core;

namespace GameShared.FrameSync.Determinism
{
    public static class DeterminismRules
    {
        public const float FixedDeltaTime = TickAccumulator.DefaultFixedDeltaTime;
        public static readonly Fixed64 MoveSpeed = (Fixed64)5;
        public static readonly Fixed64 FixedDeltaTimeFixed64 = TickAccumulator.DefaultFixedDeltaTimeFixed64;
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
        public static void AssertFixedDt(Fixed64 dt)
        {
            if (dt.m_rawValue != FixedDeltaTimeFixed64.m_rawValue)
            {
                throw new InvalidOperationException(
                    $"FrameSync fixedDt mismatch. expectedRaw={FixedDeltaTimeFixed64.m_rawValue}, actualRaw={dt.m_rawValue}.");
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
