namespace GameShared.FrameSync.Battle
{
    public sealed class PlayerState
    {
        public PlayerState(long playerId, float x, float y)
        {
            PlayerId = playerId;
            X = x;
            Y = y;
        }

        public long PlayerId { get; }
        public float X { get; set; }
        public float Y { get; set; }
    }
}
