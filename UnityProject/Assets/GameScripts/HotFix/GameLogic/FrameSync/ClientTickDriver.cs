using System;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;
using GameShared.FrameSync.Timer;
using UnityEngine;

namespace GameLogic.FrameSync
{
    public sealed class ClientTickDriver : MonoBehaviour
    {
        private const int MaxCatchUpTicksPerFrame = 8;
        private const float AheadTimeScale = 0.5f;

        [SerializeField]
        private bool autoStart = true;

        [SerializeField]
        private int timerPriority = int.MaxValue;

        private TickDispatcher _dispatcher;
        private FrameTimerService _timerService;
        private BattleWorldState _worldState;
        private UnityFrameSyncLogger _logger;
        private bool _snapshotSelfTestExecuted;
        private uint _targetFrame;
        private bool _hasTargetFrame;

        public TickDispatcher Dispatcher => _dispatcher;
        public FrameTimerService TimerService => _timerService;
        public BattleWorldState WorldState => _worldState;
        public IFrameSyncLogger Logger => _logger;
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
            _hasTargetFrame = true;
        }

        public void AlignToFrame(uint frameIndex, bool resetAccumulator = true)
        {
            EnsureInitialized();
            _dispatcher.SetCurrentFrame(frameIndex, resetAccumulator);
            _targetFrame = frameIndex;
            _hasTargetFrame = true;
        }

        private void Update()
        {
            if (!IsRunning || _dispatcher == null)
            {
                return;
            }

            if (!_hasTargetFrame)
            {
                _dispatcher.Update(Time.deltaTime);
                return;
            }

            uint currentFrame = _dispatcher.CurrentFrame;
            int frameGap = unchecked((int)(_targetFrame - currentFrame));
            if (frameGap < 0)
            {
                // Slow the logical clock while ahead. Keeping the accumulator makes
                // this independent of render FPS and still lets snapshots get applied.
                _dispatcher.Update(Time.deltaTime * AheadTimeScale);
                return;
            }

            _dispatcher.Update(Time.deltaTime);

            currentFrame = _dispatcher.CurrentFrame;
            frameGap = unchecked((int)(_targetFrame - currentFrame));
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

            _worldState = new BattleWorldState();
            _timerService = new FrameTimerService(timerPriority, _logger);
            _dispatcher.Register(_timerService);
            // SnapshotManager 是 v0.3-arch 留下的孤儿 tickable：LatestHash/RollBack 全仓零消费。
            // 启动期一次性跑 SnapshotSelfTest，省掉每帧 TakeSnapshot + Hash 的分配。
            if (!_snapshotSelfTestExecuted)
            {
                string failedCase;
                bool passed = SnapshotSelfTestSuite.Run(out failedCase);
                if (passed)
                {
                    Debug.Log("[SnapshotTest] ALL PASS");
                }
                else
                {
                    Debug.LogWarning($"[SnapshotTest] FAIL: {failedCase}");
                }

                _snapshotSelfTestExecuted = true;
            }
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
