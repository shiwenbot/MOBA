using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2B_PlayerInputHandler : Message<C2B_PlayerInput>
{
    protected override async FTask Run(Session session, C2B_PlayerInput message)
    {
        BattleComponent battleComponent = session.Scene.GetComponent<BattleComponent>();
        if (battleComponent == null)
        {
            await FTask.CompletedTask;
            return;
        }

        battleComponent.SubmitInput(session, message);
        await FTask.CompletedTask;
    }
}
