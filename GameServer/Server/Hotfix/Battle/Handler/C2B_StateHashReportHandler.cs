using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2B_StateHashReportHandler : Message<C2B_StateHashReport>
{
    protected override async FTask Run(Session session, C2B_StateHashReport message)
    {
        BattleComponent battleComponent = session.Scene.GetComponent<BattleComponent>();
        if (battleComponent == null)
        {
            await FTask.CompletedTask;
            return;
        }

        battleComponent.SubmitStateHashReport(session, message);
        await FTask.CompletedTask;
    }
}
