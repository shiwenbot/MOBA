namespace GameShared.FrameSync.Battle
{
    public readonly struct PhysicsBodySnapshot
    {
        public PhysicsBodySnapshot(
            int bodyId,
            float positionX,
            float positionY,
            float rotationRadians,
            float linearVelocityX,
            float linearVelocityY,
            float angularVelocity,
            bool isAwake,
            bool isEnabled)
        {
            BodyId = bodyId;
            PositionX = positionX;
            PositionY = positionY;
            RotationRadians = rotationRadians;
            LinearVelocityX = linearVelocityX;
            LinearVelocityY = linearVelocityY;
            AngularVelocity = angularVelocity;
            IsAwake = isAwake;
            IsEnabled = isEnabled;
        }

        public int BodyId { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public float RotationRadians { get; }
        public float LinearVelocityX { get; }
        public float LinearVelocityY { get; }
        public float AngularVelocity { get; }
        public bool IsAwake { get; }
        public bool IsEnabled { get; }
    }
}
