using System;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Network
{
    /// <summary>
    /// Pure feedforward formula: controlRtt → targetLeadFrames.
    /// Shared by server (fill S2C_FrameSnapshot.TargetLeadFrames) and client
    /// (RefreshBaselineLeadFrames) so both ends stay bit-identical in mapping.
    /// </summary>
    public static class TargetLeadCalculator
    {
        public static readonly float FixedDeltaMilliseconds = DeterminismRules.FixedDeltaTime * 1000f;

        /// <summary>
        /// targetLead = clamp(
        ///   JitterBufferFrames + InputSendSafetyFrames + ceil(controlRttMs / 2 / FixedDeltaMs),
        ///   MinLeadFrames, MaxLeadFrames)
        /// </summary>
        public static int Compute(float controlRttMs)
        {
            if (float.IsNaN(controlRttMs) || float.IsInfinity(controlRttMs) || controlRttMs < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(controlRttMs),
                    controlRttMs,
                    "controlRttMs must be finite and non-negative.");
            }

            int rttFrames = (int)Math.Ceiling(controlRttMs / 2.0f / FixedDeltaMilliseconds);
            int leadFrames = InputBufferTuning.JitterBufferFrames +
                             InputBufferTuning.InputSendSafetyFrames +
                             rttFrames;
            return Math.Clamp(
                leadFrames,
                InputBufferTuning.MinLeadFrames,
                InputBufferTuning.MaxLeadFrames);
        }

        /// <summary>
        /// Soft-cap slack for feedback-loop transients:
        /// (MaxAccepted - MinAccepted) + LeadDecreaseCooldownSnapshots / 2 = 5.
        /// </summary>
        public static int SoftCapSlackFrames =>
            InputBufferTuning.MaxAcceptedInputBufferFrames -
            InputBufferTuning.MinAcceptedInputBufferFrames +
            (InputBufferTuning.LeadDecreaseCooldownSnapshots / 2);

        /// <summary>
        /// effectiveMaxLead = min(MaxLeadFrames, targetLeadFrames + slack).
        /// When target is 0 (disabled), returns MaxLeadFrames.
        /// </summary>
        public static int ComputeEffectiveMaxLead(int targetLeadFrames)
        {
            if (targetLeadFrames <= 0)
            {
                return InputBufferTuning.MaxLeadFrames;
            }

            int softCap = targetLeadFrames + SoftCapSlackFrames;
            return Math.Min(InputBufferTuning.MaxLeadFrames, softCap);
        }
    }
}
