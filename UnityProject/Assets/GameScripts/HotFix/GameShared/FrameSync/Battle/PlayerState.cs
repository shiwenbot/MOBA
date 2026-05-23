using System.Collections.Generic;

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

        public PlayerState(long playerId, float x, float y)
            : this(playerId, x, y, PlayerAttributeSnapshot.Default)
        {
        }

        public PlayerState(long playerId, float x, float y, PlayerAttributeSnapshot attributes)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
            RestoreAttributeSnapshot(attributes);
        }

        public long PlayerId { get; }
        public float X { get; set; }
        public float Y { get; set; }
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

        public List<BuffState> ActiveBuffs { get; } = new List<BuffState>();
        public NumericState Numeric { get; } = new NumericState();
        public long NextRuntimeBuffId { get; set; } = 1;

        public PlayerAttributeSnapshot CaptureAttributeSnapshot()
        {
            return new PlayerAttributeSnapshot(Health, MaxHealth, Mana, MaxMana, Attack);
        }

        public void RestoreAttributeSnapshot(PlayerAttributeSnapshot attributes)
        {
            _suppressNumericBaseSync = false;
            Health = attributes.Health;
            MaxHealth = attributes.MaxHealth;
            Mana = attributes.Mana;
            MaxMana = attributes.MaxMana;
            Attack = attributes.Attack;
            Numeric.SetBaseAttributes(attributes);
        }

        public void RestoreRuntimeState(
            IReadOnlyList<BuffState> activeBuffs,
            long nextRuntimeBuffId,
            NumericModifierSnapshot numericSnapshot)
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
            }

            if (isComputedValue || _suppressNumericBaseSync)
            {
                return;
            }

            Numeric.SetBaseValue(attributeKind, value);
        }
    }
}
