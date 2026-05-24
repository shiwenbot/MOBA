using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using Fantasy.Network;

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
        (float spawnX, float spawnY) = GetSpawnPosition(_sessionsByPlayerId.Count);
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

    public void Tick(uint frameIndex, float fixedDt)
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

                frameSnapshot.Players.Add(new PlayerSnapshot
                {
                    PlayerId = player.PlayerId,
                    X = player.X,
                    Y = player.Y,
                    LatestAcceptedInputFrame = _battleLogic.GetLatestAcceptedInputFrame(player.PlayerId),
                    Angle = hasPhysics ? bodySnapshot.RotationRadians : 0.0f,
                    LinearVelocityX = hasPhysics ? bodySnapshot.LinearVelocityX : 0.0f,
                    LinearVelocityY = hasPhysics ? bodySnapshot.LinearVelocityY : 0.0f,
                    AngularVelocity = hasPhysics ? bodySnapshot.AngularVelocity : 0.0f,
                    IsAwake = hasPhysics && bodySnapshot.IsAwake,
                    IsEnabled = !hasPhysics || bodySnapshot.IsEnabled,
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

            session.Send(frameSnapshot);
        }

        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            PlayerStateSnapshot player = snapshot.Players[i];
            _lastBroadcastAttributesByPlayerId[player.PlayerId] = player.Attributes;
        }
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

    private static (float x, float y) GetSpawnPosition(int playerCount)
    {
        const float spacing = 3.0f;
        int row = playerCount / 2;
        float x = (playerCount & 1) == 0 ? -spacing : spacing;
        float y = row * spacing;
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
