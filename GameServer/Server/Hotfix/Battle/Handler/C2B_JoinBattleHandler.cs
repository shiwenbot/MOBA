using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Network;

namespace System;

public sealed class C2B_JoinBattleHandler : MessageRPC<C2B_JoinBattle, C2B_JoinBattleResponse>
{
    protected override async FTask Run(Session session, C2B_JoinBattle request, C2B_JoinBattleResponse response, Action reply)
    {
        BattleComponent battleComponent = session.Scene.GetComponent<BattleComponent>();
        if (battleComponent == null)
        {
            response.ErrorCode = BattleJoinErrorCodes.BattleUnavailable;
            Log.Error($"[Battle] C2B_JoinBattle failed because BattleComponent is missing. Scene={session.Scene.SceneConfigId}");
            return;
        }

        LoginSessionValidation validation = await LoginSessionStore.Validate(session.Scene, request.Token);
        if (!validation.IsValid)
        {
            response.ErrorCode = BattleJoinErrorCodes.AuthenticationFailed;
            return;
        }

        BattleJoinResult result;
        using (await session.Scene.CoroutineLockComponent.Wait(
                   (int)LockType.Battle_JoinLock,
                   validation.AccountId))
        {
            result = battleComponent.Join(session, validation.AccountId);
        }

        response.ErrorCode = result.ErrorCode;
        if (!result.IsSuccess)
        {
            return;
        }

        PlayerState state = result.State!;
        response.PlayerId = state.PlayerId;
        response.XRaw = state.X.m_rawValue;
        response.YRaw = state.Y.m_rawValue;
        response.ServerFrameIndex = battleComponent.LastFrameIndex;
        response.IsReconnect = result.IsReconnect;
    }
}
