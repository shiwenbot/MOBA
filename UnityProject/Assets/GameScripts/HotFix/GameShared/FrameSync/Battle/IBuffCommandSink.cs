namespace GameShared.FrameSync.Battle
{
    public interface IBuffCommandSink
    {
        void EnqueueApplyBuff(ApplyBuffCommand command);
        void EnqueueRemoveBuff(RemoveBuffCommand command);
        bool HasBuff(long targetId, int buffId);
        int GetBuffStackCount(long targetId, int buffId);
    }
}
