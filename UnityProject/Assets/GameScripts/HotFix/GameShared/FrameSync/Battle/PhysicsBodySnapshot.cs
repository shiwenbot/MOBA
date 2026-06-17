using FixedMathSharp;

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
            : this(
                bodyId,
                (Fixed64)positionX,
                (Fixed64)positionY,
                (Fixed64)rotationRadians,
                (Fixed64)linearVelocityX,
                (Fixed64)linearVelocityY,
                (Fixed64)angularVelocity,
                isAwake,
                isEnabled)
        {
        }

        public PhysicsBodySnapshot(
            int bodyId,
            Fixed64 positionX,
            Fixed64 positionY,
            Fixed64 rotationRadians,
            Fixed64 linearVelocityX,
            Fixed64 linearVelocityY,
            Fixed64 angularVelocity,
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
        public Fixed64 PositionX { get; }
        public Fixed64 PositionY { get; }
        public Fixed64 RotationRadians { get; }
        public Fixed64 LinearVelocityX { get; }
        public Fixed64 LinearVelocityY { get; }
        public Fixed64 AngularVelocity { get; }
        public bool IsAwake { get; }
        public bool IsEnabled { get; }
    }
}
