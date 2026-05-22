using System;
using GameShared.FrameSync.Battle;

namespace GameShared.FrameSync.Snapshot
{
    public static class SnapshotSelfTestSuite
    {
        private const int DefaultBufferCapacity = 24;
        private static readonly string[] AllCaseNames =
        {
            "basic-roundtrip",
            "capacity-eviction",
            "same-frame-override",
            "hash-normalization",
            "physics-roundtrip",
            "physics-affects-hash",
            "snapshot-attribute-roundtrip",
            "attributes-affect-hash",
            "attribute-dirty-merge"
        };

        public static bool Run(out string failedCase)
        {
            for (int i = 0; i < AllCaseNames.Length; i++)
            {
                if (!RunCase(AllCaseNames[i], out failedCase))
                {
                    return false;
                }
            }

            failedCase = string.Empty;
            return true;
        }

        public static bool RunCase(string caseName, out string failedCase)
        {
            try
            {
                string normalizedCaseName = caseName?.Trim().ToLowerInvariant() ?? string.Empty;
                bool passed = normalizedCaseName switch
                {
                    "basic-roundtrip" => BasicRoundTrip(),
                    "capacity-eviction" => CapacityEviction(),
                    "same-frame-override" => SameFrameOverride(),
                    "hash-normalization" => HashNormalization(),
                    "physics-roundtrip" => PhysicsRoundTrip(),
                    "physics-affects-hash" => PhysicsAffectsHash(),
                    "snapshot-attribute-roundtrip" or "attribute-roundtrip" => AttributeRoundTrip(),
                    "attributes-affect-hash" => AttributesAffectHash(),
                    "attribute-dirty-merge" => AttributeDirtyMerge(),
                    _ => throw new ArgumentException($"Unknown snapshot self test case: {caseName}", nameof(caseName))
                };

                failedCase = passed ? string.Empty : normalizedCaseName;
                return passed;
            }
            catch (Exception exception)
            {
                failedCase = $"{exception.GetType().Name}:{exception.Message}";
                return false;
            }
        }

        private static bool BasicRoundTrip()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, 3.5f, 8.0f);
            worldState.AddOrUpdatePlayer(2, -2.25f, 6.75f);

            BattleWorldSnapshot snapshotA = worldState.TakeSnapshot().WithFrameIndex(100);
            ulong hashA = StateHasher.Hash(snapshotA);

            worldState.AddOrUpdatePlayer(1, 99.0f, 99.0f);
            worldState.AddOrUpdatePlayer(2, -99.0f, -99.0f);
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

