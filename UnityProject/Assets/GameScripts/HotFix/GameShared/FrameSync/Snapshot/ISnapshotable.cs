namespace GameShared.FrameSync.Snapshot
{
    public interface ISnapshotable<TSnapshot>
    {
        TSnapshot TakeSnapshot();

        void RestoreSnapshot(TSnapshot snapshot);
    }
}
