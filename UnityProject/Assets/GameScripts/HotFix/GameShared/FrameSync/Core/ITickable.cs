using FixedMathSharp;

namespace GameShared.FrameSync.Core
{
    public interface ITickable
    {
        int Priority { get; }

        void Tick(uint frameIndex, Fixed64 fixedDt);

        void RollBack(uint targetFrame)
        {
        }

        void CheckConsistency(uint frameIndex)
        {
        }
    }
}