            return latest.Players.Count == 1 && Math.Abs(latest.Players[0].X - 2.0f) < 0.0001f;
        }

        private static bool HashNormalization()
        {
            BattleWorldSnapshot left = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, -0.0f, float.NaN)
                });

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, +0.0f, 0.0f)
                });

            return StateHasher.Hash(left) == StateHasher.Hash(right);
        }

        private static bool PhysicsRoundTrip()
        {
            TestPhysicsSnapshotProvider physicsProvider = new TestPhysicsSnapshotProvider(
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(7, 1.0f, 2.0f, 0.5f, 3.0f, 4.0f, 5.0f, true, true)
                    },
                    new[]
                    {
                        new PhysicsContactSnapshot(7, 8, true)
                    }));
            BattleWorldState worldState = new BattleWorldState(physicsProvider);
            worldState.AddOrUpdatePlayer(1, 3.5f, 8.0f);

            BattleWorldSnapshot snapshot = worldState.TakeSnapshot().WithFrameIndex(100);
            PhysicsWorldSnapshot originalPhysics = snapshot.PhysicsSnapshot;
            if (originalPhysics == null || originalPhysics.Bodies.Count != 1 || originalPhysics.Contacts.Count != 1)
            {
                return false;
            }

            physicsProvider.CurrentSnapshot = new PhysicsWorldSnapshot(
                new[]
                {
                    new PhysicsBodySnapshot(99, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, false, false)
                });
            worldState.RestoreSnapshot(snapshot);

            return ReferenceEquals(physicsProvider.CurrentSnapshot, originalPhysics);
        }

        private static bool PhysicsAffectsHash()
        {
            BattleWorldSnapshot left = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, 0.0f, 0.0f)
                },
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(1, 1.0f, 2.0f, 0.25f, 3.0f, 4.0f, 5.0f, true, true)
                    }));

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, 0.0f, 0.0f)
                },
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(1, 10.0f, 2.0f, 0.25f, 3.0f, 4.0f, 5.0f, true, true)
                    }));

            return StateHasher.Hash(left) != StateHasher.Hash(right);
        }

        private static bool AttributeRoundTrip()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, 2.0f, 3.0f);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            player.Health = 72;
            player.MaxHealth = 120;
            player.Mana = 18;
            player.MaxMana = 45;
            player.Attack = 27;

            BattleWorldSnapshot snapshotA = worldState.TakeSnapshot().WithFrameIndex(200);
            ulong hashA = StateHasher.Hash(snapshotA);

            player.Health = 1;
            player.MaxHealth = 2;
            player.Mana = 3;
            player.MaxMana = 4;
            player.Attack = 5;

            worldState.RestoreSnapshot(snapshotA);
            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            BattleWorldSnapshot snapshotB = worldState.TakeSnapshot().WithFrameIndex(200);
            ulong hashB = StateHasher.Hash(snapshotB);
            return restoredPlayer.Health == 72 &&
                   restoredPlayer.MaxHealth == 120 &&
                   restoredPlayer.Mana == 18 &&
                   restoredPlayer.MaxMana == 45 &&
                   restoredPlayer.Attack == 27 &&
                   hashA == hashB;
        }

        private static bool AttributesAffectHash()
        {
            BattleWorldSnapshot left = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, 0.0f, 0.0f, new PlayerAttributeSnapshot(100, 100, 40, 100, 10))
                });

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, 0.0f, 0.0f, new PlayerAttributeSnapshot(90, 100, 40, 100, 10))
                });

            return StateHasher.Hash(left) != StateHasher.Hash(right);
        }

        private static bool AttributeDirtyMerge()
        {
            PlayerAttributeSnapshot previous = new PlayerAttributeSnapshot(100, 100, 40, 100, 10);
            PlayerAttributeSnapshot current = new PlayerAttributeSnapshot(85, 100, 30, 100, 16);
            PlayerAttributeDirtyFlags dirtyMask = PlayerAttributeSync.ComputeDirtyMask(true, previous, current);
            PlayerAttributeSnapshot merged = PlayerAttributeSync.Merge(
                previous,
                dirtyMask,
                current.Health,
                current.MaxHealth,
                current.Mana,
                current.MaxMana,
                current.Attack);

            return dirtyMask == (PlayerAttributeDirtyFlags.Health | PlayerAttributeDirtyFlags.Mana | PlayerAttributeDirtyFlags.Attack) &&
                   merged.Health == current.Health &&
                   merged.MaxHealth == current.MaxHealth &&
                   merged.Mana == current.Mana &&
                   merged.MaxMana == current.MaxMana &&
                   merged.Attack == current.Attack;
        }

        private static BattleWorldSnapshot CreateSinglePlayerSnapshot(uint frameIndex, float x)
        {
            return new BattleWorldSnapshot(
                frameIndex,
                new[]
                {
                    new PlayerStateSnapshot(1, x, 0.0f)
                });
        }

        private sealed class TestPhysicsSnapshotProvider : IPhysicsMovementWorld
        {
            public TestPhysicsSnapshotProvider(PhysicsWorldSnapshot snapshot)
            {
                CurrentSnapshot = snapshot;
            }

            public PhysicsWorldSnapshot CurrentSnapshot { get; set; }

            public PhysicsWorldSnapshot TakeSnapshot()
            {
                return CurrentSnapshot;
            }

            public void RestoreSnapshot(PhysicsWorldSnapshot snapshot)
            {
                CurrentSnapshot = snapshot;
            }

            public void ClearBodies()
            {
            }

            public void EnsureBody(int bodyId, float x, float y)
            {
            }

            public void RemoveBody(int bodyId)
            {
            }

            public void SetBodyTransform(int bodyId, float x, float y, bool resetVelocity)
            {
            }

            public void SetBodyMovementInput(int bodyId, float dx, float dy)
            {
            }

            public void Step(float dt)
            {
            }

            public bool TryGetBodySnapshot(int bodyId, out PhysicsBodySnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
        }
    }
}
