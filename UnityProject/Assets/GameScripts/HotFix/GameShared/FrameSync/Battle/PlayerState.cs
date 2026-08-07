using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.SkillGraph;

namespace GameShared.FrameSync.Battle
{
    public sealed class PlayerState
    {
        private bool _suppressNumericBaseSync;
        private int _health;
        private int _maxHealth;
        private int _mana;
        private int _maxMana;
        private int _attack;
        private int _stamina;
        private int _maxStamina;

        public PlayerState(long playerId, float x, float y)
            : this(playerId, (Fixed64)x, (Fixed64)y)
        {
        }

        public PlayerState(long playerId, float x, float y, PlayerAttributeSnapshot attributes)
            : this(playerId, (Fixed64)x, (Fixed64)y, attributes)
        {
        }

        public PlayerState(long playerId, Fixed64 x, Fixed64 y)
            : this(playerId, x, y, PlayerAttributeSnapshot.Default)
        {
        }

        public PlayerState(long playerId, Fixed64 x, Fixed64 y, PlayerAttributeSnapshot attributes)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
            RestoreAttributeSnapshot(attributes);
        }

        public long PlayerId { get; }
        public Fixed64 X { get; set; }
        public Fixed64 Y { get; set; }
        public int Health
        {
            get => _health;
            set => SetAttributeValue(AttributeKind.Health, value, false);
        }

        public int MaxHealth
        {
            get => _maxHealth;
            set => SetAttributeValue(AttributeKind.MaxHealth, value, false);
        }

        public int Mana
        {
            get => _mana;
            set => SetAttributeValue(AttributeKind.Mana, value, false);
        }

        public int MaxMana
        {
            get => _maxMana;
            set => SetAttributeValue(AttributeKind.MaxMana, value, false);
        }

        public int Attack
        {
            get => _attack;
            set => SetAttributeValue(AttributeKind.Attack, value, false);
        }

        public int Stamina
        {
            get => _stamina;
            set => SetAttributeValue(AttributeKind.Stamina, value, false);
        }

        public int MaxStamina
        {
            get => _maxStamina;
            set => SetAttributeValue(AttributeKind.MaxStamina, value, false);
        }

        public List<BuffState> ActiveBuffs { get; } = new List<BuffState>();
        public NumericState Numeric { get; } = new NumericState();
        public long NextRuntimeBuffId { get; set; } = 1;
        public int StaminaRegenCounterFrames { get; set; }

        public Fixed64 DashVelocityX { get; internal set; }
        public Fixed64 DashVelocityY { get; internal set; }
        public int DashRemainingFrames { get; internal set; }
        public long DashRuntimeBuffId { get; internal set; }

        public Fixed64 KnockbackVelocityX { get; internal set; }
        public Fixed64 KnockbackVelocityY { get; internal set; }
        public int KnockbackRemainingFrames { get; internal set; }
        public long KnockbackRuntimeBuffId { get; internal set; }

        public Dictionary<long, ActiveSkillExecutionSnapshot> SkillExecutions { get; } =
            new Dictionary<long, ActiveSkillExecutionSnapshot>();

        public PlayerAttributeSnapshot CaptureAttributeSnapshot()
        {
            return new PlayerAttributeSnapshot(Health, MaxHealth, Mana, MaxMana, Attack, Stamina, MaxStamina);
        }

        public void RestoreAttributeSnapshot(PlayerAttributeSnapshot attributes)
        {
            _suppressNumericBaseSync = false;
            Health = attributes.Health;
            MaxHealth = attributes.MaxHealth;
            Mana = attributes.Mana;
            MaxMana = attributes.MaxMana;
            Attack = attributes.Attack;
            Stamina = attributes.Stamina;
            MaxStamina = attributes.MaxStamina;
            Numeric.SetBaseAttributes(attributes);
        }

