using GameShared.FrameSync.Battle;

namespace Fantasy;

public readonly struct BattleJoinResult
{
    private BattleJoinResult(uint errorCode, PlayerState? state, bool isReconnect)
    {
        ErrorCode = errorCode;
        State = state;
        IsReconnect = isReconnect;
    }

    public uint ErrorCode { get; }
    public PlayerState? State { get; }
    public bool IsReconnect { get; }
    public bool IsSuccess => ErrorCode == 0u && State != null;

    public static BattleJoinResult Success(PlayerState state, bool isReconnect)
    {
        return new BattleJoinResult(0u, state, isReconnect);
    }

    public static BattleJoinResult Failure(uint errorCode)
    {
        return new BattleJoinResult(errorCode, null, false);
    }
}
