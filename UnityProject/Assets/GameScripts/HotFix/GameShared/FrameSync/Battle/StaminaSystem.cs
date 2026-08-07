using System;

namespace GameShared.FrameSync.Battle
{
    public static class StaminaSystem
    {
        public static bool TryConsume(PlayerState state, int amount)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (amount <= 0)
            {
                return true;
            }

            if (state.Stamina < amount)
            {
                return false;
            }

            state.Stamina -= amount;
            return true;
        }

        public static void Tick(PlayerState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (state.Stamina >= state.MaxStamina)
            {
                state.StaminaRegenCounterFrames = 0;
                return;
            }

            state.StaminaRegenCounterFrames++;
            if (state.StaminaRegenCounterFrames < DashTuning.StaminaRegenIntervalFrames)
            {
                return;
            }

            state.StaminaRegenCounterFrames -= DashTuning.StaminaRegenIntervalFrames;
            state.Stamina = Math.Min(
                state.MaxStamina,
                state.Stamina + DashTuning.StaminaRegenAmount);
        }
    }
}
