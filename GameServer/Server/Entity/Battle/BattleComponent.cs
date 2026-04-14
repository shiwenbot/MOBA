using System;
using System.Collections.Generic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using Fantasy.Network;

namespace Fantasy;

public sealed class BattleComponent : Entitas.Entity, ITickable
{
    private readonly Dictionary<long, PlayerState> _statesByPlayerId = new();
    private readonly Dictionary<long, PlayerSession> _sessionsByPlayerId = new();
    private readonly Dictionary<long, long> _playerIdBySessionId = new();
    private readonly Dictionary<long, PendingInput> _pendingInputsByPlayerId = new();
    private readonly List<long> _playerIdBuffer = new();

    private long _nextPlayerId = 1;

    public int Priority => 0;
    public uint LastFrameIndex { get; private set; }

    public PlayerState Join(Session session)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        CleanupDisconnectedPlayers();

        if (_playerIdBySessionId.TryGetValue(session.Id, out long existingPlayerId) &&
            _statesByPlayerId.TryGetValue(existingPlayerId, out PlayerState existingState))
        {
            if (_sessionsByPlayerId.TryGetValue(existingPlayerId, out PlayerSession existingPlayerSession))
            {
                existingPlayerSession.Session = session;
            }

            return existingState;
        }

        long playerId = _nextPlayerId++;
        (float spawnX, float spawnY) = GetSpawnPosition(_statesByPlayerId.Count);
        PlayerState newState = new PlayerState(playerId, spawnX, spawnY);

        _statesByPlayerId.Add(playerId, newState);
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

        if (!_pendingInputsByPlayerId.TryGetValue(playerId, out PendingInput pendingInput) ||
            IsInputNewerOrEqual(input.InputSeq, pendingInput.InputSeq))
        {
            _pendingInputsByPlayerId[playerId] = new PendingInput(input.FrameIndex, input.InputSeq, input.Dx, input.Dy);
        }
    }

    public void Tick(uint frameIndex, float fixedDt)
    {
        DeterminismRules.AssertFixedDt(fixedDt);
        LastFrameIndex = frameIndex;

        CleanupDisconnectedPlayers();
        BuildSortedPlayerBuffer();

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState state))
            {
                continue;
            }

            float dx = 0.0f;
            float dy = 0.0f;
            if (_pendingInputsByPlayerId.TryGetValue(playerId, out PendingInput input))
            {
                dx = input.Dx;
                dy = input.Dy;
            }

            MoveSystem.Apply(state, dx, dy, fixedDt);
        }

        _pendingInputsByPlayerId.Clear();
        BroadcastSnapshot(frameIndex);
    }

    private void BroadcastSnapshot(uint frameIndex)
    {
        if (_sessionsByPlayerId.Count == 0)
        {
            return;
        }

        S2C_FrameSnapshot snapshot = new S2C_FrameSnapshot
        {
            FrameIndex = frameIndex
        };

        for (int i = 0; i < _playerIdBuffer.Count; i++)
        {
            long playerId = _playerIdBuffer[i];
            if (!_statesByPlayerId.TryGetValue(playerId, out PlayerState state))
            {
                continue;
            }

            snapshot.Players.Add(new PlayerSnapshot
            {
                PlayerId = state.PlayerId,
                X = state.X,
                Y = state.Y
            });
        }

        _playerIdBuffer.Clear();

        foreach (KeyValuePair<long, PlayerSession> pair in _sessionsByPlayerId)
        {
            Session session = pair.Value.Session;
            if (session == null || session.IsDisposed)
            {
                continue;
            }

            session.Send(snapshot);
        }

        Log.Debug($"[Battle] Frame={frameIndex}, Players={snapshot.Players.Count}, Broadcast");
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

            if (_sessionsByPlayerId.TryGetValue(playerId, out PlayerSession playerSession))
            {
                long sessionId = playerSession.SessionId;
                if (sessionId != 0)
                {
                    _playerIdBySessionId.Remove(sessionId);
                }
            }

            _sessionsByPlayerId.Remove(playerId);
            _statesByPlayerId.Remove(playerId);
            _pendingInputsByPlayerId.Remove(playerId);
        }
    }

    private void BuildSortedPlayerBuffer()
    {
        _playerIdBuffer.Clear();

        foreach (long playerId in _statesByPlayerId.Keys)
        {
            _playerIdBuffer.Add(playerId);
        }

        _playerIdBuffer.Sort();
    }

    private static (float x, float y) GetSpawnPosition(int playerCount)
    {
        const float spacing = 3.0f;
        int row = playerCount / 2;
        float x = (playerCount & 1) == 0 ? -spacing : spacing;
        float y = row * spacing;
        return (x, y);
    }

    private static bool IsInputNewerOrEqual(uint incomingSeq, uint cachedSeq)
    {
        return incomingSeq == cachedSeq || unchecked(incomingSeq - cachedSeq) < 0x80000000;
    }

    private readonly struct PendingInput
    {
        public PendingInput(uint frameIndex, uint inputSeq, float dx, float dy)
        {
            FrameIndex = frameIndex;
            InputSeq = inputSeq;
            Dx = dx;
            Dy = dy;
        }

        public uint FrameIndex { get; }
        public uint InputSeq { get; }
        public float Dx { get; }
        public float Dy { get; }
    }
}
