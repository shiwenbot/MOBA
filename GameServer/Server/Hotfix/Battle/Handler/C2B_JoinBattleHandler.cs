using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;
using GameShared.FrameSync.Battle;

namespace System;

public sealed class C2B_JoinBattleHandler : MessageRPC<C2B_JoinBattle, C2B_JoinBattleResponse>
{
    protected override async FTask Run(Session session, C2B_JoinBattle request, C2B_JoinBattleResponse response, Action reply)
    {
        BattleComponent battleComponent = session.Scene.GetComponent<BattleComponent>();
        if (battleComponent == null)
        {
            response.ErrorCode = 1;
            Log.Error($"[Battle] C2B_JoinBattle failed because BattleComponent is missing. Scene={session.Scene.SceneConfigId}");
            return;
        }

        PlayerState state = battleComponent.Join(session);
        response.PlayerId = state.PlayerId;
        response.X = state.X;
        response.Y = state.Y;
        response.ServerFrameIndex = battleComponent.LastFrameIndex;

        await FTask.CompletedTask;
    }
}
