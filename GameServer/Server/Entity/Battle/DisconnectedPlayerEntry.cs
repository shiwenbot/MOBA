namespace Fantasy;

public enum DisconnectCause
{
    ProbeTimeout,
    SessionDisposed
}

public enum DisconnectedPlayerState
{
    SuspectedDisconnected,
    ConfirmedDisconnected,
    Claimed
}

/// <summary>
/// In-memory ownership record retained while a disconnected battle slot can be reclaimed.
/// Authentication tokens deliberately do not live in this record.
/// </summary>
public sealed class DisconnectedPlayerEntry
{
    public DisconnectedPlayerEntry(
        long accountId,
        long playerId,
        long disconnectedSessionId,
        uint remainingGraceFrames,
        DisconnectCause cause)
    {
        AccountId = accountId;
        PlayerId = playerId;
        DisconnectedSessionId = disconnectedSessionId;
        RemainingGraceFrames = remainingGraceFrames;
        State = cause == DisconnectCause.SessionDisposed
            ? DisconnectedPlayerState.ConfirmedDisconnected
            : DisconnectedPlayerState.SuspectedDisconnected;
    }

    public long AccountId { get; }
    public long PlayerId { get; }
    public long DisconnectedSessionId { get; }
    public uint RemainingGraceFrames { get; private set; }
    public DisconnectedPlayerState State { get; private set; }
    public bool IsExpired => State != DisconnectedPlayerState.Claimed && RemainingGraceFrames == 0u;

    public bool Promote(DisconnectCause cause)
    {
        if (State != DisconnectedPlayerState.SuspectedDisconnected ||
            cause != DisconnectCause.SessionDisposed)
        {
            return false;
        }

        State = DisconnectedPlayerState.ConfirmedDisconnected;
        return true;
    }

    public void AdvanceOneFrame()
    {
        if (State == DisconnectedPlayerState.Claimed || RemainingGraceFrames == 0u)
        {
            return;
        }

        RemainingGraceFrames--;
    }

    public bool TryMarkClaimed()
    {
        if (State == DisconnectedPlayerState.Claimed || IsExpired)
        {
            return false;
        }

        State = DisconnectedPlayerState.Claimed;
        return true;
    }
}
