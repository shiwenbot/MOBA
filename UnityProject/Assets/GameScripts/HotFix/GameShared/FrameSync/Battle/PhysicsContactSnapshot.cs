namespace GameShared.FrameSync.Battle
{
    public readonly struct PhysicsContactSnapshot
    {
        public PhysicsContactSnapshot(int bodyAId, int bodyBId, bool isTouching)
        {
            BodyAId = bodyAId;
            BodyBId = bodyBId;
            IsTouching = isTouching;
        }

        public int BodyAId { get; }
        public int BodyBId { get; }
        public bool IsTouching { get; }
    }
}
