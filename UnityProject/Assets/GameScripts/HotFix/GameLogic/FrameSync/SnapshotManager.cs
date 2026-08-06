using System;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Snapshot;

namespace GameLogic.FrameSync
{
    /// <summary>
    /// v0.3-arch 遗留的 tickable。S6 起不再由 ClientTickDriver 注册；
    /// 自测已迁移到 SnapshotSelfTestSuite 启动期一次性调用。
    /// 保留类型仅避免外部反射/历史引用编译失败。
    /// </summary>
    public sealed class SnapshotManager : ITickable
    {
        private readonly BattleWorldState _worldState;
        private readonly SnapshotBuffer<BattleWorldSnapshot> _buffer;

        public SnapshotManager(
            BattleWorldState worldState,
            SnapshotBuffer<BattleWorldSnapshot> buffer,
            int priority = int.MaxValue)
        {
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Priority = priority;
        }

        public int Priority { get; }
        public ulong LatestHash { get; private set; }
        public int SnapshotCount => _buffer.Count;

        public void Tick(uint frameIndex, Fixed64 fixedDt)
        {
            // 不再每帧拍快照；S3 哈希上报走 BattleSimulation 权威缓冲。
        }

        public bool TryGetSnapshot(uint frameIndex, out BattleWorldSnapshot snapshot)
        {
            return _buffer.TryGet(frameIndex, out snapshot);
        }
    }
}
