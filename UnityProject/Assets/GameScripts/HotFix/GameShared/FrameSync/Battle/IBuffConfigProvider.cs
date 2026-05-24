namespace GameShared.FrameSync.Battle
{
    public interface IBuffConfigProvider
    {
        bool TryGetBuffConfig(int buffId, out BuffConfig config);
    }
}
