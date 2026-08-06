using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2B_RttProbeAckHandler : Message<C2B_RttProbeAck>
{
    protected override async FTask Run(Session session, C2B_RttProbeAck message)
    {
        BattleComponent battleComponent = session.Scene.GetComponent<BattleComponent>();
        if (battleComponent == null)
        {
            await FTask.CompletedTask;
            return;
        }

        battleComponent.SubmitRttProbeAck(session, message);
        await FTask.CompletedTask;
    }
}
