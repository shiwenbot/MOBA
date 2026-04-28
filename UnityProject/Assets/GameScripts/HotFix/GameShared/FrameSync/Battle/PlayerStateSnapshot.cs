namespace GameShared.FrameSync.Battle
{
    public readonly struct PlayerStateSnapshot
    {
        public PlayerStateSnapshot(long playerId, float x, float y)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
        }

        public long PlayerId { get; }
        public float X { get; }
        public float Y { get; }
    }
}
