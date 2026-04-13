using System;
using System.Diagnostics;
using Fantasy.Entitas;
using Fantasy.Entitas.Interface;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Timer;

namespace Fantasy;

public sealed class ServerTickDriver : Entitas.Entity
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly IFrameSyncLogger _logger = new ServerFrameSyncLogger();
    private long _lastElapsedTicks;

    public ServerTickDriver()
    {
        Dispatcher = new TickDispatcher(
            DeterminismRules.FixedDeltaTime,
            TickAccumulator.DefaultMaxDeltaTime,
            _logger);

        TimerService = new FrameTimerService(int.MaxValue, _logger);
        Dispatcher.Register(TimerService);
        _lastElapsedTicks = _stopwatch.ElapsedTicks;
    }

    public TickDispatcher Dispatcher { get; }
    public FrameTimerService TimerService { get; }
    public bool IsRunning { get; private set; } = true;

    public void StartTick()
    {
        _lastElapsedTicks = _stopwatch.ElapsedTicks;
        IsRunning = true;
    }

    public void StopTick()
    {
        IsRunning = false;
    }

    public void TickOnce()
    {
        if (!IsRunning)
        {
            return;
        }

        long currentTicks = _stopwatch.ElapsedTicks;
        float deltaTime = (float)(currentTicks - _lastElapsedTicks) / Stopwatch.Frequency;
        _lastElapsedTicks = currentTicks;
        Dispatcher.Update(deltaTime);
    }

    private sealed class ServerFrameSyncLogger : IFrameSyncLogger
    {
        public void LogError(Exception exception, string context)
        {
            Log.Error($"[FrameSync] {context} Exception={exception}");
        }
    }
}

public sealed class ServerTickDriverUpdateSystem : UpdateSystem<ServerTickDriver>
{
    protected override void Update(ServerTickDriver self)
    {
        self.TickOnce();
    }
}
