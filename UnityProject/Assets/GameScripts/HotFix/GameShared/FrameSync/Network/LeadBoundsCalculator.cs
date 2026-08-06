using System;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Network
{
    /// <summary>
    /// Pure-function upper-bound for observed input lead vs server LastFrameIndex.
    /// Alerts only; never rejects. Anchored on MaxAcceptedInputBufferFrames (arrival-time
    /// lead dimension), not MaxLeadFrames (send-time lead).
    /// </summary>
    public sealed class LeadBoundsParams
    {
        public float WindowMs { get; }
        public float FixedDeltaMilliseconds { get; }
        public int MaxAcceptedInputBufferFrames { get; }
        public int MaxFutureInputFrames { get; }

        public LeadBoundsParams(
            float windowMs = 200f,
            float fixedDeltaMilliseconds = -1f,
            int maxAcceptedInputBufferFrames = -1,
            int maxFutureInputFrames = -1)
        {
            float dt = fixedDeltaMilliseconds > 0f
                ? fixedDeltaMilliseconds
                : DeterminismRules.FixedDeltaTime * 1000f;

            int maxAccepted = maxAcceptedInputBufferFrames > 0
                ? maxAcceptedInputBufferFrames
                : InputBufferTuning.MaxAcceptedInputBufferFrames;

            int maxFuture = maxFutureInputFrames > 0
                ? maxFutureInputFrames
                : InputBufferTuning.MaxFutureInputFrames;

            if (float.IsNaN(windowMs) || float.IsInfinity(windowMs) || windowMs < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(windowMs), windowMs, "WindowMs must be finite and non-negative.");
            }

            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaMilliseconds), dt, "FixedDeltaMilliseconds must be finite and positive.");
            }

            int toleranceFrames = (int)Math.Ceiling(windowMs / (double)dt) + 1;
            if (maxAccepted + toleranceFrames >= maxFuture)
            {
                throw new InvalidOperationException(
                    $"Lead alarm region is empty: MaxAccepted({maxAccepted}) + " +
                    $"tolerance({toleranceFrames}) >= MaxFuture({maxFuture}). " +
                    "Widen MaxFuture or shrink WindowMs.");
            }

            WindowMs = windowMs;
            FixedDeltaMilliseconds = dt;
            MaxAcceptedInputBufferFrames = maxAccepted;
            MaxFutureInputFrames = maxFuture;
        }
    }

    public readonly struct LeadBoundsResult
    {
        public LeadBoundsResult(bool hasSample, long observedLead, int upperBound, int jitterAllowanceFrames, bool isOutOfBounds)
        {
            HasSample = hasSample;
            ObservedLead = observedLead;
            UpperBound = upperBound;
            JitterAllowanceFrames = jitterAllowanceFrames;
            IsOutOfBounds = isOutOfBounds;
        }

        public bool HasSample { get; }
        public long ObservedLead { get; }
        public int UpperBound { get; }
        public int JitterAllowanceFrames { get; }
        public bool IsOutOfBounds { get; }
    }

    public static class LeadBoundsCalculator
    {
        private static readonly LeadBoundsParams DefaultParams = new LeadBoundsParams();

        /// <summary>
        /// Compute signed observed lead and upper bound.
        /// Returns IsOutOfBounds=false when no sample, late/equal input, or within bounds.
        /// </summary>
        public static LeadBoundsResult Evaluate(
            uint claimedFrameIndex,
            uint lastFrameIndex,
            bool hasSample,
            float controlRttMs,
            LeadBoundsParams? parameters = null)
        {
            LeadBoundsParams p = parameters ?? DefaultParams;

            if (claimedFrameIndex <= lastFrameIndex)
            {
                return new LeadBoundsResult(hasSample, 0, 0, 0, false);
            }

            long observedLead = (long)claimedFrameIndex - (long)lastFrameIndex;

            if (!hasSample)
            {
                return new LeadBoundsResult(false, observedLead, 0, 0, false);
            }

            if (float.IsNaN(controlRttMs) || float.IsInfinity(controlRttMs) || controlRttMs < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(controlRttMs),
                    controlRttMs,
                    "controlRttMs must be finite and non-negative when hasSample is true.");
            }

            int jitterAllowanceFrames = (int)Math.Ceiling(p.WindowMs / (double)p.FixedDeltaMilliseconds) + 1;
            int rttAllowanceFrames = (int)Math.Ceiling(controlRttMs / 2.0d / p.FixedDeltaMilliseconds);
            int upperBound = p.MaxAcceptedInputBufferFrames + jitterAllowanceFrames + rttAllowanceFrames;

            return new LeadBoundsResult(true, observedLead, upperBound, jitterAllowanceFrames, observedLead > upperBound);
        }
    }
}
