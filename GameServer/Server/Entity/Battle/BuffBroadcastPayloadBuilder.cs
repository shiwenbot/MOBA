using System.Collections.Generic;
using GameShared.FrameSync.Battle;

namespace Fantasy;

internal static class BuffBroadcastPayloadBuilder
{
    public static BuffSyncPayload CreateInitialFullSync(PlayerStateSnapshot player)
    {
        return CreateFullSync(player, 0u);
    }

    public static BuffSyncPayload Build(
        BuffBroadcastBaseline baseline,
        PlayerStateSnapshot player,
        uint frameIndex,
        bool forceFullSync)
    {
        if (forceFullSync)
        {
            baseline.Update(player.ActiveBuffs, player.NextRuntimeBuffId, frameIndex);
            return CreateFullSync(player, frameIndex);
        }

        BuffSync.BuffChange[] changes = BuffSync.ComputeChanges(baseline.ActiveBuffs, player.ActiveBuffs);
        if (changes.Length == 0)
        {
            return new BuffSyncPayload(
                new List<BuffSnapshot>(),
                0L,
                baseline.FrameIndex,
                0u,
                false);
        }

        uint baselineFrameIndex = baseline.FrameIndex;
        baseline.Update(player.ActiveBuffs, player.NextRuntimeBuffId, frameIndex);
        return new BuffSyncPayload(
            BuildBuffSnapshots(changes),
            player.NextRuntimeBuffId,
            baselineFrameIndex,
            BuffSync.HasAnyChangeMask,
            false);
    }

    private static BuffSyncPayload CreateFullSync(PlayerStateSnapshot player, uint baselineFrameIndex)
    {
        return new BuffSyncPayload(
            BuildBuffSnapshots(player.ActiveBuffs),
            player.NextRuntimeBuffId,
            baselineFrameIndex,
            0u,
            true);
    }

    private static List<BuffSnapshot> BuildBuffSnapshots(IReadOnlyList<BuffState> buffs)
    {
        List<BuffSnapshot> snapshots = new List<BuffSnapshot>(buffs.Count);
        for (int i = 0; i < buffs.Count; i++)
        {
            BuffState buff = buffs[i];
            snapshots.Add(new BuffSnapshot
            {
                RuntimeBuffId = buff.RuntimeBuffId,
                BuffId = buff.BuffId,
                CasterId = buff.CasterId,
                TargetId = buff.TargetId,
                StackCount = buff.StackCount,
                RemainingFrames = buff.RemainingFrames,
                AppliedFrame = buff.AppliedFrame,
                Flags = (uint)buff.Flags,
                DirtyFlags = 0u
            });
        }

        return snapshots;
    }

    private static List<BuffSnapshot> BuildBuffSnapshots(IReadOnlyList<BuffSync.BuffChange> changes)
    {
        List<BuffSnapshot> snapshots = new List<BuffSnapshot>(changes.Count);
        for (int i = 0; i < changes.Count; i++)
        {
            BuffSync.BuffChange change = changes[i];
            BuffState buff = change.State;
            snapshots.Add(new BuffSnapshot
            {
                RuntimeBuffId = buff.RuntimeBuffId,
                BuffId = buff.BuffId,
                CasterId = buff.CasterId,
                TargetId = buff.TargetId,
                StackCount = buff.StackCount,
                RemainingFrames = buff.RemainingFrames,
                AppliedFrame = buff.AppliedFrame,
                Flags = (uint)buff.Flags,
                DirtyFlags = (uint)change.DirtyFlags
            });
        }

        return snapshots;
    }
}

internal readonly struct BuffSyncPayload
{
    public BuffSyncPayload(
        List<BuffSnapshot> buffs,
        long nextRuntimeBuffId,
        uint baselineFrameIndex,
        uint dirtyMask,
        bool isFullSync)
    {
        Buffs = buffs;
        NextRuntimeBuffId = nextRuntimeBuffId;
        BaselineFrameIndex = baselineFrameIndex;
        DirtyMask = dirtyMask;
        IsFullSync = isFullSync;
    }

    public List<BuffSnapshot> Buffs { get; }
    public long NextRuntimeBuffId { get; }
    public uint BaselineFrameIndex { get; }
    public uint DirtyMask { get; }
    public bool IsFullSync { get; }
}
