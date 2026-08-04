using FixedMathSharp;

namespace GameShared.FrameSync.Battle
{
    public interface IPhysicsSnapshotProvider
    {
        PhysicsWorldSnapshot TakeSnapshot();

        void RestoreSnapshot(PhysicsWorldSnapshot snapshot);
    }

    public interface IPhysicsMovementWorld : IPhysicsSnapshotProvider
    {
        void ClearBodies();

        void EnsureBody(int bodyId, Fixed64 x, Fixed64 y);

        void RemoveBody(int bodyId);

        void SetBodyTransform(int bodyId, Fixed64 x, Fixed64 y, bool resetVelocity);

        void SetBodyMovementInput(int bodyId, Fixed64 dx, Fixed64 dy);

        void ApplyBodyImpulse(int bodyId, Fixed64 impulseX, Fixed64 impulseY);

        void Step(Fixed64 dt);

        bool TryGetBodySnapshot(int bodyId, out PhysicsBodySnapshot snapshot);
    }
}
