using Fantasy.Network;

namespace Fantasy;

public sealed class PlayerSession
{
    public PlayerSession(long playerId, Session session)
    {
        PlayerId = playerId;
        Session = session;
    }

    public long PlayerId { get; }
    public Session Session { get; set; }
    public long SessionId => Session?.Id ?? 0;
}
