using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameLogic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Network;
using GameShared.FrameSync.Determinism;

using Fantasy.Network;
using Fantasy.Serialize;
using TEngine;

namespace Fantasy;

public sealed class BattleComponent : Entitas.Entity, ITickable
{
    private readonly BattleAutomationServerConfig _automationConfig = BattleAutomationServerConfig.Current;
    private readonly BattleLogic _battleLogic = new(null, Log.Warning);
    private readonly Dictionary<long, PlayerSession> _sessionsByPlayerId = new();
    private readonly Dictionary<long, long> _playerIdBySessionId = new();
    private readonly Dictionary<long, long> _playerIdByAccountId = new();
    private readonly ObserverTargetBaselineMap<AttributeBroadcastBaseline> _attributeBaselinesByObserverTarget = new();
    private readonly ObserverTargetBaselineMap<BuffBroadcastBaseline> _buffBaselinesByObserverTarget = new();
    private readonly Dictionary<long, ServerRttTracker> _rttTrackersBySessionId = new();
    private readonly Dictionary<long, DisconnectedPlayerEntry> _disconnectedPlayersByAccountId = new();
    private readonly List<long> _playerIdBuffer = new();
    private readonly List<long> _accountIdBuffer = new();

    private readonly BattleBandwidthConfig _bandwidthConfig = BattleBandwidthConfig.Current;
    private readonly BattleBandwidthStats _bandwidthStats = new(BattleBandwidthConfig.Current.ReportIntervalFrames);
    private readonly MemoryStreamBuffer _bandwidthMeasureBuffer = new();
    private readonly BattleRttConfig _rttConfig = BattleRttConfig.FromEnvironment();
    private readonly BattleReconnectConfig _reconnectConfig;
    private readonly IProbeNonceSource _probeNonceSource = CryptoProbeNonceSource.Instance;
    private readonly IBattleClock _rttClock = SystemBattleClock.Instance;
    private readonly LeadBoundsParams _leadBoundsParams;
    private uint _rttProbeFrameCounter;

    /// <summary>带宽统计累积器。报告窗口数据从这里读取（含对照基线）。</summary>
    public BattleBandwidthStats BandwidthStats => _bandwidthStats;

    /// <summary>带宽统计总开关是否开启。</summary>
    public bool BandwidthStatsEnabled => _bandwidthConfig.Enabled;

    /// <summary>对照测量是否开启（未开启则算不出「省了多少」）。</summary>
    public bool BandwidthMeasureFullSyncBaseline => _bandwidthConfig.MeasureFullSyncBaseline;

    public int HashReportsMatched => _battleLogic.HashReportsMatched;
    public int HashMismatchCount => _battleLogic.HashMismatchCount;
    public int HashNoRecordCount => _battleLogic.HashNoRecordCount;
    public int LeadOutOfBoundsCount { get; private set; }

    private long _nextPlayerId = 1;
    private bool _automationPlayerThresholdObserved;
    private bool _automationBuffCommandsQueued;
    private uint _automationBuffApplyFrame;

    public int Priority => 0;
    public uint LastFrameIndex => _battleLogic.LastFrameIndex;

    internal int ActivePlayerCountForTests => _sessionsByPlayerId.Count;
    internal int AttributeBaselineCountForTests => _attributeBaselinesByObserverTarget.Count;
    internal int BuffBaselineCountForTests => _buffBaselinesByObserverTarget.Count;

    internal bool HasSessionMappingForTests(long sessionId)
    {
        return _playerIdBySessionId.ContainsKey(sessionId);
    }

    internal bool HasRttTrackerForTests(long sessionId)
    {
        return _rttTrackersBySessionId.ContainsKey(sessionId);
    }

    internal bool HasAttributeBaselineForTests(long observerSessionId, long targetPlayerId)
    {
        return _attributeBaselinesByObserverTarget.TryGet(observerSessionId, targetPlayerId, out _);
    }

    internal bool HasBuffBaselineForTests(long observerSessionId, long targetPlayerId)
    {
        return _buffBaselinesByObserverTarget.TryGet(observerSessionId, targetPlayerId, out _);
    }

