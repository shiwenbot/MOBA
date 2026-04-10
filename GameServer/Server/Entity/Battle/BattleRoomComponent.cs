using Fantasy.Network;
using Fantasy.Network.Interface;

namespace Fantasy;

public sealed class BattleRoomComponent : Entitas.Entity
{
    private const int MaxPlayerCount = 2;
    private readonly Dictionary<long, Session> _sessions = new();
    private readonly List<long> _removeBuffer = new();

    public long ReceivedMessageCount;
    public long BroadcastMessageCount;
    public long LastStatsSecondMs;
    public int ReceivedCountInCurrentSecond;
    public long TotalProcessCostMs;
    public long MaxProcessCostMs;

    public int SessionCount => _sessions.Count;

    public bool JoinRoom(Session session)
    {
        if (session == null || session.IsDisposed)
        {
            return false;
        }

        CleanupDisposedSessions();

        if (_sessions.ContainsKey(session.Id))
        {
            return true;
        }

        if (_sessions.Count >= MaxPlayerCount)
        {
            return false;
        }

        _sessions.Add(session.Id, session);
        Log.Info($"[Battle] session join room. sessionId={session.Id}, sessionCount={_sessions.Count}");
        return true;
    }

    public void LeaveRoom(Session session)
    {
        if (session == null)
        {
            return;
        }

        if (_sessions.Remove(session.Id))
        {
            Log.Info($"[Battle] session leave room. sessionId={session.Id}, sessionCount={_sessions.Count}");
        }
    }

    public void Broadcast(IMessage message)
    {
        if (message == null)
        {
            return;
        }

        CleanupDisposedSessions();

        foreach (var (_, session) in _sessions)
        {
            if (session.IsDisposed)
            {
                continue;
            }

            session.Send(message, message.GetType());
            BroadcastMessageCount++;
        }
    }

    private void CleanupDisposedSessions()
    {
        _removeBuffer.Clear();

        foreach (var (sessionId, session) in _sessions)
        {
            if (session == null || session.IsDisposed)
            {
                _removeBuffer.Add(sessionId);
            }
        }

        for (var i = 0; i < _removeBuffer.Count; i++)
        {
            _sessions.Remove(_removeBuffer[i]);
        }
    }
}