        public void RestoreRuntimeState(
            IReadOnlyList<BuffState> activeBuffs,
            long nextRuntimeBuffId,
            NumericModifierSnapshot numericSnapshot,
            int staminaRegenCounterFrames = 0,
            Fixed64 dashVelocityX = default,
            Fixed64 dashVelocityY = default,
            int dashRemainingFrames = 0,
            long dashRuntimeBuffId = 0,
            Fixed64 knockbackVelocityX = default,
            Fixed64 knockbackVelocityY = default,
            int knockbackRemainingFrames = 0,
            long knockbackRuntimeBuffId = 0,
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> skillExecutions = null)
        {
            ActiveBuffs.Clear();
            if (activeBuffs != null)
            {
                for (int i = 0; i < activeBuffs.Count; i++)
                {
                    ActiveBuffs.Add(activeBuffs[i]);
                }
            }

            NextRuntimeBuffId = nextRuntimeBuffId > 0 ? nextRuntimeBuffId : 1;
            StaminaRegenCounterFrames = Math.Max(0, staminaRegenCounterFrames);
            DashVelocityX = dashVelocityX;
            DashVelocityY = dashVelocityY;
            DashRemainingFrames = Math.Max(0, dashRemainingFrames);
            DashRuntimeBuffId = dashRuntimeBuffId;
            KnockbackVelocityX = knockbackVelocityX;
            KnockbackVelocityY = knockbackVelocityY;
            KnockbackRemainingFrames = Math.Max(0, knockbackRemainingFrames);
            KnockbackRuntimeBuffId = knockbackRuntimeBuffId;
            SkillExecutions.Clear();
            foreach (KeyValuePair<long, ActiveSkillExecutionSnapshot> pair in
                     SkillExecutionSnapshotCodec.CloneExecutions(skillExecutions))
            {
                SkillExecutions[pair.Key] = pair.Value;
            }
            Numeric.RestoreSnapshot(numericSnapshot);
            Numeric.Recalculate(this);
        }

        internal long AllocateRuntimeBuffId()
        {
            long runtimeBuffId = NextRuntimeBuffId;
            NextRuntimeBuffId++;
            return runtimeBuffId;
        }

        internal void SetComputedAttributes(PlayerAttributeSnapshot attributes)
        {
            _suppressNumericBaseSync = true;
            try
            {
                Health = attributes.Health;
                MaxHealth = attributes.MaxHealth;
                Mana = attributes.Mana;
                MaxMana = attributes.MaxMana;
                Attack = attributes.Attack;
                Stamina = attributes.Stamina;
                MaxStamina = attributes.MaxStamina;
            }
            finally
            {
                _suppressNumericBaseSync = false;
            }
        }

        private void SetAttributeValue(AttributeKind attributeKind, int value, bool isComputedValue)
        {
            switch (attributeKind)
            {
                case AttributeKind.Health:
                    _health = value;
                    break;
                case AttributeKind.MaxHealth:
                    _maxHealth = value;
                    break;
                case AttributeKind.Mana:
                    _mana = value;
                    break;
                case AttributeKind.MaxMana:
                    _maxMana = value;
                    break;
                case AttributeKind.Attack:
                    _attack = value;
                    break;
                case AttributeKind.Stamina:
                    _stamina = value;
                    break;
                case AttributeKind.MaxStamina:
                    _maxStamina = value;
                    break;
            }

            if (isComputedValue || _suppressNumericBaseSync)
            {
                return;
            }

            Numeric.SetBaseValue(attributeKind, value);
        }

        internal void SetDisplacement(
            DisplacementEffect effect,
            long runtimeBuffId,
            Fixed64 velocityX,
            Fixed64 velocityY)
        {
            Fixed64 resolvedVelocityX = velocityX;
            Fixed64 resolvedVelocityY = velocityY;
            switch (effect.Kind)
            {
                case DisplacementKind.Dash:
                    DashVelocityX = resolvedVelocityX;
                    DashVelocityY = resolvedVelocityY;
                    DashRemainingFrames = effect.DurationFrames;
                    DashRuntimeBuffId = runtimeBuffId;
                    break;
                case DisplacementKind.Knockback:
                    KnockbackVelocityX = resolvedVelocityX;
                    KnockbackVelocityY = resolvedVelocityY;
                    KnockbackRemainingFrames = effect.DurationFrames;
                    KnockbackRuntimeBuffId = runtimeBuffId;
                    break;
            }
        }

        internal void ClearDisplacement(DisplacementKind kind)
        {
            if (kind == DisplacementKind.Dash)
            {
                DashVelocityX = Fixed64.Zero;
                DashVelocityY = Fixed64.Zero;
                DashRemainingFrames = 0;
                DashRuntimeBuffId = 0;
                return;
            }

            KnockbackVelocityX = Fixed64.Zero;
            KnockbackVelocityY = Fixed64.Zero;
            KnockbackRemainingFrames = 0;
            KnockbackRuntimeBuffId = 0;
        }

        internal void ClearDisplacementForBuff(long runtimeBuffId)
        {
            if (runtimeBuffId <= 0)
            {
                return;
            }

            if (DashRuntimeBuffId == runtimeBuffId)
            {
                ClearDisplacement(DisplacementKind.Dash);
            }

            if (KnockbackRuntimeBuffId == runtimeBuffId)
            {
                ClearDisplacement(DisplacementKind.Knockback);
            }
        }

        public bool TryGetBuff(int buffId, out BuffState buffState)
        {
            for (int i = 0; i < ActiveBuffs.Count; i++)
            {
                if (ActiveBuffs[i].BuffId == buffId)
                {
                    buffState = ActiveBuffs[i];
                    return true;
                }
            }

            buffState = default;
            return false;
        }
    }
}
