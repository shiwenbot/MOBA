using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    public static class DashTuning
    {
        public const int DashSkillId = 1002;
        public const int DashBuffId = 9301;
        public const int RecoverBuffId = 9302;
        public const int DashFrames = 6;
        public const int RecoverFrames = 20;
        public const int DashSpeed = 18;
        public const int DashStaminaCost = 30;
        public const int DashDecayNumerator = 1;
        public const int DashDecayDenominator = 1;
        public const int StaminaRegenIntervalFrames = 3;
        public const int StaminaRegenAmount = 1;

        public static readonly Fixed64 DashSpeedFixed = (Fixed64)DashSpeed;
    }
}
