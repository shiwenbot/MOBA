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
}
