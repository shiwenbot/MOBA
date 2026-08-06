using System;
using FixedMathSharp;
using GameShared.FrameSync.Battle;

namespace GameLogic
{
    public static class SnapshotSyncRecoveryPolicy
    {
        public const uint FullSyncIntervalFrames = 60u;

        public static bool IsPeriodicFullSyncFrame(uint frameIndex, long playerId)
        {
            ulong playerSlot = unchecked((ulong)playerId) % FullSyncIntervalFrames;
            return frameIndex % FullSyncIntervalFrames == playerSlot;
        }
    }

    public static class BattleSnapshotProtocolMapper
    {
        public static AttributeMergeResult MergeAttributes(
            uint frameIndex,
            Fantasy.PlayerSnapshot player,
            bool hasBaseline,
            PlayerAttributeSnapshot baselineAttributes,
            uint baselineFrameIndex)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            PlayerAttributeDirtyFlags dirtyMask = (PlayerAttributeDirtyFlags)player.AttributeDirtyMask;
            if (dirtyMask == PlayerAttributeDirtyFlags.None)
            {
                return new AttributeMergeResult(
                    hasBaseline ? baselineAttributes : PlayerAttributeSnapshot.Default,
                    hasBaseline ? baselineFrameIndex : 0u,
                    hasBaseline,
                    false,
                    false);
            }

            bool isFullSync = IsFullAttributeSnapshot(dirtyMask);
            if (!isFullSync && (!hasBaseline || baselineFrameIndex != player.AttributeBaselineFrameIndex))
            {
                return new AttributeMergeResult(
                    hasBaseline ? baselineAttributes : PlayerAttributeSnapshot.Default,
                    hasBaseline ? baselineFrameIndex : 0u,
                    hasBaseline,
                    false,
                    true);
            }

            PlayerAttributeSnapshot merged = PlayerAttributeSync.Merge(
                hasBaseline ? baselineAttributes : PlayerAttributeSnapshot.Default,
                dirtyMask,
                player.Health,
                player.MaxHealth,
                player.Mana,
                player.MaxMana,
                player.Attack);
            return new AttributeMergeResult(merged, frameIndex, true, true, false);
        }

        public static bool IsFullAttributeSnapshot(PlayerAttributeDirtyFlags dirtyMask)
        {
            return (dirtyMask & PlayerAttributeDirtyFlags.All) == PlayerAttributeDirtyFlags.All;
        }

        public static void WritePhysicsBody(
            Fantasy.PlayerSnapshot player,
            bool hasPhysics,
            PhysicsBodySnapshot body)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            player.LinearVelocityXRaw = hasPhysics ? body.LinearVelocityX.m_rawValue : 0L;
            player.LinearVelocityYRaw = hasPhysics ? body.LinearVelocityY.m_rawValue : 0L;
            player.AngularVelocityRaw = hasPhysics ? body.AngularVelocity.m_rawValue : 0L;
            player.RotationRadiansRaw = hasPhysics ? body.RotationRadians.m_rawValue : 0L;
            player.IsAsleep = hasPhysics && !body.IsAwake;
            player.IsDisabled = hasPhysics && !body.IsEnabled;
        }

        public static PhysicsBodySnapshot ReadPhysicsBody(Fantasy.PlayerSnapshot player)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            return new PhysicsBodySnapshot(
                checked((int)player.PlayerId),
                Fixed64.FromRaw(player.XRaw),
                Fixed64.FromRaw(player.YRaw),
                Fixed64.FromRaw(player.RotationRadiansRaw),
                Fixed64.FromRaw(player.LinearVelocityXRaw),
                Fixed64.FromRaw(player.LinearVelocityYRaw),
                Fixed64.FromRaw(player.AngularVelocityRaw),
                !player.IsAsleep,
                !player.IsDisabled);
        }
    }

    public readonly struct AttributeMergeResult
    {
        public AttributeMergeResult(
            PlayerAttributeSnapshot attributes,
            uint frameIndex,
            bool hasBaseline,
            bool applied,
            bool diverged)
        {
            Attributes = attributes;
            FrameIndex = frameIndex;
            HasBaseline = hasBaseline;
            Applied = applied;
            Diverged = diverged;
        }

        public PlayerAttributeSnapshot Attributes { get; }
        public uint FrameIndex { get; }
        public bool HasBaseline { get; }
        public bool Applied { get; }
        public bool Diverged { get; }
    }
}
