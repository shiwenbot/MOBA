using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using Fantasy.Network;
using Fantasy.Serialize;

namespace Fantasy;

public sealed class BattleComponent : Entitas.Entity, ITickable
{
    private readonly BattleAutomationServerConfig _automationConfig = BattleAutomationServerConfig.Current;
    private readonly BattleLogic _battleLogic = new(null, Log.Warning);
    private readonly Dictionary<long, PlayerSession> _sessionsByPlayerId = new();
    private readonly Dictionary<long, long> _playerIdBySessionId = new();
    private readonly Dictionary<long, PlayerAttributeSnapshot> _lastBroadcastAttributesByPlayerId = new();
    private readonly Dictionary<(long ObserverSessionId, long TargetPlayerId), BuffBroadcastBaseline> _buffBaselinesByObserverTarget = new();
    private readonly List<long> _playerIdBuffer = new();
    private readonly List<(long ObserverSessionId, long TargetPlayerId)> _staleBuffBaselineKeys = new();

    private readonly BattleBandwidthConfig _bandwidthConfig = BattleBandwidthConfig.Current;
    private readonly BattleBandwidthStats _bandwidthStats = new(BattleBandwidthConfig.Current.ReportIntervalFrames);
    private readonly MemoryStreamBuffer _bandwidthMeasureBuffer = new();

    /// <summary>带宽统计累积器。报告窗口数据从这里读取（含对照基线）。</summary>
    public BattleBandwidthStats BandwidthStats => _bandwidthStats;

    /// <summary>带宽统计总开关是否开启。</summary>
    public bool BandwidthStatsEnabled => _bandwidthConfig.Enabled;

    /// <summary>对照测量是否开启（未开启则算不出「省了多少」）。</summary>
    public bool BandwidthMeasureFullSyncBaseline => _bandwidthConfig.MeasureFullSyncBaseline;

    public int HashReportsMatched => _battleLogic.HashReportsMatched;
    public int HashMismatchCount => _battleLogic.HashMismatchCount;
    public int HashNoRecordCount => _battleLogic.HashNoRecordCount;

    private long _nextPlayerId = 1;
    private bool _automationPlayerThresholdObserved;
    private bool _automationBuffCommandsQueued;
    private uint _automationBuffApplyFrame;

    public int Priority => 0;
    public uint LastFrameIndex => _battleLogic.LastFrameIndex;

    public BattleComponent()
    {
        _battleLogic.OnBroadcast = BroadcastSnapshot;
    }

    public PlayerState Join(Session session)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        CleanupDisconnectedPlayers();

        if (_playerIdBySessionId.TryGetValue(session.Id, out long existingPlayerId) &&
            _battleLogic.TryGetPlayer(existingPlayerId, out PlayerState existingState))
        {
            if (_sessionsByPlayerId.TryGetValue(existingPlayerId, out PlayerSession? existingPlayerSession))
            {
                existingPlayerSession!.Session = session;
            }

            return existingState;
        }

        long playerId = _nextPlayerId++;
        (Fixed64 spawnX, Fixed64 spawnY) = GetSpawnPosition(_sessionsByPlayerId.Count);
        PlayerState newState = _battleLogic.JoinPlayer(playerId, spawnX, spawnY);

        _sessionsByPlayerId[playerId] = new PlayerSession(playerId, session);
        _playerIdBySessionId[session.Id] = playerId;

