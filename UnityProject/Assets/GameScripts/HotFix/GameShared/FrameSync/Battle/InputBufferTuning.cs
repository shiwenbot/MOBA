using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    /// <summary>
    /// Shared tuning values for client prediction lead and server-side future input buffering.
    /// These are safety rails, while the runtime target is adjusted by client feedback control.
    /// </summary>
    public static class InputBufferTuning
    {
        public const int MinLeadFrames = 3;
        public const int MaxLeadFrames = 20;
        public const int MaxFutureInputFrames = 24;
        public const int MinAcceptedInputBufferFrames = 4;
        public const int MaxAcceptedInputBufferFrames = 6;
        public const int LeadDecreaseCooldownSnapshots = 6;
        public const int JitterBufferFrames = 2;
        public const int InputSendSafetyFrames = 1;
    }

    /// <summary>
    /// Holds the last consumed movement input used for server-side packet-loss reuse.
    /// Suppression clears the latch so reconnect cannot revive input from the old session.
    /// </summary>
    public sealed class ReusablePlayerInput
    {
        private Fixed64 _dx;
        private Fixed64 _dy;
        private bool _hasValue;

        public bool IsSuppressed { get; private set; }
        public bool HasValue => _hasValue;

        public bool SetSuppressed(bool suppressed)
        {
            if (IsSuppressed == suppressed)
            {
                return false;
            }

            IsSuppressed = suppressed;
            if (suppressed)
            {
                Clear();
            }

            return true;
        }

        public void Record(Fixed64 dx, Fixed64 dy)
        {
            if (IsSuppressed)
            {
                return;
            }

            _dx = dx;
            _dy = dy;
            _hasValue = true;
        }

        public bool TryGet(out Fixed64 dx, out Fixed64 dy)
        {
            if (IsSuppressed || !_hasValue)
            {
                dx = Fixed64.Zero;
                dy = Fixed64.Zero;
                return false;
            }

            dx = _dx;
            dy = _dy;
            return true;
        }

        public void Clear()
        {
            _dx = Fixed64.Zero;
            _dy = Fixed64.Zero;
            _hasValue = false;
        }
    }
}