    internal bool IsInputSuppressedForTests(long playerId)
    {
        return _battleLogic.IsInputSuppressed(playerId);
    }

    public BattleComponent()
        : this(BattleReconnectConfig.FromEnvironment())
    {
    }

    internal BattleComponent(BattleReconnectConfig reconnectConfig)
    {
        _reconnectConfig = reconnectConfig ?? throw new ArgumentNullException(nameof(reconnectConfig));
        _battleLogic.OnBroadcast = BroadcastSnapshot;
        _leadBoundsParams = new LeadBoundsParams(
            windowMs: _rttConfig.WindowMs,
            fixedDeltaMilliseconds: DeterminismRules.FixedDeltaTime * 1000f,
            maxAcceptedInputBufferFrames: InputBufferTuning.MaxAcceptedInputBufferFrames,
            maxFutureInputFrames: InputBufferTuning.MaxFutureInputFrames);
    }

    public BattleJoinResult Join(Session session, long accountId)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (accountId <= 0L)
        {
            return BattleJoinResult.Failure(BattleJoinErrorCodes.AuthenticationFailed);
        }

        DetectDisconnectedPlayers();
        CleanupExpiredDisconnectedPlayers();

        if (_playerIdBySessionId.TryGetValue(session.Id, out long existingPlayerId))
        {
            if (!_sessionsByPlayerId.TryGetValue(existingPlayerId, out PlayerSession? sessionOwner) ||
                sessionOwner == null ||
                sessionOwner.AccountId != accountId ||
                !_battleLogic.TryGetPlayer(existingPlayerId, out PlayerState existingState))
            {
                return BattleJoinResult.Failure(BattleJoinErrorCodes.AccountAlreadyOnline);
            }

            sessionOwner.Session = session;
            EnsureRttTracker(session.Id);
            return BattleJoinResult.Success(existingState, sessionOwner.IsReconnectSession);
        }

        if (_playerIdByAccountId.TryGetValue(accountId, out long accountPlayerId) &&
            _sessionsByPlayerId.TryGetValue(accountPlayerId, out PlayerSession? accountPlayerSession) &&
            accountPlayerSession != null &&
            _battleLogic.TryGetPlayer(accountPlayerId, out PlayerState accountPlayerState))
        {
            if (!_disconnectedPlayersByAccountId.TryGetValue(accountId, out DisconnectedPlayerEntry? disconnected) ||
                disconnected == null ||
                disconnected.PlayerId != accountPlayerId ||
                disconnected.IsExpired)
            {
                return BattleJoinResult.Failure(BattleJoinErrorCodes.AccountAlreadyOnline);
            }

            long oldSessionId = disconnected.DisconnectedSessionId;
            if (!disconnected.TryMarkClaimed())
            {
                return BattleJoinResult.Failure(BattleJoinErrorCodes.AccountAlreadyOnline);
            }

            // Claimed is terminal before any old-session mapping is removed. This closes the
            // late-packet revocation window while the account-scoped join lock is held.
            _playerIdBySessionId.Remove(oldSessionId);
            _rttTrackersBySessionId.Remove(oldSessionId);
            RemoveAttributeBroadcastBaselines(oldSessionId, 0L);
            RemoveBuffBroadcastBaselines(oldSessionId, 0L);

            accountPlayerSession.Session = session;
            accountPlayerSession.IsReconnectSession = true;
            _playerIdBySessionId[session.Id] = accountPlayerId;
            EnsureRttTracker(session.Id);
            _battleLogic.SetInputSuppressed(accountPlayerId, false);
            _disconnectedPlayersByAccountId.Remove(accountId);
            return BattleJoinResult.Success(accountPlayerState, true);
        }

        long playerId = _nextPlayerId++;
        (Fixed64 spawnX, Fixed64 spawnY) = GetSpawnPosition(_sessionsByPlayerId.Count);
        PlayerState newState = _battleLogic.JoinPlayer(playerId, spawnX, spawnY);

        _sessionsByPlayerId[playerId] = new PlayerSession(accountId, playerId, session);
        _playerIdBySessionId[session.Id] = playerId;
        _playerIdByAccountId[accountId] = playerId;
        EnsureRttTracker(session.Id);

