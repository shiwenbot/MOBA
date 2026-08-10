using Fantasy.Network;

namespace Fantasy;

public sealed class PlayerSession
{
    private Session? _session;
    private long _sessionId;

    public PlayerSession(long accountId, long playerId, Session session)
    {
        AccountId = accountId;
        PlayerId = playerId;
        Session = session;
    }

    public long AccountId { get; }
    public long PlayerId { get; }
    public Session? Session
    {
        get => _session;
        set
        {
            _session = value;
            if (value != null)
            {
                _sessionId = value.Id;
            }
        }
    }

    public long SessionId => _sessionId;
    public bool IsReconnectSession { get; set; }
}
