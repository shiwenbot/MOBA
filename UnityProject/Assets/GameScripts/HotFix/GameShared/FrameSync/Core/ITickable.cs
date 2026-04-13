namespace GameShared.FrameSync.Core
{
    public interface ITickable
    {
        int Priority { get; }

        void Tick(uint frameIndex, float fixedDt);

        void RollBack(uint targetFrame)
        {
        }

        void CheckConsistency(uint frameIndex)
        {
        }
    }
}
