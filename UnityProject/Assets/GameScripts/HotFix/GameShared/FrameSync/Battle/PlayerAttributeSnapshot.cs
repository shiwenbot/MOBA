namespace GameShared.FrameSync.Battle
{
    public readonly struct PlayerAttributeSnapshot
    {
        public const int DefaultMaxHealth = 100;
        public const int DefaultMaxMana = 100;
        public const int DefaultAttack = 10;

        public static readonly PlayerAttributeSnapshot Default = new PlayerAttributeSnapshot(
            DefaultMaxHealth,
            DefaultMaxHealth,
            DefaultMaxMana,
            DefaultMaxMana,
            DefaultAttack);

        public PlayerAttributeSnapshot(int health, int maxHealth, int mana, int maxMana, int attack)
        {
            Health = health;
            MaxHealth = maxHealth;
            Mana = mana;
            MaxMana = maxMana;
            Attack = attack;
        }

        public int Health { get; }
        public int MaxHealth { get; }
        public int Mana { get; }
        public int MaxMana { get; }
        public int Attack { get; }
    }
}
