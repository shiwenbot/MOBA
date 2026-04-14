using Fantasy;
using Fantasy.Entitas.Interface;

namespace System;

public sealed class BattleComponentAwakeSystem : AwakeSystem<BattleComponent>
{
    protected override void Awake(BattleComponent self)
    {
        ServerTickDriver tickDriver = self.Scene.GetComponent<ServerTickDriver>();
        if (tickDriver == null)
        {
            Log.Error($"[Battle] Failed to register BattleComponent to TickDispatcher. Scene={self.Scene.SceneConfigId}");
            return;
        }

        tickDriver.Dispatcher.Register(self);
    }
}

public sealed class BattleComponentDestroySystem : DestroySystem<BattleComponent>
{
    protected override void Destroy(BattleComponent self)
    {
        if (self.Scene == null || self.Scene.IsDisposed)
        {
            return;
        }

        ServerTickDriver tickDriver = self.Scene.GetComponent<ServerTickDriver>();
        tickDriver?.Dispatcher.Unregister(self);
    }
}
