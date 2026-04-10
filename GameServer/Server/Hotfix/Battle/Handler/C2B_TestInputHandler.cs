using Fantasy;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network;
using Fantasy.Network.Interface;

namespace System;

public sealed class C2B_TestInputHandler : Message<C2B_TestInput>
{
    protected override async FTask Run(Session session, C2B_TestInput message)
    {
        var scene = session.Scene;
        var room = scene.GetComponent<BattleRoomComponent>();

        if (room == null)
        {
            Log.Error($"[Battle] BattleRoomComponent missing. sceneId={scene.SceneConfigId}");
            return;
        }

        if (!room.JoinRoom(session))
        {
            Log.Warning($"[Battle] room full, reject session. sessionId={session.Id} sceneId={scene.SceneConfigId}");
            return;
        }

        var serverRecvTimeMs = TimeHelper.Now;
        room.ReceivedMessageCount++;

        var currentSecond = serverRecvTimeMs / 1000;
        if (room.LastStatsSecondMs == 0)
        {
            room.LastStatsSecondMs = currentSecond;
        }
        else if (room.LastStatsSecondMs != currentSecond)
        {
            var avgProcessMs = room.ReceivedCountInCurrentSecond > 0
                ? room.TotalProcessCostMs / room.ReceivedCountInCurrentSecond
                : 0;
            Log.Info($"[Battle] recv/s={room.ReceivedCountInCurrentSecond} sessions={room.SessionCount} totalRecv={room.ReceivedMessageCount} avgProcessMs={avgProcessMs} maxProcessMs={room.MaxProcessCostMs}");
            room.LastStatsSecondMs = currentSecond;
            room.ReceivedCountInCurrentSecond = 0;
            room.TotalProcessCostMs = 0;
            room.MaxProcessCostMs = 0;
        }
        
        room.ReceivedCountInCurrentSecond++;

        var serverSendTimeMs = TimeHelper.Now;
        room.Broadcast(new B2C_TestState
        {
            Seq = message.Seq,
            ClientSendTimeMs = message.ClientSendTimeMs,
            ServerRecvTimeMs = serverRecvTimeMs,
            ServerSendTimeMs = serverSendTimeMs
        });

        var processCostMs = TimeHelper.Now - serverRecvTimeMs;
        room.TotalProcessCostMs += processCostMs;
        if (processCostMs > room.MaxProcessCostMs)
        {
            room.MaxProcessCostMs = processCostMs;
        }

        await FTask.CompletedTask;
    }
}
