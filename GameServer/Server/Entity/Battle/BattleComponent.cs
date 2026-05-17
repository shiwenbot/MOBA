using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using Fantasy.Network;

namespace Fantasy;

public sealed class BattleComponent : Entitas.Entity, ITickable
{
    private readonly BattleLogic _battleLogic = new(Log.Debug, Log.Warning);
    private readonly Dictionary<long, PlayerSession> _sessionsByPlayerId = new();
    private readonly Dictionary<long, long> _playerIdBySessionId = new();
    private readonly List<long> _playerIdBuffer = new();

    private long _nextPlayerId = 1;

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
                IsEnabled = !hasPhysics || bodySnapshot.IsEnabled
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

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            session.Send(frameSnapshot);
        }

        Log.Debug($"[Battle] Frame={snapshot.FrameIndex}, Players={frameSnapshot.Players.Count}, Broadcast");
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

    private static (float x, float y) GetSpawnPosition(int playerCount)
    {
        const float spacing = 3.0f;
        int row = playerCount / 2;
        float x = (playerCount & 1) == 0 ? -spacing : spacing;
        float y = row * spacing;
        return (x, y);
    }
}
