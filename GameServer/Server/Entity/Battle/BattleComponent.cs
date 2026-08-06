using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameLogic;
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
    private readonly Dictionary<long, AttributeBroadcastBaseline> _lastBroadcastAttributesByPlayerId = new();
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

        _battleLogic.SubmitInput(playerId, input.FrameIndex, input.InputSeq, input.DxRaw, input.DyRaw, input.SkillId);
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
        AttributeSyncPlan[] attributeSyncPlans = BuildAttributeSyncPlans(snapshot);
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
                AttributeSyncPlan attributePlan = attributeSyncPlans[i];
                PlayerAttributeSnapshot currentAttributes = player.Attributes;
                BuffSyncPayload buffPayload = BuildBuffSyncPayload(session.Id, player, snapshot.FrameIndex);
                PlayerAttributeDirtyFlags dirtyMask = attributePlan.DirtyMask;

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

                PlayerSnapshot wirePlayer = BattleSnapshotProtocolMapper.WritePlayer(
                    player,
                    hasPhysics,
                    bodySnapshot,
                    _battleLogic.GetLatestAcceptedInputFrame(player.PlayerId),
                    dirtyMask,
                    attributePlan.IsFullSync
                        ? snapshot.FrameIndex
                        : attributePlan.BaselineFrameIndex,
                    buffPayload.Buffs,
                    buffPayload.NextRuntimeBuffId,
                    buffPayload.DirtyMask,
                    buffPayload.BaselineFrameIndex,
                    buffPayload.IsFullSync);
                frameSnapshot.Players.Add(wirePlayer);
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
            if (attributeSyncPlans[i].DirtyMask == PlayerAttributeDirtyFlags.None)
            {
                continue;
            }

            if (_lastBroadcastAttributesByPlayerId.TryGetValue(player.PlayerId, out AttributeBroadcastBaseline? baseline))
            {
                baseline.Update(player.Attributes, snapshot.FrameIndex);
            }
            else
            {
                _lastBroadcastAttributesByPlayerId[player.PlayerId] =
                    new AttributeBroadcastBaseline(player.Attributes, snapshot.FrameIndex);
            }
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
            bool hasAttributes = _lastBroadcastAttributesByPlayerId.TryGetValue(playerId, out AttributeBroadcastBaseline? attributes);

            fullSync.Players.Add(new PlayerSnapshot
            {
                PlayerId = source.PlayerId,
                XRaw = source.XRaw,
                YRaw = source.YRaw,
                LatestAcceptedInputFrame = source.LatestAcceptedInputFrame,
                LinearVelocityXRaw = source.LinearVelocityXRaw,
                LinearVelocityYRaw = source.LinearVelocityYRaw,
                AngularVelocityRaw = source.AngularVelocityRaw,
                IsAsleep = source.IsAsleep,
                IsDisabled = source.IsDisabled,
                AttributeDirtyMask = (uint)PlayerAttributeDirtyFlags.All,
                AttributeBaselineFrameIndex = frameSnapshot.FrameIndex,
                Health = hasAttributes ? attributes!.Attributes.Health : source.Health,
                MaxHealth = hasAttributes ? attributes!.Attributes.MaxHealth : source.MaxHealth,
                Mana = hasAttributes ? attributes!.Attributes.Mana : source.Mana,
                MaxMana = hasAttributes ? attributes!.Attributes.MaxMana : source.MaxMana,
                Attack = hasAttributes ? attributes!.Attributes.Attack : source.Attack,
                ActiveBuffs = source.ActiveBuffs,
                NextRuntimeBuffId = source.NextRuntimeBuffId,
                Numeric = source.Numeric,
                BuffDirtyMask = uint.MaxValue,
                BuffSnapshotFrameIndex = source.BuffSnapshotFrameIndex,
                IsBuffFullSync = true,
                RotationRadiansRaw = source.RotationRadiansRaw
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
        return BattleSnapshotProtocolMapper.BuildPhysicsBodyLookup(physicsSnapshot);
    }

    private AttributeSyncPlan[] BuildAttributeSyncPlans(TestSnapshot snapshot)
    {
        AttributeSyncPlan[] plans = new AttributeSyncPlan[snapshot.Players.Length];
        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            PlayerStateSnapshot player = snapshot.Players[i];
            bool hasBaseline = _lastBroadcastAttributesByPlayerId.TryGetValue(
                player.PlayerId,
                out AttributeBroadcastBaseline? baseline);
            bool forceFullSync = SnapshotSyncRecoveryPolicy.IsPeriodicFullSyncFrame(
                                     snapshot.FrameIndex,
                                     player.PlayerId) ||
                                 HasObserverWithoutBuffBaseline(player.PlayerId);
            PlayerAttributeDirtyFlags dirtyMask = forceFullSync || !hasBaseline
                ? PlayerAttributeDirtyFlags.All
                : PlayerAttributeSync.ComputeDirtyMask(true, baseline!.Attributes, player.Attributes);
            plans[i] = new AttributeSyncPlan(
                dirtyMask,
                hasBaseline ? baseline!.FrameIndex : 0u);
        }

        return plans;
    }

    private bool HasObserverWithoutBuffBaseline(long targetPlayerId)
    {
        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            if (!_buffBaselinesByObserverTarget.ContainsKey((session.Id, targetPlayerId)))
            {
                return true;
            }
        }

        return false;
    }

    private static NumericSnapshot BuildNumericSnapshot(GameShared.FrameSync.Battle.NumericModifierSnapshot numericState)
    {
        return BattleSnapshotProtocolMapper.WriteNumericSnapshot(numericState);
    }

    private BuffSyncPayload BuildBuffSyncPayload(long observerSessionId, PlayerStateSnapshot player, uint frameIndex)
    {
        (long ObserverSessionId, long TargetPlayerId) key = (observerSessionId, player.PlayerId);
        if (!_buffBaselinesByObserverTarget.TryGetValue(key, out BuffBroadcastBaseline? baseline))
        {
            BuffBroadcastBaseline fullSyncBaseline = new BuffBroadcastBaseline(player.ActiveBuffs, player.NextRuntimeBuffId, frameIndex);
            _buffBaselinesByObserverTarget[key] = fullSyncBaseline;
            return BuffBroadcastPayloadBuilder.CreateInitialFullSync(player);
        }

        return BuffBroadcastPayloadBuilder.Build(
            baseline,
            player,
            frameIndex,
            SnapshotSyncRecoveryPolicy.IsPeriodicFullSyncFrame(frameIndex, player.PlayerId));
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

    internal readonly struct AttributeSyncPlan
    {
        public AttributeSyncPlan(PlayerAttributeDirtyFlags dirtyMask, uint baselineFrameIndex)
        {
            DirtyMask = dirtyMask;
            BaselineFrameIndex = baselineFrameIndex;
        }

        public PlayerAttributeDirtyFlags DirtyMask { get; }
        public uint BaselineFrameIndex { get; }
        public bool IsFullSync => BattleSnapshotProtocolMapper.IsFullAttributeSnapshot(DirtyMask);
    }

}
