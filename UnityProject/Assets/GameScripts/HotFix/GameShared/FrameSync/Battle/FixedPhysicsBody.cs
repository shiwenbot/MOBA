using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    internal struct FixedPhysicsBody
    {
        public FixedPhysicsBody(Fixed64 positionX, Fixed64 positionY)
        {
            PositionX = positionX;
            PositionY = positionY;
            RotationRadians = Fixed64.Zero;
            LinearVelocityX = Fixed64.Zero;
            LinearVelocityY = Fixed64.Zero;
            AngularVelocity = Fixed64.Zero;
            IsAwake = true;
            IsEnabled = true;
        }

        public FixedPhysicsBody(PhysicsBodySnapshot snapshot)
        {
            PositionX = snapshot.PositionX;
            PositionY = snapshot.PositionY;
            RotationRadians = snapshot.RotationRadians;
            LinearVelocityX = snapshot.LinearVelocityX;
            LinearVelocityY = snapshot.LinearVelocityY;
            AngularVelocity = snapshot.AngularVelocity;
            IsAwake = snapshot.IsAwake;
            IsEnabled = snapshot.IsEnabled;
        }

        public Fixed64 PositionX;
        public Fixed64 PositionY;
        public Fixed64 RotationRadians;
        public Fixed64 LinearVelocityX;
        public Fixed64 LinearVelocityY;
        public Fixed64 AngularVelocity;
        public bool IsAwake;
        public bool IsEnabled;

        public PhysicsBodySnapshot ToSnapshot(int bodyId)
        {
            return new PhysicsBodySnapshot(
                bodyId,
                PositionX,
                PositionY,
                RotationRadians,
                LinearVelocityX,
                LinearVelocityY,
                AngularVelocity,
                IsAwake,
                IsEnabled);
        }
    }

    internal readonly struct FixedPhysicsVector
    {
        public static readonly FixedPhysicsVector Zero = new FixedPhysicsVector(Fixed64.Zero, Fixed64.Zero);

        public FixedPhysicsVector(Fixed64 x, Fixed64 y)
        {
            X = x;
            Y = y;
        }

        public Fixed64 X { get; }
        public Fixed64 Y { get; }
    }
}