        return newState;
    }

    public void SubmitInput(Session session, C2B_PlayerInput input)
    {
        if (session == null || input == null)
        {
            return;
        }

        if (!_playerIdBySessionId.TryGetValue(session.Id, out long playerId))
        {
            return;
        }

        _battleLogic.SubmitInput(playerId, input.FrameIndex, input.InputSeq, input.Dx, input.Dy, input.SkillId);
    }

    public void SubmitStateHashReport(Session session, C2B_StateHashReport report)
    {
        if (session == null || report == null)
        {
            return;
        }

        if (!_playerIdBySessionId.TryGetValue(session.Id, out long playerId))
        {
            return;
        }

        _battleLogic.TryCompareReportedHash(playerId, report.FrameIndex, report.StateHash);
    }

    public void Tick(uint frameIndex, Fixed64 fixedDt)
    {
        CleanupDisconnectedPlayers();
        RunAutomationScenario(frameIndex);
        _battleLogic.Tick(frameIndex, fixedDt);
    }

    private void CleanupDisconnectedPlayers()
    {
        _playerIdBuffer.Clear();

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                _playerIdBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];

            if (_sessionsByPlayerId.TryGetValue(playerId, out PlayerSession? playerSession))
            {
                long sessionId = playerSession!.SessionId;
                if (sessionId != 0)
                {
                    _playerIdBySessionId.Remove(sessionId);
                    RemoveBuffBroadcastBaselines(sessionId, 0);
                }
            }

            _sessionsByPlayerId.Remove(playerId);
            _lastBroadcastAttributesByPlayerId.Remove(playerId);
            RemoveBuffBroadcastBaselines(0, playerId);
            _battleLogic.RemovePlayer(playerId);
        }
    }

    private void BroadcastSnapshot(TestSnapshot snapshot)
    {
        if (_sessionsByPlayerId.Count == 0)
        {
            return;
        }

        Dictionary<int, PhysicsBodySnapshot> physicsByBodyId = BuildPhysicsBodyLookup(snapshot.PhysicsSnapshot);
        bool measureBandwidth = _bandwidthConfig.Enabled;
        if (measureBandwidth)
        {
            _bandwidthStats.RecordSnapshotTick();
        }

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            S2C_FrameSnapshot frameSnapshot = new S2C_FrameSnapshot
            {
                FrameIndex = snapshot.FrameIndex
            };
            int dirtyAttributeEntries = 0;
            int buffFullSyncEntries = 0;

            for (int i = 0; i < snapshot.Players.Length; i++)
            {
                PlayerStateSnapshot player = snapshot.Players[i];
                int bodyId = checked((int)player.PlayerId);
                bool hasPhysics = physicsByBodyId.TryGetValue(bodyId, out PhysicsBodySnapshot bodySnapshot);
                bool hasPreviousAttributes = _lastBroadcastAttributesByPlayerId.TryGetValue(player.PlayerId, out PlayerAttributeSnapshot previousAttributes);
                PlayerAttributeSnapshot currentAttributes = player.Attributes;
                BuffSyncPayload buffPayload = BuildBuffSyncPayload(session.Id, player, snapshot.FrameIndex);
                PlayerAttributeDirtyFlags dirtyMask = buffPayload.IsFullSync || !hasPreviousAttributes
                    ? PlayerAttributeDirtyFlags.All
                    : PlayerAttributeSync.ComputeDirtyMask(true, previousAttributes, currentAttributes);

                if (measureBandwidth)
                {
                    if (dirtyMask != PlayerAttributeDirtyFlags.None)
                    {
                        dirtyAttributeEntries++;
                    }

                    if (buffPayload.IsFullSync)
                    {
                        buffFullSyncEntries++;
                    }
                }

                frameSnapshot.Players.Add(new PlayerSnapshot
                {
                    PlayerId = player.PlayerId,
                    XRaw = player.X.m_rawValue,
                    YRaw = player.Y.m_rawValue,
                    LatestAcceptedInputFrame = _battleLogic.GetLatestAcceptedInputFrame(player.PlayerId),
                    LinearVelocityXRaw = hasPhysics ? bodySnapshot.LinearVelocityX.m_rawValue : 0L,
                    LinearVelocityYRaw = hasPhysics ? bodySnapshot.LinearVelocityY.m_rawValue : 0L,
                    AttributeDirtyMask = (uint)dirtyMask,
                    Health = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.Health, currentAttributes.Health),
                    MaxHealth = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.MaxHealth, currentAttributes.MaxHealth),
                    Mana = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.Mana, currentAttributes.Mana),
                    MaxMana = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.MaxMana, currentAttributes.MaxMana),
                    Attack = PlayerAttributeSync.SelectSerializedValue(dirtyMask, PlayerAttributeDirtyFlags.Attack, currentAttributes.Attack),
                    ActiveBuffs = buffPayload.Buffs,
                    NextRuntimeBuffId = buffPayload.NextRuntimeBuffId,
                    Numeric = BuildNumericSnapshot(player.Numeric),
                    BuffDirtyMask = buffPayload.DirtyMask,
                    BuffSnapshotFrameIndex = buffPayload.BaselineFrameIndex,
                    IsBuffFullSync = buffPayload.IsFullSync
                });
            }

            if (snapshot.PhysicsSnapshot != null)
            {
                for (int i = 0; i < snapshot.PhysicsSnapshot.Contacts.Count; i++)
                {
                    PhysicsContactSnapshot contact = snapshot.PhysicsSnapshot.Contacts[i];
                    frameSnapshot.Contacts.Add(new FrameContactSnapshot
                    {
                        BodyAId = contact.BodyAId,
                        BodyBId = contact.BodyBId,
                        IsTouching = contact.IsTouching
                    });
                }
            }

            if (measureBandwidth)
            {
                _bandwidthStats.RecordSend(
                    MeasureSerializedBytes(frameSnapshot),
                    frameSnapshot.Players.Count,
                    frameSnapshot.Contacts.Count,
                    dirtyAttributeEntries,
                    buffFullSyncEntries);

                if (_bandwidthConfig.MeasureFullSyncBaseline)
                {
                    _bandwidthStats.RecordFullSyncCounterfactual(MeasureFullSyncBytes(frameSnapshot));
                }
            }

            session.Send(frameSnapshot);
        }

        if (_bandwidthStats.TryConsumeReportDue(snapshot.FrameIndex))
        {
            // 到点：先把当前窗口的统计推送给每个客户端，再打日志、再清窗口。
            // 推送不依赖总开关——开关关着时也推一条带标记的消息，客户端据此显示「统计未开启」。
            S2C_BandwidthStats statsMessage = BuildBandwidthStatsMessage(snapshot.FrameIndex);
            foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
            {
                Session session = pair.Value.Session;
                if (session != null && !session.IsDisposed)
                {
                    session.Send(statsMessage);
                }
            }

            if (measureBandwidth)
            {
                Log.Info(_bandwidthStats.BuildReport(snapshot.FrameIndex, _sessionsByPlayerId.Count));
            }

            _bandwidthStats.ResetWindow();
        }

        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            PlayerStateSnapshot player = snapshot.Players[i];
            _lastBroadcastAttributesByPlayerId[player.PlayerId] = player.Attributes;
        }
    }

    /// <summary>
    /// 用真实序列化器量一遍消息体字节数。复用同一个 buffer，避免每帧分配。
    /// </summary>
    private int MeasureSerializedBytes(S2C_FrameSnapshot frameSnapshot)
    {
        _bandwidthMeasureBuffer.SetLength(0);
        _bandwidthMeasureBuffer.Position = 0;
        SerializerManager.ProtoBufHelper.Serialize(frameSnapshot, _bandwidthMeasureBuffer);
        return (int)_bandwidthMeasureBuffer.Length;
    }

    /// <summary>
    /// 对照测量：把属性掩码强制为全量、Buff 强制为全量后再量一次，用来算脏同步省了多少。
    /// 只改用于测量的临时对象，不影响真正发出去的那条消息。
    /// </summary>
    private int MeasureFullSyncBytes(S2C_FrameSnapshot frameSnapshot)
    {
        S2C_FrameSnapshot fullSync = new S2C_FrameSnapshot
        {
            FrameIndex = frameSnapshot.FrameIndex
        };

        for (int i = 0; i < frameSnapshot.Players.Count; i++)
        {
            PlayerSnapshot source = frameSnapshot.Players[i];
            long playerId = source.PlayerId;
            bool hasAttributes = _lastBroadcastAttributesByPlayerId.TryGetValue(playerId, out PlayerAttributeSnapshot attributes);

            fullSync.Players.Add(new PlayerSnapshot
            {
                PlayerId = source.PlayerId,
                XRaw = source.XRaw,
                YRaw = source.YRaw,
                LatestAcceptedInputFrame = source.LatestAcceptedInputFrame,
                LinearVelocityXRaw = source.LinearVelocityXRaw,
                LinearVelocityYRaw = source.LinearVelocityYRaw,
                AttributeDirtyMask = (uint)PlayerAttributeDirtyFlags.All,
                Health = hasAttributes ? attributes.Health : source.Health,
                MaxHealth = hasAttributes ? attributes.MaxHealth : source.MaxHealth,
                Mana = hasAttributes ? attributes.Mana : source.Mana,
                MaxMana = hasAttributes ? attributes.MaxMana : source.MaxMana,
                Attack = hasAttributes ? attributes.Attack : source.Attack,
                ActiveBuffs = source.ActiveBuffs,
                NextRuntimeBuffId = source.NextRuntimeBuffId,
                Numeric = source.Numeric,
                BuffDirtyMask = uint.MaxValue,
                BuffSnapshotFrameIndex = source.BuffSnapshotFrameIndex,
                IsBuffFullSync = true
            });
        }

        for (int i = 0; i < frameSnapshot.Contacts.Count; i++)
        {
            fullSync.Contacts.Add(frameSnapshot.Contacts[i]);
        }

        return MeasureSerializedBytes(fullSync);
    }

    /// <summary>
    /// 把当前窗口的带宽统计打包成下推消息。节省量算法与 <see cref="BattleBandwidthStats.BuildReport"/> 末尾一致：
    /// saved = fullSyncPayload - actualPayload；ratio = saved / fullSyncPayload * 100。
    /// 总开关关闭时只填标记位，actual/fullSync 都为 0。
    /// </summary>
    private S2C_BandwidthStats BuildBandwidthStatsMessage(uint frameIndex)
    {
        S2C_BandwidthStats message = new S2C_BandwidthStats();
        message.FrameIndex = frameIndex;
        message.HasSamples = _bandwidthStats.HasSamples;
        message.MeasureFullSyncBaseline = _bandwidthConfig.MeasureFullSyncBaseline;

        if (!message.HasSamples || !_bandwidthConfig.Enabled)
        {
            message.ActualPayloadBytes = 0;
            message.FullSyncPayloadBytes = 0;
            message.DirtySyncSavedRatio = 0.0;
            message.DirtySyncSavedBytes = 0;
            return message;
        }

        long actual = _bandwidthStats.TotalPayloadBytes;
        long fullSync = _bandwidthStats.TotalFullSyncPayloadBytes;
        message.ActualPayloadBytes = actual;
        message.FullSyncPayloadBytes = fullSync;

        if (_bandwidthConfig.MeasureFullSyncBaseline && _bandwidthStats.HasFullSyncBaseline && fullSync > 0)
        {
            long saved = fullSync - actual;
            message.DirtySyncSavedBytes = saved;
            message.DirtySyncSavedRatio = (double)saved / fullSync * 100.0;
        }
        else
        {
            message.DirtySyncSavedBytes = 0;
            message.DirtySyncSavedRatio = 0.0;
        }

        return message;
    }

    private static Dictionary<int, PhysicsBodySnapshot> BuildPhysicsBodyLookup(PhysicsWorldSnapshot physicsSnapshot)
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
                DirtyFlags = 0
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

    private static NumericSnapshot BuildNumericSnapshot(GameShared.FrameSync.Battle.NumericModifierSnapshot numericState)
    {
        NumericSnapshot snapshot = new NumericSnapshot
        {
            BaseHealth = numericState.BaseAttributes.Health,
            BaseMaxHealth = numericState.BaseAttributes.MaxHealth,
            BaseMana = numericState.BaseAttributes.Mana,
            BaseMaxMana = numericState.BaseAttributes.MaxMana,
            BaseAttack = numericState.BaseAttributes.Attack
        };

        for (int i = 0; i < numericState.Modifiers.Count; i++)
        {
            NumericModifier modifier = numericState.Modifiers[i];
            snapshot.Modifiers.Add(new NumericModifierSnapshot
            {
                SourceBuffId = modifier.SourceBuffId,
                ValueType = (uint)modifier.ValueType,
                AttributeKind = (uint)modifier.AttributeKind,
                Value = modifier.Value
            });
        }

        return snapshot;
    }

    private BuffSyncPayload BuildBuffSyncPayload(long observerSessionId, PlayerStateSnapshot player, uint frameIndex)
    {
        (long ObserverSessionId, long TargetPlayerId) key = (observerSessionId, player.PlayerId);
        if (!_buffBaselinesByObserverTarget.TryGetValue(key, out BuffBroadcastBaseline? baseline))
        {
            BuffBroadcastBaseline fullSyncBaseline = new BuffBroadcastBaseline(player.ActiveBuffs, player.NextRuntimeBuffId, frameIndex);
            _buffBaselinesByObserverTarget[key] = fullSyncBaseline;
            return new BuffSyncPayload(
                BuildBuffSnapshots(player.ActiveBuffs),
                player.NextRuntimeBuffId,
                0u,
                0u,
                true);
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

    private void RemoveBuffBroadcastBaselines(long observerSessionId, long targetPlayerId)
    {
        if (_buffBaselinesByObserverTarget.Count == 0)
        {
            return;
        }

        _staleBuffBaselineKeys.Clear();
        foreach (KeyValuePair<(long ObserverSessionId, long TargetPlayerId), BuffBroadcastBaseline> pair in _buffBaselinesByObserverTarget)
        {
            bool matchesObserver = observerSessionId == 0 || pair.Key.ObserverSessionId == observerSessionId;
            bool matchesTarget = targetPlayerId == 0 || pair.Key.TargetPlayerId == targetPlayerId;
            if (matchesObserver && matchesTarget)
            {
                _staleBuffBaselineKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < _staleBuffBaselineKeys.Count; i++)
        {
            _buffBaselinesByObserverTarget.Remove(_staleBuffBaselineKeys[i]);
        }

        _staleBuffBaselineKeys.Clear();
    }

    private void RunAutomationScenario(uint frameIndex)
    {
        if (!_automationConfig.IsTimedBuffScenario)
        {
            return;
        }

        if (!_automationPlayerThresholdObserved && _sessionsByPlayerId.Count >= _automationConfig.MinimumPlayerCount)
        {
            _automationPlayerThresholdObserved = true;
            _automationBuffApplyFrame = unchecked(frameIndex + _automationConfig.BuffApplyDelayFrames);
            Log.Warning(
                $"[Automation][BattleServer] Observed target players. scenario={_automationConfig.Scenario} applyFrame={_automationBuffApplyFrame} playerCount={_sessionsByPlayerId.Count}");
        }

        if (!_automationPlayerThresholdObserved ||
            _automationBuffCommandsQueued ||
            frameIndex < _automationBuffApplyFrame)
        {
            return;
        }

        _playerIdBuffer.Clear();
        foreach (long playerId in _sessionsByPlayerId.Keys)
        {
            _playerIdBuffer.Add(playerId);
        }

        _playerIdBuffer.Sort();
        if (_automationConfig.IsSkillBuffScenario)
        {
            for (int i = 0; i < _playerIdBuffer.Count; i++)
            {
                long playerId = _playerIdBuffer[i];
                uint automationInputSeq = unchecked(frameIndex + 1024u);
                _battleLogic.SubmitInput(
                    playerId,
                    frameIndex,
                    automationInputSeq,
                    0.0f,
                    0.0f,
                    _automationConfig.ExpectedSkillId);
            }

            _automationBuffCommandsQueued = true;
            Log.Warning(
                $"[Automation][BattleServer] Queued skill buff commands. scenario={_automationConfig.Scenario} skillId={_automationConfig.ExpectedSkillId} buffId={_automationConfig.ExpectedBuffId} players={_playerIdBuffer.Count}");
            return;
        }

        if (_automationConfig.IsBuffStackScenario)
        {
            QueueBuffForAllPlayers(frameIndex, DefaultBuffConfigProvider.StackTestBuffId, _automationConfig.BuffDurationFrames, 1);
            QueueBuffForAllPlayers(unchecked(frameIndex + 1u), DefaultBuffConfigProvider.StackTestBuffId, _automationConfig.BuffDurationFrames, 1);
            QueueBuffForAllPlayers(unchecked(frameIndex + 2u), DefaultBuffConfigProvider.StackTestBuffId, _automationConfig.BuffDurationFrames, 1);
            _automationBuffCommandsQueued = true;
            Log.Warning(
                $"[Automation][BattleServer] Queued buff stack commands. scenario={_automationConfig.Scenario} buffId={DefaultBuffConfigProvider.StackTestBuffId} players={_playerIdBuffer.Count}");
            return;
        }

        if (_automationConfig.IsBuffRefreshScenario)
        {
            QueueBuffForAllPlayers(frameIndex, DefaultBuffConfigProvider.RefreshTestBuffId, _automationConfig.BuffDurationFrames, 1);
            QueueBuffForAllPlayers(
                unchecked(frameIndex + (uint)DefaultBuffConfigProvider.RefreshReapplyDelayFrames),
                DefaultBuffConfigProvider.RefreshTestBuffId,
                _automationConfig.BuffDurationFrames,
                1);
            _automationBuffCommandsQueued = true;
            Log.Warning(
                $"[Automation][BattleServer] Queued buff refresh commands. scenario={_automationConfig.Scenario} buffId={DefaultBuffConfigProvider.RefreshTestBuffId} players={_playerIdBuffer.Count}");
            return;
        }

        if (_automationConfig.IsBuffMutexScenario)
        {
            QueueBuffForAllPlayers(frameIndex, DefaultBuffConfigProvider.MutexLowBuffId, _automationConfig.BuffDurationFrames, 1);
            QueueBuffForAllPlayers(unchecked(frameIndex + 1u), DefaultBuffConfigProvider.MutexHighBuffId, _automationConfig.BuffDurationFrames, 1);
            _automationBuffCommandsQueued = true;
            Log.Warning(
                $"[Automation][BattleServer] Queued buff mutex commands. scenario={_automationConfig.Scenario} lowBuffId={DefaultBuffConfigProvider.MutexLowBuffId} highBuffId={DefaultBuffConfigProvider.MutexHighBuffId} players={_playerIdBuffer.Count}");
            return;
        }

        QueueBuffForAllPlayers(frameIndex, _automationConfig.ExpectedBuffId, _automationConfig.BuffDurationFrames, _automationConfig.BuffStackCount);

        _automationBuffCommandsQueued = true;
        Log.Warning(
            $"[Automation][BattleServer] Queued buff lifecycle commands. scenario={_automationConfig.Scenario} buffId={_automationConfig.ExpectedBuffId} duration={_automationConfig.BuffDurationFrames} players={_playerIdBuffer.Count}");
    }

    private void QueueBuffForAllPlayers(uint frameIndex, int buffId, int durationFrames, int stackCount)
    {
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            _battleLogic.EnqueueApplyBuff(new ApplyBuffCommand
            {
                CasterId = playerId,
                TargetId = playerId,
                BuffId = buffId,
                DurationFrames = durationFrames,
                StackCount = stackCount,
                FrameIndex = frameIndex,
                Flags = _automationConfig.BuffFlags
            });
        }
    }

    private static (Fixed64 x, Fixed64 y) GetSpawnPosition(int playerCount)
    {
        Fixed64 spacing = (Fixed64)3;
        int row = playerCount / 2;
        Fixed64 x = (playerCount & 1) == 0 ? -spacing : spacing;
        Fixed64 y = row * spacing;
        return (x, y);
    }

    private readonly struct BuffSyncPayload
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
}