        return BattleJoinResult.Success(newState, false);
    }

    public void SubmitInput(Session session, C2B_PlayerInput input)
    {
        if (session == null || input == null)
        {
            return;
        }

        if (!_playerIdBySessionId.TryGetValue(session.Id, out long playerId) ||
            !TryAcceptBusinessActivity(session.Id, playerId))
        {
            return;
        }

        ObserveInputLead(session.Id, playerId, input.FrameIndex);
        _battleLogic.SubmitInput(playerId, input.FrameIndex, input.InputSeq, input.DxRaw, input.DyRaw, input.SkillId);
    }

    public void SubmitRttProbeAck(Session session, C2B_RttProbeAck ack)
    {
        if (!_rttConfig.ProbeEnabled || session == null || ack == null)
        {
            return;
        }

        if (!_playerIdBySessionId.TryGetValue(session.Id, out long playerId))
        {
            return;
        }

        ServerRttTracker tracker = EnsureRttTracker(session.Id);
        long nowMs = _rttClock.NowMs;
        if (!tracker.TryRecordAck(ack.ProbeNonce, nowMs, out _) ||
            !TryAcceptBusinessActivity(session.Id, playerId))
        {
            return;
        }
    }

    public void SubmitStateHashReport(Session session, C2B_StateHashReport report)
    {
        if (session == null || report == null)
        {
            return;
        }

        if (!_playerIdBySessionId.TryGetValue(session.Id, out long playerId) ||
            !TryAcceptBusinessActivity(session.Id, playerId))
        {
            return;
        }

        _battleLogic.TryCompareReportedHash(playerId, report.FrameIndex, report.StateHash);
    }

    public void Tick(uint frameIndex, Fixed64 fixedDt)
    {
        DetectDisconnectedPlayers();
        CleanupExpiredDisconnectedPlayers();
        RunAutomationScenario(frameIndex);
        if (_rttConfig.ProbeEnabled)
        {
            long nowMs = _rttClock.NowMs;
            SendRttProbesIfNeeded(nowMs);
            CleanupStaleProbes(nowMs);
            TickRttEnvelopes(nowMs);
        }

        _battleLogic.Tick(frameIndex, fixedDt);
        AdvanceDisconnectedPlayerGracePeriods();
    }

    public bool MarkDisconnected(long sessionId, DisconnectCause cause)
    {
        if (sessionId == 0 ||
            !_playerIdBySessionId.TryGetValue(sessionId, out long playerId) ||
            !_sessionsByPlayerId.TryGetValue(playerId, out PlayerSession? playerSession) ||
            playerSession == null)
        {
            return false;
        }

        long accountId = playerSession.AccountId;
        if (_disconnectedPlayersByAccountId.TryGetValue(accountId, out DisconnectedPlayerEntry? existing))
        {
            if (existing == null ||
                existing.PlayerId != playerId ||
                existing.DisconnectedSessionId != sessionId ||
                existing.State == DisconnectedPlayerState.Claimed)
            {
                return false;
            }

            bool promoted = existing.Promote(cause);
            _battleLogic.SetInputSuppressed(playerId, true);
            return promoted;
        }

        _disconnectedPlayersByAccountId.Add(
            accountId,
            new DisconnectedPlayerEntry(
                accountId,
                playerId,
                sessionId,
                _reconnectConfig.GracePeriodFrames,
                cause));
        _battleLogic.SetInputSuppressed(playerId, true);
        return true;
    }

    internal bool TryGetDisconnectedPlayer(long accountId, out DisconnectedPlayerEntry entry)
    {
        bool found = _disconnectedPlayersByAccountId.TryGetValue(accountId, out DisconnectedPlayerEntry? resolved);
        entry = resolved!;
        return found;
    }

    private void DetectDisconnectedPlayers()
    {
        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session? session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                MarkDisconnected(pair.Value.SessionId, DisconnectCause.SessionDisposed);
            }
        }
    }

    private void AdvanceDisconnectedPlayerGracePeriods()
    {
        foreach (KeyValuePair<long, DisconnectedPlayerEntry> pair in _disconnectedPlayersByAccountId)
        {
            pair.Value.AdvanceOneFrame();
        }
    }

    private void CleanupExpiredDisconnectedPlayers()
    {
        _accountIdBuffer.Clear();
        foreach (KeyValuePair<long, DisconnectedPlayerEntry> pair in _disconnectedPlayersByAccountId)
        {
            if (pair.Value.IsExpired)
            {
                _accountIdBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _accountIdBuffer.Count; i++)
        {
            long accountId = _accountIdBuffer[i];
            if (!_disconnectedPlayersByAccountId.TryGetValue(accountId, out DisconnectedPlayerEntry? entry) ||
                entry == null)
            {
                continue;
            }

            long playerId = entry.PlayerId;

            if (_sessionsByPlayerId.TryGetValue(playerId, out PlayerSession? playerSession))
            {
                long sessionId = playerSession!.SessionId;
                if (sessionId != 0)
                {
                    _playerIdBySessionId.Remove(sessionId);
                    _rttTrackersBySessionId.Remove(sessionId);
                    RemoveAttributeBroadcastBaselines(sessionId, 0);
                    RemoveBuffBroadcastBaselines(sessionId, 0);
                }
            }

            _sessionsByPlayerId.Remove(playerId);
            _playerIdByAccountId.Remove(accountId);
            RemoveAttributeBroadcastBaselines(0, playerId);
            RemoveBuffBroadcastBaselines(0, playerId);
            _battleLogic.RemovePlayer(playerId);
            _disconnectedPlayersByAccountId.Remove(accountId);
        }

        _accountIdBuffer.Clear();
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
            Session? session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            S2C_FrameSnapshot frameSnapshot = new S2C_FrameSnapshot
            {
                FrameIndex = snapshot.FrameIndex,
                TargetLeadFrames = ResolveTargetLeadFrames(session.Id)
            };

            int dirtyAttributeEntries = 0;
            int buffFullSyncEntries = 0;
            AttributeSyncPlan[] attributeSyncPlans = BuildAttributeSyncPlans(session.Id, snapshot);

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
                    _bandwidthStats.RecordFullSyncCounterfactual(MeasureFullSyncBytes(session.Id, frameSnapshot));
                }
            }

            session.Send(frameSnapshot);

            // Always push per-session RTT stats (switch-off still reports Enabled=false).
            session.Send(BuildRttStatsMessage(session.Id, snapshot.FrameIndex));

            UpdateAttributeBroadcastBaselines(session.Id, snapshot, attributeSyncPlans);
        }


        if (_bandwidthStats.TryConsumeReportDue(snapshot.FrameIndex))
        {
            // 到点：先把当前窗口的统计推送给每个客户端，再打日志、再清窗口。
            // 推送不依赖总开关——开关关着时也推一条带标记的消息，客户端据此显示「统计未开启」。
            S2C_BandwidthStats statsMessage = BuildBandwidthStatsMessage(snapshot.FrameIndex);
            foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
            {
                Session? session = pair.Value.Session;
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
    private int MeasureFullSyncBytes(long observerSessionId, S2C_FrameSnapshot frameSnapshot)
    {
        S2C_FrameSnapshot fullSync = new S2C_FrameSnapshot
        {
            FrameIndex = frameSnapshot.FrameIndex
        };

        for (int i = 0; i < frameSnapshot.Players.Count; i++)
        {
            PlayerSnapshot source = frameSnapshot.Players[i];
            long playerId = source.PlayerId;
            bool hasAttributes = _attributeBaselinesByObserverTarget.TryGet(
                observerSessionId,
                playerId,
                out AttributeBroadcastBaseline? attributes);

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

    private AttributeSyncPlan[] BuildAttributeSyncPlans(long observerSessionId, TestSnapshot snapshot)
    {
        AttributeSyncPlan[] plans = new AttributeSyncPlan[snapshot.Players.Length];
        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            PlayerStateSnapshot player = snapshot.Players[i];
            bool hasBaseline = _attributeBaselinesByObserverTarget.TryGet(
                observerSessionId,
                player.PlayerId,
                out AttributeBroadcastBaseline? baseline);
            bool forceFullSync = SnapshotSyncRecoveryPolicy.IsPeriodicFullSyncFrame(
                snapshot.FrameIndex,
                player.PlayerId);
            PlayerAttributeDirtyFlags dirtyMask = forceFullSync || !hasBaseline
                ? PlayerAttributeDirtyFlags.All
                : PlayerAttributeSync.ComputeDirtyMask(true, baseline!.Attributes, player.Attributes);
            plans[i] = new AttributeSyncPlan(
                dirtyMask,
                hasBaseline ? baseline!.FrameIndex : 0u);
        }

        return plans;
    }

    private void UpdateAttributeBroadcastBaselines(
        long observerSessionId,
        TestSnapshot snapshot,
        AttributeSyncPlan[] plans)
    {
        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            if (plans[i].DirtyMask == PlayerAttributeDirtyFlags.None)
            {
                continue;
            }

            PlayerStateSnapshot player = snapshot.Players[i];
            if (_attributeBaselinesByObserverTarget.TryGet(
                    observerSessionId,
                    player.PlayerId,
                    out AttributeBroadcastBaseline? baseline))
            {
                baseline!.Update(player.Attributes, snapshot.FrameIndex);
            }
            else
            {
                _attributeBaselinesByObserverTarget.Set(
                    observerSessionId,
                    player.PlayerId,
                    new AttributeBroadcastBaseline(player.Attributes, snapshot.FrameIndex));
            }
        }
    }

    private void RemoveAttributeBroadcastBaselines(long observerSessionId, long targetPlayerId)
    {
        _attributeBaselinesByObserverTarget.RemoveMatching(observerSessionId, targetPlayerId);
    }

    private static NumericSnapshot BuildNumericSnapshot(GameShared.FrameSync.Battle.NumericModifierSnapshot numericState)
    {
        return BattleSnapshotProtocolMapper.WriteNumericSnapshot(numericState);
    }

    private BuffSyncPayload BuildBuffSyncPayload(long observerSessionId, PlayerStateSnapshot player, uint frameIndex)
    {
        if (!_buffBaselinesByObserverTarget.TryGet(
                observerSessionId,
                player.PlayerId,
                out BuffBroadcastBaseline? baseline))
        {
            BuffBroadcastBaseline fullSyncBaseline = new BuffBroadcastBaseline(player.ActiveBuffs, player.NextRuntimeBuffId, frameIndex);
            _buffBaselinesByObserverTarget.Set(observerSessionId, player.PlayerId, fullSyncBaseline);
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
        _buffBaselinesByObserverTarget.RemoveMatching(observerSessionId, targetPlayerId);
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

    private ServerRttTracker EnsureRttTracker(long sessionId)
    {
        if (_rttTrackersBySessionId.TryGetValue(sessionId, out ServerRttTracker? existing) && existing != null)
        {
            return existing;
        }

        ServerRttTracker tracker = new ServerRttTracker(
            tightenRateMsPerSec: _rttConfig.TightenRateMsPerSec,
            envelopeSampleCapEnabled: _rttConfig.EnvelopeSampleCapEnabled);
        _rttTrackersBySessionId[sessionId] = tracker;
        return tracker;
    }

    private void SendRttProbesIfNeeded(long nowMs)
    {
        _rttProbeFrameCounter++;
        if (_rttProbeFrameCounter < _rttConfig.ProbeIntervalFrames)
        {
            return;
        }

        _rttProbeFrameCounter = 0;
        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session? session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            ulong nonce = _probeNonceSource.NextNonce();
            ServerRttTracker tracker = EnsureRttTracker(session.Id);
            tracker.RecordProbeSent(nonce, nowMs);
            session.Send(new S2C_RttProbe { ProbeNonce = nonce });
        }
    }

    private void CleanupStaleProbes(long nowMs)
    {
        long timeoutMs = _rttConfig.ProbeTimeoutMs;
        foreach (KeyValuePair<long, ServerRttTracker> pair in _rttTrackersBySessionId)
        {
            pair.Value.CleanupStale(nowMs, timeoutMs);
            if ((uint)pair.Value.ConsecutiveTimedOutProbeCount >= _rttConfig.DisconnectTimeoutCount)
            {
                MarkDisconnected(pair.Key, DisconnectCause.ProbeTimeout);
            }
        }
    }

    private bool TryAcceptBusinessActivity(long sessionId, long playerId)
    {
        if (!_sessionsByPlayerId.TryGetValue(playerId, out PlayerSession? playerSession) ||
            playerSession == null ||
            playerSession.SessionId != sessionId)
        {
            return false;
        }

        long accountId = playerSession.AccountId;
        if (_disconnectedPlayersByAccountId.TryGetValue(accountId, out DisconnectedPlayerEntry? entry) &&
            entry != null)
        {
            if (entry.PlayerId != playerId ||
                entry.DisconnectedSessionId != sessionId ||
                entry.State != DisconnectedPlayerState.SuspectedDisconnected)
            {
                return false;
            }

            _disconnectedPlayersByAccountId.Remove(accountId);
            _battleLogic.SetInputSuppressed(playerId, false);
        }

        if (_rttTrackersBySessionId.TryGetValue(sessionId, out ServerRttTracker? tracker) && tracker != null)
        {
            tracker.ResetConsecutiveProbeTimeouts();
        }

        return true;
    }

    private void TickRttEnvelopes(long nowMs)
    {
        foreach (KeyValuePair<long, ServerRttTracker> pair in _rttTrackersBySessionId)
        {
            pair.Value.TickEnvelope(nowMs);
        }
    }

    private void ObserveInputLead(long sessionId, long playerId, uint claimedFrameIndex)
    {
        if (!_rttTrackersBySessionId.TryGetValue(sessionId, out ServerRttTracker? tracker) || tracker == null)
        {
            return;
        }

        LeadBoundsResult result = LeadBoundsCalculator.Evaluate(
            claimedFrameIndex,
            _battleLogic.LastFrameIndex,
            tracker.HasSample,
            tracker.ControlRttMs,
            _leadBoundsParams);

        if (!result.IsOutOfBounds)
        {
            return;
        }

        tracker.IncrementLeadOutOfBounds();
        LeadOutOfBoundsCount++;
        Log.Warning(
            $"[Battle][InputLeadOutOfBounds] player={playerId} claimed={claimedFrameIndex} " +
            $"serverFrame={_battleLogic.LastFrameIndex} observedLead={result.ObservedLead} " +
            $"upperBound={result.UpperBound} controlRtt={tracker.ControlRttMs:F1}ms " +
            $"rttEma={tracker.EmaMs:F1}ms rttMin={tracker.MinMs}ms");
    }

    private uint ResolveTargetLeadFrames(long sessionId)
    {
        if (!_rttConfig.ProbeEnabled || !_rttConfig.AuthoritativeLeadEnabled)
        {
            return 0u;
        }

        if (!_rttTrackersBySessionId.TryGetValue(sessionId, out ServerRttTracker? tracker) ||
            tracker == null ||
            !tracker.HasSample)
        {
            return 0u;
        }

        return (uint)TargetLeadCalculator.Compute(tracker.ControlRttMs);
    }

    private S2C_RttStats BuildRttStatsMessage(long sessionId, uint frameIndex)
    {
        S2C_RttStats message = new S2C_RttStats
        {
            FrameIndex = frameIndex,
            Enabled = _rttConfig.ProbeEnabled
        };

        if (!_rttConfig.ProbeEnabled)
        {
            message.HasSample = false;
            message.RttMinMs = 0;
            message.RttEmaMs = 0;
            message.ControlRttMs = 0;
            message.RttSampleCount = 0;
            message.LeadOutOfBoundsCount = 0;
            return message;
        }

        if (!_rttTrackersBySessionId.TryGetValue(sessionId, out ServerRttTracker? tracker) || tracker == null)
        {
            return message;
        }

        message.HasSample = tracker.HasSample;
        message.RttMinMs = tracker.MinMs;
        message.RttEmaMs = tracker.EmaMs;
        message.ControlRttMs = tracker.ControlRttMs;
        message.RttSampleCount = tracker.SampleCount;
        message.LeadOutOfBoundsCount = tracker.LeadOutOfBoundsCount;
        return message;
    }



}
