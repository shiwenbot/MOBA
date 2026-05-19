namespace GameShared.FrameSync.Battle
{
    public sealed class PlayerState
    {
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
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }
        public int Attack { get; set; }

        public PlayerAttributeSnapshot CaptureAttributeSnapshot()
        {
            return new PlayerAttributeSnapshot(Health, MaxHealth, Mana, MaxMana, Attack);
        }

        public void RestoreAttributeSnapshot(PlayerAttributeSnapshot attributes)
        {
            Health = attributes.Health;
            MaxHealth = attributes.MaxHealth;
            Mana = attributes.Mana;
            MaxMana = attributes.MaxMana;
            Attack = attributes.Attack;
        }
    }
}
