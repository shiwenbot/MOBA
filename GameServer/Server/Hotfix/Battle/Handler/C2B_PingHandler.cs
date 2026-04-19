using Fantasy;
using Fantasy.Async;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2B_PingHandler : Message<C2B_Ping>
{
    protected override async FTask Run(Session session, C2B_Ping message)
    {
        using S2C_Pong pong = S2C_Pong.Create();
        pong.SendTimestampMs = message.SendTimestampMs;
        session.Send(pong);
        await FTask.CompletedTask;
    }
}
