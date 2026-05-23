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
    private readonly List<long> _playerIdBuffer = new();

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

        _battleLogic.SubmitInput(playerId, input.FrameIndex, input.InputSeq, input.Dx, input.Dy);
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
                }
            }

            _sessionsByPlayerId.Remove(playerId);
            _lastBroadcastAttributesByPlayerId.Remove(playerId);
            _battleLogic.RemovePlayer(playerId);
        }
    }

    private void BroadcastSnapshot(TestSnapshot snapshot)
    {
        if (_sessionsByPlayerId.Count == 0)
        {
            return;
        }

        S2C_FrameSnapshot frameSnapshot = new S2C_FrameSnapshot
        {
            FrameIndex = snapshot.FrameIndex
        };
        Dictionary<int, PhysicsBodySnapshot> physicsByBodyId = BuildPhysicsBodyLookup(snapshot.PhysicsSnapshot);

        for (int i = 0; i < snapshot.Players.Length; i++)
        {
            PlayerStateSnapshot player = snapshot.Players[i];
            int bodyId = checked((int)player.PlayerId);
            bool hasPhysics = physicsByBodyId.TryGetValue(bodyId, out PhysicsBodySnapshot bodySnapshot);
            bool hasPreviousAttributes = _lastBroadcastAttributesByPlayerId.TryGetValue(player.PlayerId, out PlayerAttributeSnapshot previousAttributes);
            PlayerAttributeSnapshot currentAttributes = player.Attributes;
            PlayerAttributeDirtyFlags dirtyMask = PlayerAttributeSync.ComputeDirtyMask(
                hasPreviousAttributes,
                previousAttributes,
                currentAttributes);
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
                ActiveBuffs = BuildBuffSnapshots(player.ActiveBuffs),
                NextRuntimeBuffId = player.NextRuntimeBuffId,
                Numeric = BuildNumericSnapshot(player.Numeric)
            });

            _lastBroadcastAttributesByPlayerId[player.PlayerId] = currentAttributes;
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

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            session.Send(frameSnapshot);
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
                Flags = (uint)buff.Flags
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

    private void RunAutomationScenario(uint frameIndex)
    {
        if (!_automationConfig.IsBuffLifecycleScenario)
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
        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            _battleLogic.EnqueueApplyBuff(new ApplyBuffCommand
            {
                CasterId = playerId,
                TargetId = playerId,
                BuffId = _automationConfig.ExpectedBuffId,
                DurationFrames = _automationConfig.BuffDurationFrames,
                StackCount = _automationConfig.BuffStackCount,
                FrameIndex = frameIndex,
                Flags = _automationConfig.BuffFlags
            });
        }

        _automationBuffCommandsQueued = true;
        Log.Warning(
            $"[Automation][BattleServer] Queued buff lifecycle commands. scenario={_automationConfig.Scenario} buffId={_automationConfig.ExpectedBuffId} duration={_automationConfig.BuffDurationFrames} players={_playerIdBuffer.Count}");
    }

    private static (float x, float y) GetSpawnPosition(int playerCount)
    {
        const float spacing = 3.0f;
        int row = playerCount / 2;
        float x = (playerCount & 1) == 0 ? -spacing : spacing;
        float y = row * spacing;
        return (x, y);
    }
}
