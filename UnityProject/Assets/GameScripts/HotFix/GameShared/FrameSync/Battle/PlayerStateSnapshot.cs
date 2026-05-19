namespace GameShared.FrameSync.Battle
{
    public readonly struct PlayerStateSnapshot
    {
        public PlayerStateSnapshot(long playerId, float x, float y)
            : this(playerId, x, y, PlayerAttributeSnapshot.Default)
        {
        }

        public PlayerStateSnapshot(long playerId, float x, float y, PlayerAttributeSnapshot attributes)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
            Attributes = attributes;
        }

        public long PlayerId { get; }
        public float X { get; }
        public float Y { get; }
        public PlayerAttributeSnapshot Attributes { get; }
        public int Health => Attributes.Health;
        public int MaxHealth => Attributes.MaxHealth;
        public int Mana => Attributes.Mana;
        public int MaxMana => Attributes.MaxMana;
        public int Attack => Attributes.Attack;
    }
}
