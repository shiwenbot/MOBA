using System;

namespace GameShared.FrameSync.Network
{
    public readonly struct NetworkConditionConfig
    {
        public NetworkConditionConfig(
            bool isEnabled,
            int uplinkDelayMs,
            int downlinkDelayMs,
            int uplinkJitterMs,
            int downlinkJitterMs,
            int uplinkLossPercent,
            int downlinkLossPercent,
            ulong seed)
        {
            ValidateNonNegative(uplinkDelayMs, nameof(uplinkDelayMs));
            ValidateNonNegative(downlinkDelayMs, nameof(downlinkDelayMs));
            ValidateNonNegative(uplinkJitterMs, nameof(uplinkJitterMs));
            ValidateNonNegative(downlinkJitterMs, nameof(downlinkJitterMs));
            ValidateLossPercent(uplinkLossPercent, nameof(uplinkLossPercent));
            ValidateLossPercent(downlinkLossPercent, nameof(downlinkLossPercent));

            IsEnabled = isEnabled;
            UplinkDelayMs = uplinkDelayMs;
            DownlinkDelayMs = downlinkDelayMs;
            UplinkJitterMs = uplinkJitterMs;
            DownlinkJitterMs = downlinkJitterMs;
            UplinkLossPercent = uplinkLossPercent;
            DownlinkLossPercent = downlinkLossPercent;
            Seed = seed;
        }

        public static NetworkConditionConfig Disabled => default;

        public bool IsEnabled { get; }
        public int UplinkDelayMs { get; }
        public int DownlinkDelayMs { get; }
        public int UplinkJitterMs { get; }
        public int DownlinkJitterMs { get; }
        public int UplinkLossPercent { get; }
        public int DownlinkLossPercent { get; }
        public ulong Seed { get; }

        private static void ValidateNonNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Network delay and jitter must be non-negative.");
            }
        }

        private static void ValidateLossPercent(int value, string parameterName)
        {
            if (value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Network loss percent must be in [0, 100].");
            }
        }
    }
}
