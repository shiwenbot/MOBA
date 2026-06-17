using System;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Snapshot;
using TEngine;

namespace GameLogic.FrameSync
{
    public sealed class SnapshotManager : ITickable
    {
        private const int DefaultBufferCapacity = 24;

        private readonly BattleWorldState _worldState;
        private readonly SnapshotBuffer<BattleWorldSnapshot> _buffer;
        private bool _selfTestExecuted;

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
            if (!_selfTestExecuted)
            {
                RunSelfTest();
                _selfTestExecuted = true;
            }

            BattleWorldSnapshot snapshot = _worldState.TakeSnapshot().WithFrameIndex(frameIndex);
            _buffer.Save(frameIndex, snapshot);
            LatestHash = StateHasher.Hash(snapshot);

        }

        public bool TryGetSnapshot(uint frameIndex, out BattleWorldSnapshot snapshot)
        {
            return _buffer.TryGet(frameIndex, out snapshot);
        }

        public void RollBack(uint targetFrame)
        {
            if (_buffer.TryGet(targetFrame, out BattleWorldSnapshot snapshot))
            {
                _worldState.RestoreSnapshot(snapshot);
                LatestHash = StateHasher.Hash(snapshot);
            }
        }

        public void CheckConsistency(uint frameIndex)
        {
        }

        private void RunSelfTest()
        {
            string failedCase;
            bool passed = SnapshotSelfTestSuite.Run(out failedCase);
            if (passed)
            {
                Log.Info("[SnapshotTest] ALL PASS");
            }
            else
            {
                Log.Warning($"[SnapshotTest] FAIL: {failedCase}");
            }
        }

        private static class SnapshotSelfTest
        {
            public static bool Run(out string failedCase)
            {
                if (!BasicRoundTrip())
                {
                    failedCase = "basic-roundtrip";
                    return false;
                }

                if (!CapacityEviction())
                {
                    failedCase = "capacity-eviction";
                    return false;
                }

                if (!SameFrameOverride())
                {
                    failedCase = "same-frame-override";
                    return false;
                }

                if (!HashNormalization())
                {
                    failedCase = "hash-normalization";
                    return false;
                }

                failedCase = string.Empty;
                return true;
            }

            private static bool BasicRoundTrip()
            {
                BattleWorldState worldState = new BattleWorldState();
                worldState.AddOrUpdatePlayer(1, F(3.5f), F(8.0f));
                worldState.AddOrUpdatePlayer(2, F(-2.25f), F(6.75f));

                BattleWorldSnapshot snapshotA = worldState.TakeSnapshot().WithFrameIndex(100);
                ulong hashA = StateHasher.Hash(snapshotA);

                worldState.AddOrUpdatePlayer(1, F(99.0f), F(99.0f));
                worldState.AddOrUpdatePlayer(2, F(-99.0f), F(-99.0f));
                worldState.RestoreSnapshot(snapshotA);

                BattleWorldSnapshot snapshotB = worldState.TakeSnapshot().WithFrameIndex(100);
                ulong hashB = StateHasher.Hash(snapshotB);
                return hashA == hashB;
            }

            private static bool CapacityEviction()
            {
                SnapshotBuffer<BattleWorldSnapshot> buffer = new SnapshotBuffer<BattleWorldSnapshot>(DefaultBufferCapacity);
                for (uint frameIndex = 1; frameIndex <= 25; frameIndex++)
                {
                    buffer.Save(frameIndex, CreateSinglePlayerSnapshot(frameIndex, frameIndex));
                }

                bool hasFirst = buffer.TryGet(1, out _);
                bool hasSecond = buffer.TryGet(2, out _);
                return !hasFirst && hasSecond && buffer.Count == DefaultBufferCapacity;
            }

            private static bool SameFrameOverride()
            {
                SnapshotBuffer<BattleWorldSnapshot> buffer = new SnapshotBuffer<BattleWorldSnapshot>(4);
                BattleWorldSnapshot first = CreateSinglePlayerSnapshot(10, 1.0f);
                BattleWorldSnapshot second = CreateSinglePlayerSnapshot(10, 2.0f);

                buffer.Save(10, first);
                buffer.Save(10, second);

                if (buffer.Count != 1)
                {
                    return false;
                }

                if (!buffer.TryGet(10, out BattleWorldSnapshot latest))
                {
                    return false;
                }

                return latest.Players.Count == 1 && latest.Players[0].X.m_rawValue == F(2.0f).m_rawValue;
            }

            private static bool HashNormalization()
            {
                BattleWorldSnapshot left = new BattleWorldSnapshot(
                    1,
                    new[]
                    {
                        new PlayerStateSnapshot(1, Fixed64.Zero, F(1.0f))
                    });

                BattleWorldSnapshot right = new BattleWorldSnapshot(
                    1,
                    new[]
                    {
                        new PlayerStateSnapshot(1, Fixed64.Zero, F(1.0f))
                    });

                return StateHasher.Hash(left) == StateHasher.Hash(right);
            }

            private static BattleWorldSnapshot CreateSinglePlayerSnapshot(uint frameIndex, float x)
            {
                return new BattleWorldSnapshot(
                    frameIndex,
                    new[]
                    {
                        new PlayerStateSnapshot(1, F(x), Fixed64.Zero)
                    });
            }

            private static Fixed64 F(float value)
            {
                return (Fixed64)value;
            }
        }
    }
}
