using System;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Timer;
using UnityEngine;

namespace GameLogic.FrameSync
{
    public sealed class ClientTickDriver : MonoBehaviour
    {
        private const int MaxCatchUpTicksPerFrame = 8;

        [SerializeField]
        private bool autoStart = true;

        [SerializeField]
        private int timerPriority = int.MaxValue;

        private TickDispatcher _dispatcher;
        private FrameTimerService _timerService;
        private UnityFrameSyncLogger _logger;
        private uint _targetFrame;

        public TickDispatcher Dispatcher => _dispatcher;
        public FrameTimerService TimerService => _timerService;
        public bool IsRunning { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            if (autoStart)
            {
                StartTick();
            }
        }

        private void OnDisable()
        {
            StopTick();
        }

        public void StartTick()
        {
            EnsureInitialized();
            IsRunning = true;
        }

        public void StopTick()
        {
            IsRunning = false;
        }

        public void SetTargetFrame(uint targetFrame)
        {
            _targetFrame = targetFrame;
        }

        private void Update()
        {
            if (!IsRunning || _dispatcher == null)
            {
                return;
            }

            _dispatcher.Update(Time.deltaTime);

            uint currentFrame = _dispatcher.CurrentFrame;
            int frameGap = unchecked((int)(_targetFrame - currentFrame));
            if (frameGap <= 0)
            {
                return;
            }

            int catchUpCount = Math.Min(frameGap, MaxCatchUpTicksPerFrame);
            for (int i = 0; i < catchUpCount; i++)
            {
                _dispatcher.TickOnce();
            }
        }

        private void EnsureInitialized()
        {
            if (_dispatcher != null)
            {
                return;
            }

            _logger = new UnityFrameSyncLogger();
            _dispatcher = new TickDispatcher(
                DeterminismRules.FixedDeltaTime,
                TickAccumulator.DefaultMaxDeltaTime,
                _logger);

            _timerService = new FrameTimerService(timerPriority, _logger);
            _dispatcher.Register(_timerService);
        }

        private sealed class UnityFrameSyncLogger : IFrameSyncLogger
        {
            public void LogError(Exception exception, string context)
            {
                Debug.LogException(new Exception($"[FrameSync] {context}", exception));
            }
        }
    }
}
