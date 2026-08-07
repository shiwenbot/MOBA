using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.SkillGraph;

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

    /// <summary>
    /// 生产与无头测试共用的快照协议映射。
    /// 测试通路必须调用这里，禁止在 harness 里另写一份转换。
    /// </summary>
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
                player.Attack,
                player.Stamina,
                player.MaxStamina);
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

        /// <summary>
        /// 把逻辑侧玩家态写入 wire 字段。生产 BroadcastSnapshot 与测试 ToProto 共用。
        /// </summary>
        public static Fantasy.PlayerSnapshot WritePlayer(
            PlayerStateSnapshot player,
            bool hasPhysics,
            PhysicsBodySnapshot body,
            uint latestAcceptedInputFrame,
            PlayerAttributeDirtyFlags dirtyMask,
            uint attributeBaselineFrameIndex,
            IReadOnlyList<Fantasy.BuffSnapshot> activeBuffs,
            long nextRuntimeBuffId,
            uint buffDirtyMask,
            uint buffSnapshotFrameIndex,
            bool isBuffFullSync)
        {
            PlayerAttributeSnapshot currentAttributes = player.Attributes;
            Fantasy.PlayerSnapshot wirePlayer = new Fantasy.PlayerSnapshot
            {
                PlayerId = player.PlayerId,
                XRaw = player.X.m_rawValue,
                YRaw = player.Y.m_rawValue,
                LatestAcceptedInputFrame = latestAcceptedInputFrame,
                AttributeDirtyMask = (uint)dirtyMask,
                AttributeBaselineFrameIndex = attributeBaselineFrameIndex,
                Health = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.Health,
                    currentAttributes.Health),
                MaxHealth = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.MaxHealth,
                    currentAttributes.MaxHealth),
                Mana = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.Mana,
                    currentAttributes.Mana),
                MaxMana = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.MaxMana,
                    currentAttributes.MaxMana),
                Attack = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.Attack,
                    currentAttributes.Attack),
                Stamina = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.Stamina,
                    currentAttributes.Stamina),
                MaxStamina = PlayerAttributeSync.SelectSerializedValue(
                    dirtyMask,
                    PlayerAttributeDirtyFlags.MaxStamina,
                    currentAttributes.MaxStamina),
                NextRuntimeBuffId = nextRuntimeBuffId,
                Numeric = WriteNumericSnapshot(player.Numeric),
                BuffDirtyMask = buffDirtyMask,
                BuffSnapshotFrameIndex = buffSnapshotFrameIndex,
                IsBuffFullSync = isBuffFullSync,
                StaminaRegenCounterFrames = player.StaminaRegenCounterFrames,
                DashVelocityXRaw = player.DashVelocityX.m_rawValue,
                DashVelocityYRaw = player.DashVelocityY.m_rawValue,
                DashRemainingFrames = player.DashRemainingFrames,
                DashRuntimeBuffId = player.DashRuntimeBuffId,
                KnockbackVelocityXRaw = player.KnockbackVelocityX.m_rawValue,
                KnockbackVelocityYRaw = player.KnockbackVelocityY.m_rawValue,
                KnockbackRemainingFrames = player.KnockbackRemainingFrames,
                KnockbackRuntimeBuffId = player.KnockbackRuntimeBuffId
            };

            if (activeBuffs != null)
            {
                for (int i = 0; i < activeBuffs.Count; i++)
                {
                    wirePlayer.ActiveBuffs.Add(activeBuffs[i]);
                }
            }

            List<Fantasy.ActiveSkillExecutionSnapshot> skillExecutions =
                WriteSkillExecutions(player.SkillExecutions);
            for (int i = 0; i < skillExecutions.Count; i++)
            {
                wirePlayer.SkillExecutions.Add(skillExecutions[i]);
            }

            WritePhysicsBody(wirePlayer, hasPhysics, body);
            return wirePlayer;
        }

        public static Fantasy.NumericSnapshot WriteNumericSnapshot(
            GameShared.FrameSync.Battle.NumericModifierSnapshot numericState)
        {
            Fantasy.NumericSnapshot snapshot = new Fantasy.NumericSnapshot
            {
                BaseHealth = numericState.BaseAttributes.Health,
                BaseMaxHealth = numericState.BaseAttributes.MaxHealth,
                BaseMana = numericState.BaseAttributes.Mana,
                BaseMaxMana = numericState.BaseAttributes.MaxMana,
                BaseAttack = numericState.BaseAttributes.Attack,
                BaseStamina = numericState.BaseAttributes.Stamina,
                BaseMaxStamina = numericState.BaseAttributes.MaxStamina
            };

            for (int i = 0; i < numericState.Modifiers.Count; i++)
            {
                NumericModifier modifier = numericState.Modifiers[i];
                snapshot.Modifiers.Add(new Fantasy.NumericModifierSnapshot
                {
                    SourceBuffId = modifier.SourceBuffId,
                    ValueType = (uint)modifier.ValueType,
                    AttributeKind = (uint)modifier.AttributeKind,
                    Value = modifier.Value
                });
            }

            return snapshot;
        }

        public static GameShared.FrameSync.Battle.NumericModifierSnapshot ReadNumericSnapshot(
            Fantasy.NumericSnapshot numeric,
            PlayerAttributeSnapshot fallbackAttributes)
        {
            if (numeric == null)
            {
                return GameShared.FrameSync.Battle.NumericModifierSnapshot.FromAttributes(fallbackAttributes);
            }

            NumericModifier[] modifiers = new NumericModifier[numeric.Modifiers.Count];
            for (int i = 0; i < numeric.Modifiers.Count; i++)
            {
                Fantasy.NumericModifierSnapshot modifier = numeric.Modifiers[i];
                modifiers[i] = new NumericModifier(
                    modifier.SourceBuffId,
                    (ModifierValueType)modifier.ValueType,
                    (AttributeKind)modifier.AttributeKind,
                    modifier.Value);
            }

            return new GameShared.FrameSync.Battle.NumericModifierSnapshot(
                new PlayerAttributeSnapshot(
                    numeric.BaseHealth,
                    numeric.BaseMaxHealth,
                    numeric.BaseMana,
                    numeric.BaseMaxMana,
                    numeric.BaseAttack,
                    numeric.BaseStamina,
                    numeric.BaseMaxStamina),
                modifiers);
        }

        public static List<Fantasy.ActiveSkillExecutionSnapshot> WriteSkillExecutions(
            IReadOnlyDictionary<long, GameShared.SkillGraph.ActiveSkillExecutionSnapshot> executions)
        {
            List<Fantasy.ActiveSkillExecutionSnapshot> snapshots =
                new List<Fantasy.ActiveSkillExecutionSnapshot>();
            if (executions == null || executions.Count == 0)
            {
                return snapshots;
            }

            List<long> casterIds = new List<long>(executions.Keys);
            casterIds.Sort();
            for (int i = 0; i < casterIds.Count; i++)
            {
                GameShared.SkillGraph.ActiveSkillExecutionSnapshot execution = executions[casterIds[i]];
                SkillExecutionSnapshot runner = execution.RunnerSnapshot;
                SkillBlackboardSnapshot blackboard = runner.Blackboard ?? new SkillBlackboardSnapshot();
                Fantasy.ActiveSkillExecutionSnapshot wire = new Fantasy.ActiveSkillExecutionSnapshot
                {
                    CasterId = execution.CasterId,
                    TargetId = execution.TargetId,
                    SkillId = execution.SkillId,
                    CurrentNodeId = runner.CurrentNodeId,
                    Status = (uint)runner.Status,
                    ExecutedSteps = runner.ExecutedSteps,
                    FrameIndex = runner.FrameIndex,
                    Message = runner.Message ?? string.Empty,
                    DirectionXRaw = execution.DirectionX.m_rawValue,
                    DirectionYRaw = execution.DirectionY.m_rawValue
                };

                WriteStringValues(blackboard.Strings, wire.Strings);
                WriteFloatValues(blackboard.Floats, wire.Floats);
                WriteIntValues(blackboard.Ints, wire.Ints);
                WriteBoolValues(blackboard.Bools, wire.Bools);
                WriteDelayValues(runner.DelayRemainingFrames, wire.DelayRemainingFrames);
                snapshots.Add(wire);
            }

            return snapshots;
        }

        public static Dictionary<long, GameShared.SkillGraph.ActiveSkillExecutionSnapshot> ReadSkillExecutions(
            IReadOnlyList<Fantasy.ActiveSkillExecutionSnapshot> executions)
        {
            Dictionary<long, GameShared.SkillGraph.ActiveSkillExecutionSnapshot> snapshots =
                new Dictionary<long, GameShared.SkillGraph.ActiveSkillExecutionSnapshot>();
            if (executions == null)
            {
                return snapshots;
            }

            for (int i = 0; i < executions.Count; i++)
            {
                Fantasy.ActiveSkillExecutionSnapshot wire = executions[i];
                SkillBlackboardSnapshot blackboard = new SkillBlackboardSnapshot();
                ReadStringValues(wire.Strings, blackboard.Strings);
                ReadFloatValues(wire.Floats, blackboard.Floats);
                ReadIntValues(wire.Ints, blackboard.Ints);
                ReadBoolValues(wire.Bools, blackboard.Bools);

                Dictionary<int, int> delayRemainingFrames = new Dictionary<int, int>();
                if (wire.DelayRemainingFrames != null)
                {
                    for (int delayIndex = 0; delayIndex < wire.DelayRemainingFrames.Count; delayIndex++)
                    {
                        Fantasy.SkillDelaySnapshot delay = wire.DelayRemainingFrames[delayIndex];
                        delayRemainingFrames[delay.NodeId] = delay.RemainingFrames;
                    }
                }

                SkillExecutionSnapshot runner = new SkillExecutionSnapshot
                {
                    CurrentNodeId = wire.CurrentNodeId,
                    Status = (SkillExecutionStatus)wire.Status,
                    ExecutedSteps = wire.ExecutedSteps,
                    FrameIndex = wire.FrameIndex,
                    Message = wire.Message ?? string.Empty,
                    Blackboard = blackboard,
                    DelayRemainingFrames = delayRemainingFrames
                };
                snapshots[wire.CasterId] = new GameShared.SkillGraph.ActiveSkillExecutionSnapshot(
                    wire.CasterId,
                    wire.TargetId,
                    wire.SkillId,
                    runner,
                    Fixed64.FromRaw(wire.DirectionXRaw),
                    Fixed64.FromRaw(wire.DirectionYRaw));
            }

            return snapshots;
        }

        public static BuffState[] ReadBuffStates(IReadOnlyList<Fantasy.BuffSnapshot> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return Array.Empty<BuffState>();
            }

            BuffState[] states = new BuffState[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                Fantasy.BuffSnapshot buff = buffs[i];
                states[i] = new BuffState(
                    buff.RuntimeBuffId,
                    buff.BuffId,
                    buff.CasterId,
                    buff.TargetId,
                    buff.StackCount,
                    buff.RemainingFrames,
                    buff.AppliedFrame,
                    (BuffFlags)buff.Flags);
            }

            return states;
        }

        public static BuffSync.BuffChange[] ReadBuffChanges(IReadOnlyList<Fantasy.BuffSnapshot> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return Array.Empty<BuffSync.BuffChange>();
            }

            BuffSync.BuffChange[] changes = new BuffSync.BuffChange[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                Fantasy.BuffSnapshot buff = buffs[i];
                BuffDirtyFlags dirtyFlags = (BuffDirtyFlags)buff.DirtyFlags;
                if (dirtyFlags == BuffDirtyFlags.None)
                {
                    dirtyFlags = BuffDirtyFlags.Updated;
                }

                changes[i] = new BuffSync.BuffChange(
                    new BuffState(
                        buff.RuntimeBuffId,
                        buff.BuffId,
                        buff.CasterId,
                        buff.TargetId,
                        buff.StackCount,
                        buff.RemainingFrames,
                        buff.AppliedFrame,
                        (BuffFlags)buff.Flags),
                    dirtyFlags);
            }

            return changes;
        }

        public static List<Fantasy.BuffSnapshot> WriteBuffStates(IReadOnlyList<BuffState> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return new List<Fantasy.BuffSnapshot>();
            }

            List<Fantasy.BuffSnapshot> snapshots = new List<Fantasy.BuffSnapshot>(buffs.Count);
            for (int i = 0; i < buffs.Count; i++)
            {
                BuffState buff = buffs[i];
                snapshots.Add(new Fantasy.BuffSnapshot
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

        /// <summary>
        /// 无头测试默认走全量同步。目标是守精度，不守增量协议（增量见 B1）。
        /// </summary>
        public static Fantasy.S2C_FrameSnapshot ToFullSyncProto(
            BattleWorldSnapshot snapshot,
            Func<long, uint> latestAcceptedInputFrameProvider = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Fantasy.S2C_FrameSnapshot frameSnapshot = new Fantasy.S2C_FrameSnapshot
            {
                FrameIndex = snapshot.FrameIndex
            };

            Dictionary<int, PhysicsBodySnapshot> physicsByBodyId = BuildPhysicsBodyLookup(snapshot.PhysicsSnapshot);
            IReadOnlyList<PlayerStateSnapshot> players = snapshot.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                int bodyId = checked((int)player.PlayerId);
                bool hasPhysics = physicsByBodyId.TryGetValue(bodyId, out PhysicsBodySnapshot bodySnapshot);
                uint latestAcceptedInputFrame = latestAcceptedInputFrameProvider != null
                    ? latestAcceptedInputFrameProvider(player.PlayerId)
                    : 0u;
                List<Fantasy.BuffSnapshot> buffs = WriteBuffStates(player.ActiveBuffs);
                Fantasy.PlayerSnapshot wirePlayer = WritePlayer(
                    player,
                    hasPhysics,
                    bodySnapshot,
                    latestAcceptedInputFrame,
                    PlayerAttributeDirtyFlags.All,
                    snapshot.FrameIndex,
                    buffs,
                    player.NextRuntimeBuffId,
                    0u,
                    snapshot.FrameIndex,
                    true);
                frameSnapshot.Players.Add(wirePlayer);
            }

            if (snapshot.PhysicsSnapshot != null)
            {
                for (int i = 0; i < snapshot.PhysicsSnapshot.Contacts.Count; i++)
                {
                    PhysicsContactSnapshot contact = snapshot.PhysicsSnapshot.Contacts[i];
                    frameSnapshot.Contacts.Add(new Fantasy.FrameContactSnapshot
                    {
                        BodyAId = contact.BodyAId,
                        BodyBId = contact.BodyBId,
                        IsTouching = contact.IsTouching
                    });
                }
            }

            return frameSnapshot;
        }

        /// <summary>
        /// 全量反序列化路径，与客户端 ConvertSnapshot 在 dirtyMask=All / IsBuffFullSync 时语义一致。
        /// 无头测试默认走全量，因此这里不维护属性/Buff 增量 baseline。
        /// </summary>
        public static BattleWorldSnapshot FromFullSyncProto(Fantasy.S2C_FrameSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            PlayerStateSnapshot[] players = new PlayerStateSnapshot[snapshot.Players.Count];
            PhysicsBodySnapshot[] bodies = new PhysicsBodySnapshot[snapshot.Players.Count];
            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                Fantasy.PlayerSnapshot player = snapshot.Players[i];
                PlayerAttributeDirtyFlags dirtyMask = (PlayerAttributeDirtyFlags)player.AttributeDirtyMask;
                if (!IsFullAttributeSnapshot(dirtyMask) && dirtyMask != PlayerAttributeDirtyFlags.None)
                {
                    throw new InvalidOperationException(
                        "FromFullSyncProto only accepts full-sync attribute packets. Use the client ConvertSnapshot path for deltas.");
                }

                AttributeMergeResult attributeMerge = MergeAttributes(
                    snapshot.FrameIndex,
                    player,
                    false,
                    PlayerAttributeSnapshot.Default,
                    0u);
                PlayerAttributeSnapshot attributes = attributeMerge.Attributes;
                if (!player.IsBuffFullSync && player.BuffDirtyMask != 0)
                {
                    throw new InvalidOperationException(
                        "FromFullSyncProto only accepts full-sync buff packets. Use the client ConvertSnapshot path for deltas.");
                }

                BuffState[] buffs = player.IsBuffFullSync || player.BuffDirtyMask == 0
                    ? ReadBuffStates(player.ActiveBuffs)
                    : Array.Empty<BuffState>();
                long nextRuntimeBuffId = player.IsBuffFullSync
                    ? (player.NextRuntimeBuffId > 0 ? player.NextRuntimeBuffId : 1L)
                    : 1L;
                players[i] = new PlayerStateSnapshot(
                    player.PlayerId,
                    Fixed64.FromRaw(player.XRaw),
                    Fixed64.FromRaw(player.YRaw),
                    attributes,
                    buffs,
                    nextRuntimeBuffId,
                    ReadNumericSnapshot(player.Numeric, attributes),
                    player.StaminaRegenCounterFrames,
                    Fixed64.FromRaw(player.DashVelocityXRaw),
                    Fixed64.FromRaw(player.DashVelocityYRaw),
                    player.DashRemainingFrames,
                    player.DashRuntimeBuffId,
                    Fixed64.FromRaw(player.KnockbackVelocityXRaw),
                    Fixed64.FromRaw(player.KnockbackVelocityYRaw),
                    player.KnockbackRemainingFrames,
                    player.KnockbackRuntimeBuffId,
                    ReadSkillExecutions(player.SkillExecutions));
                bodies[i] = ReadPhysicsBody(player);
            }

            Array.Sort(players, PlayerStateSnapshotIdComparer.Instance);
            Array.Sort(bodies, PhysicsBodyIdComparer.Instance);

            PhysicsContactSnapshot[] contacts = new PhysicsContactSnapshot[snapshot.Contacts.Count];
            for (int i = 0; i < snapshot.Contacts.Count; i++)
            {
                Fantasy.FrameContactSnapshot contact = snapshot.Contacts[i];
                contacts[i] = new PhysicsContactSnapshot(contact.BodyAId, contact.BodyBId, contact.IsTouching);
            }

            return new BattleWorldSnapshot(
                snapshot.FrameIndex,
                players,
                new PhysicsWorldSnapshot(bodies, contacts));
        }

        public static Dictionary<int, PhysicsBodySnapshot> BuildPhysicsBodyLookup(PhysicsWorldSnapshot physicsSnapshot)
        {
            Dictionary<int, PhysicsBodySnapshot> lookup = new Dictionary<int, PhysicsBodySnapshot>();
            if (physicsSnapshot == null)
            {
                return lookup;
            }

            for (int i = 0; i < physicsSnapshot.Bodies.Count; i++)
            {
                PhysicsBodySnapshot body = physicsSnapshot.Bodies[i];
                lookup[body.BodyId] = body;
            }

            return lookup;
        }

        private static void WriteStringValues(
            IReadOnlyDictionary<string, string> source,
            ICollection<Fantasy.SkillStringValueSnapshot> destination)
        {
            if (source == null)
            {
                return;
            }

            List<string> keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                destination.Add(new Fantasy.SkillStringValueSnapshot
                {
                    Key = key,
                    Value = source[key] ?? string.Empty
                });
            }
        }

        private static void WriteFloatValues(
            IReadOnlyDictionary<string, float> source,
            ICollection<Fantasy.SkillFloatValueSnapshot> destination)
        {
            if (source == null)
            {
                return;
            }

            List<string> keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                destination.Add(new Fantasy.SkillFloatValueSnapshot { Key = key, Value = source[key] });
            }
        }

        private static void WriteIntValues(
            IReadOnlyDictionary<string, int> source,
            ICollection<Fantasy.SkillIntValueSnapshot> destination)
        {
            if (source == null)
            {
                return;
            }

            List<string> keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                destination.Add(new Fantasy.SkillIntValueSnapshot { Key = key, Value = source[key] });
            }
        }

        private static void WriteBoolValues(
            IReadOnlyDictionary<string, bool> source,
            ICollection<Fantasy.SkillBoolValueSnapshot> destination)
        {
            if (source == null)
            {
                return;
            }

            List<string> keys = new List<string>(source.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                destination.Add(new Fantasy.SkillBoolValueSnapshot { Key = key, Value = source[key] });
            }
        }

        private static void WriteDelayValues(
            IReadOnlyDictionary<int, int> source,
            ICollection<Fantasy.SkillDelaySnapshot> destination)
        {
            if (source == null)
            {
                return;
            }

            List<int> nodeIds = new List<int>(source.Keys);
            nodeIds.Sort();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                int nodeId = nodeIds[i];
                destination.Add(new Fantasy.SkillDelaySnapshot
                {
                    NodeId = nodeId,
                    RemainingFrames = source[nodeId]
                });
            }
        }

        private static void ReadStringValues(
            IReadOnlyList<Fantasy.SkillStringValueSnapshot> source,
            IDictionary<string, string> destination)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                destination[source[i].Key ?? string.Empty] = source[i].Value ?? string.Empty;
            }
        }

        private static void ReadFloatValues(
            IReadOnlyList<Fantasy.SkillFloatValueSnapshot> source,
            IDictionary<string, float> destination)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                destination[source[i].Key ?? string.Empty] = source[i].Value;
            }
        }

        private static void ReadIntValues(
            IReadOnlyList<Fantasy.SkillIntValueSnapshot> source,
            IDictionary<string, int> destination)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                destination[source[i].Key ?? string.Empty] = source[i].Value;
            }
        }

        private static void ReadBoolValues(
            IReadOnlyList<Fantasy.SkillBoolValueSnapshot> source,
            IDictionary<string, bool> destination)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                destination[source[i].Key ?? string.Empty] = source[i].Value;
            }
        }

        private sealed class PlayerStateSnapshotIdComparer : IComparer<PlayerStateSnapshot>
        {
            public static readonly PlayerStateSnapshotIdComparer Instance = new PlayerStateSnapshotIdComparer();

            public int Compare(PlayerStateSnapshot x, PlayerStateSnapshot y)
            {
                return x.PlayerId.CompareTo(y.PlayerId);
            }
        }

        private sealed class PhysicsBodyIdComparer : IComparer<PhysicsBodySnapshot>
        {
            public static readonly PhysicsBodyIdComparer Instance = new PhysicsBodyIdComparer();

            public int Compare(PhysicsBodySnapshot x, PhysicsBodySnapshot y)
            {
                return x.BodyId.CompareTo(y.BodyId);
            }
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
