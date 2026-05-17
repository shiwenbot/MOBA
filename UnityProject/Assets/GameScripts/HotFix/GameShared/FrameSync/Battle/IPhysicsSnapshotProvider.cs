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

        void EnsureBody(int bodyId, float x, float y);

        void RemoveBody(int bodyId);

        void SetBodyTransform(int bodyId, float x, float y, bool resetVelocity);

        void SetBodyMovementInput(int bodyId, float dx, float dy);

        void Step(float dt);

        bool TryGetBodySnapshot(int bodyId, out PhysicsBodySnapshot snapshot);
    }
}
