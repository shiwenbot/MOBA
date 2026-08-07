using System;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.SkillGraph;

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
            "attribute-dirty-merge",
            "buff-roundtrip",
            "buffs-affect-hash",
            "runtime-buff-id-roundtrip",
            "skill-buff-roundtrip",
            "stack-overlay-roundtrip",
            "refresh-overlay-roundtrip",
            "mutex-replace-roundtrip",
            "mutex-reject-roundtrip",
            "mutex-same-priority",
            "mutex-same-frame-two-commands",
            "stackable-bridge-upgrade"
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
                    "buff-roundtrip" => BuffRoundTrip(),
                    "buffs-affect-hash" => BuffsAffectHash(),
                    "runtime-buff-id-roundtrip" => RuntimeBuffIdRoundTrip(),
                    "skill-buff-roundtrip" => SkillBuffRoundTrip(),
                    "stack-overlay-roundtrip" => StackOverlayRoundTrip(),
                    "refresh-overlay-roundtrip" => RefreshOverlayRoundTrip(),
                    "mutex-replace-roundtrip" => MutexReplaceRoundTrip(),
                    "mutex-reject-roundtrip" => MutexRejectRoundTrip(),
                    "mutex-same-priority" => MutexSamePriority(),
                    "mutex-same-frame-two-commands" => MutexSameFrameTwoCommands(),
                    "stackable-bridge-upgrade" => StackableBridgeUpgrade(),
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
            worldState.AddOrUpdatePlayer(1, F(3.5f), F(8.0f));
            worldState.AddOrUpdatePlayer(2, -2.25f, 6.75f);

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
                    new PlayerStateSnapshot(1, F(0.0f), F(1.0f))
                });

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, F(0.0f), F(1.0f))
                });

            return StateHasher.Hash(left) == StateHasher.Hash(right);
        }

        private static bool PhysicsRoundTrip()
        {
            TestPhysicsSnapshotProvider physicsProvider = new TestPhysicsSnapshotProvider(
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(7, F(1.0f), F(2.0f), F(0.5f), F(3.0f), F(4.0f), F(5.0f), true, true)
                    },
                    new[]
                    {
                        new PhysicsContactSnapshot(7, 8, true)
                    }));
            BattleWorldState worldState = new BattleWorldState(physicsProvider);
            worldState.AddOrUpdatePlayer(1, F(3.5f), F(8.0f));

            BattleWorldSnapshot snapshot = worldState.TakeSnapshot().WithFrameIndex(100);
            PhysicsWorldSnapshot originalPhysics = snapshot.PhysicsSnapshot;
            if (originalPhysics == null || originalPhysics.Bodies.Count != 1 || originalPhysics.Contacts.Count != 1)
            {
                return false;
            }

            physicsProvider.CurrentSnapshot = new PhysicsWorldSnapshot(
                new[]
                {
                    new PhysicsBodySnapshot(99, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, false, false)
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
                    new PlayerStateSnapshot(1, Fixed64.Zero, Fixed64.Zero)
                },
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(1, F(1.0f), F(2.0f), F(0.25f), F(3.0f), F(4.0f), F(5.0f), true, true)
                    }));

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, Fixed64.Zero, Fixed64.Zero)
                },
                new PhysicsWorldSnapshot(
                    new[]
                    {
                        new PhysicsBodySnapshot(1, F(10.0f), F(2.0f), F(0.25f), F(3.0f), F(4.0f), F(5.0f), true, true)
                    }));

            return StateHasher.Hash(left) != StateHasher.Hash(right);
        }

        private static bool AttributeRoundTrip()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, F(2.0f), F(3.0f));
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
                    new PlayerStateSnapshot(1, Fixed64.Zero, Fixed64.Zero, new PlayerAttributeSnapshot(100, 100, 40, 100, 10, 100, 100))
                });

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(1, Fixed64.Zero, Fixed64.Zero, new PlayerAttributeSnapshot(90, 100, 40, 100, 10, 100, 100))
                });

            return StateHasher.Hash(left) != StateHasher.Hash(right);
        }

        private static bool AttributeDirtyMerge()
        {
            PlayerAttributeSnapshot previous = new PlayerAttributeSnapshot(100, 100, 40, 100, 10, 100, 100);
            PlayerAttributeSnapshot current = new PlayerAttributeSnapshot(85, 100, 30, 100, 16, 100, 100);
            PlayerAttributeDirtyFlags dirtyMask = PlayerAttributeSync.ComputeDirtyMask(true, previous, current);
            PlayerAttributeSnapshot merged = PlayerAttributeSync.Merge(
                previous,
                dirtyMask,
                current.Health,
                current.MaxHealth,
                current.Mana,
                current.MaxMana,
                current.Attack,
                current.Stamina,
                current.MaxStamina);

            return dirtyMask == (PlayerAttributeDirtyFlags.Health | PlayerAttributeDirtyFlags.Mana | PlayerAttributeDirtyFlags.Attack) &&
                   merged.Health == current.Health &&
                   merged.MaxHealth == current.MaxHealth &&
                   merged.Mana == current.Mana &&
                   merged.MaxMana == current.MaxMana &&
                   merged.Attack == current.Attack;
        }

        private static bool BuffRoundTrip()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, F(2.0f), F(3.0f));
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            long runtimeBuffId = BuffSystem.AddBuff(
                player,
                casterId: 7,
                buffId: 101,
                durationFrames: 6,
                stackCount: 2,
                appliedFrame: 12,
                flags: BuffFlags.Duration | BuffFlags.Stackable);
            player.Numeric.AddModifier(new NumericModifier(runtimeBuffId, ModifierValueType.Flat, AttributeKind.Attack, 5));
            player.Numeric.Recalculate(player);

            BattleWorldSnapshot snapshotA = worldState.TakeSnapshot().WithFrameIndex(300);
            ulong hashA = StateHasher.Hash(snapshotA);

            player.Attack = 1;
            player.ActiveBuffs.Clear();
            player.Numeric.RestoreSnapshot(NumericModifierSnapshot.FromAttributes(player.CaptureAttributeSnapshot()));

            worldState.RestoreSnapshot(snapshotA);
            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            BattleWorldSnapshot snapshotB = worldState.TakeSnapshot().WithFrameIndex(300);
            ulong hashB = StateHasher.Hash(snapshotB);
            return restoredPlayer.ActiveBuffs.Count == 1 &&
                   restoredPlayer.ActiveBuffs[0].RuntimeBuffId == runtimeBuffId &&
                   restoredPlayer.ActiveBuffs[0].StackCount == 2 &&
                   restoredPlayer.Attack == 15 &&
                   restoredPlayer.Numeric.Count == 1 &&
                   hashA == hashB;
        }

        private static bool BuffsAffectHash()
        {
            PlayerAttributeSnapshot attributes = new PlayerAttributeSnapshot(100, 100, 40, 100, 10, 100, 100);
            BattleWorldSnapshot left = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(
                        1,
                        Fixed64.Zero,
                        Fixed64.Zero,
                        attributes,
                        new[]
                        {
                            new BuffState(1, 101, 7, 1, 1, 5, 10, BuffFlags.Duration)
                        },
                        2,
                        new NumericModifierSnapshot(
                            attributes,
                            new[]
                            {
                                new NumericModifier(1, ModifierValueType.Flat, AttributeKind.Attack, 5)
                            }))
                });

            BattleWorldSnapshot right = new BattleWorldSnapshot(
                1,
                new[]
                {
                    new PlayerStateSnapshot(
                        1,
                        Fixed64.Zero,
                        Fixed64.Zero,
                        attributes,
                        new[]
                        {
                            new BuffState(2, 202, 7, 1, 1, 5, 10, BuffFlags.Duration)
                        },
                        3,
                        new NumericModifierSnapshot(
                            attributes,
                            new[]
                            {
                                new NumericModifier(2, ModifierValueType.Flat, AttributeKind.Attack, 8)
                            }))
                });

            return StateHasher.Hash(left) != StateHasher.Hash(right);
        }

        private static bool RuntimeBuffIdRoundTrip()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            long firstRuntimeBuffId = BuffSystem.AddBuff(
                player,
                casterId: 9,
                buffId: 301,
                durationFrames: 3,
                stackCount: 1,
                appliedFrame: 20,
                flags: BuffFlags.Duration);
            BattleWorldSnapshot snapshot = worldState.TakeSnapshot().WithFrameIndex(400);

            worldState.RestoreSnapshot(snapshot);
            if (!worldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            long secondRuntimeBuffId = BuffSystem.AddBuff(
                restoredPlayer,
                casterId: 9,
                buffId: 302,
                durationFrames: 3,
                stackCount: 1,
                appliedFrame: 21,
                flags: BuffFlags.Duration);
            return firstRuntimeBuffId == 1 &&
                   secondRuntimeBuffId == 2 &&
                   restoredPlayer.NextRuntimeBuffId == 3;
        }

        private static bool SkillBuffRoundTrip()
        {
            int skillId = BattleSkillGraphLibrary.ResolveConfiguredSkillId();
            int expectedBuffId = BattleSkillGraphLibrary.ResolveConfiguredBuffId();

            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            TestWorldBuffCommandSink commandSink = new TestWorldBuffCommandSink(worldState);
            BattleSkillGraphRuntime runtime = new BattleSkillGraphRuntime(
                commandSink,
                BattleSkillGraphLibrary.CreateBuiltInGraphs());

            runtime.QueueSkillRequest(1, 1, skillId, 0);
            runtime.Step(0);
            commandSink.Process(0);
            player.Numeric.Recalculate(player);
            BuffSystem.ApplyTick(player, 0);

            BattleWorldSnapshot snapshotA = worldState.TakeSnapshot().WithFrameIndex(500);
            ulong hashA = StateHasher.Hash(snapshotA);

            BattleWorldState restoredWorldState = new BattleWorldState();
            restoredWorldState.RestoreSnapshot(snapshotA);
            BattleWorldSnapshot snapshotB = restoredWorldState.TakeSnapshot().WithFrameIndex(500);
            ulong hashB = StateHasher.Hash(snapshotB);
            if (!restoredWorldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            return restoredPlayer.ActiveBuffs.Count == 1 &&
                   restoredPlayer.ActiveBuffs[0].BuffId == expectedBuffId &&
                   hashA == hashB;
        }

        private static bool StackOverlayRoundTrip()
        {
            DefaultBuffConfigProvider provider = new DefaultBuffConfigProvider();
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            ApplyBuffCommand command = new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.StackTestBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 10,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            };

            long runtimeBuffId = BuffSystem.AddBuff(player, command, provider);
            command.FrameIndex = 11;
            BuffSystem.AddBuff(player, command, provider);
            command.FrameIndex = 12;
            BuffSystem.AddBuff(player, command, provider);
            if (player.ActiveBuffs.Count != 1 ||
                player.ActiveBuffs[0].RuntimeBuffId != runtimeBuffId ||
                player.ActiveBuffs[0].StackCount != 3 ||
                player.Attack != 25)
            {
                return false;
            }

            for (uint frame = 13; frame < 23; frame++)
            {
                BuffSystem.ApplyTick(player, frame);
            }

            uint appliedFrame = player.ActiveBuffs[0].AppliedFrame;
            command.FrameIndex = 23;
            BuffSystem.AddBuff(player, command, provider);

            return player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].RuntimeBuffId == runtimeBuffId &&
                   player.ActiveBuffs[0].StackCount == DefaultBuffConfigProvider.StackMaxCount &&
                   player.ActiveBuffs[0].RemainingFrames == 45 &&
                   player.ActiveBuffs[0].AppliedFrame == appliedFrame &&
                   player.Attack == 25 &&
                   SnapshotRoundTrip(worldState, 610, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].RuntimeBuffId == runtimeBuffId &&
                       restoredPlayer.ActiveBuffs[0].StackCount == DefaultBuffConfigProvider.StackMaxCount &&
                       restoredPlayer.ActiveBuffs[0].RemainingFrames == 45 &&
                       restoredPlayer.ActiveBuffs[0].AppliedFrame == appliedFrame &&
                       restoredPlayer.Attack == 25);
        }

        private static bool RefreshOverlayRoundTrip()
        {
            DefaultBuffConfigProvider provider = new DefaultBuffConfigProvider();
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            ApplyBuffCommand command = new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.RefreshTestBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 20,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            };

            long runtimeBuffId = BuffSystem.AddBuff(player, command, provider);
            for (uint frame = 21; frame < 31; frame++)
            {
                BuffSystem.ApplyTick(player, frame);
            }

            if (player.ActiveBuffs.Count != 1 || player.ActiveBuffs[0].RemainingFrames != 35)
            {
                return false;
            }

            uint appliedFrame = player.ActiveBuffs[0].AppliedFrame;
            command.FrameIndex = 31;
            long refreshedRuntimeBuffId = BuffSystem.AddBuff(player, command, provider);

            return refreshedRuntimeBuffId == runtimeBuffId &&
                   player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].RuntimeBuffId == runtimeBuffId &&
                   player.ActiveBuffs[0].StackCount == 1 &&
                   player.ActiveBuffs[0].RemainingFrames == 45 &&
                   player.ActiveBuffs[0].AppliedFrame == appliedFrame &&
                   player.Attack == 17 &&
                   SnapshotRoundTrip(worldState, 620, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].RuntimeBuffId == runtimeBuffId &&
                       restoredPlayer.ActiveBuffs[0].RemainingFrames == 45 &&
                       restoredPlayer.ActiveBuffs[0].AppliedFrame == appliedFrame &&
                       restoredPlayer.Attack == 17);
        }

        private static bool MutexReplaceRoundTrip()
        {
            DefaultBuffConfigProvider provider = new DefaultBuffConfigProvider();
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            long lowRuntimeBuffId = BuffSystem.AddBuff(player, new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexLowBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 40,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            }, provider);
            long highRuntimeBuffId = BuffSystem.AddBuff(player, new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexHighBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 41,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            }, provider);

            return lowRuntimeBuffId > 0 &&
                   highRuntimeBuffId > 0 &&
                   player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].RuntimeBuffId == highRuntimeBuffId &&
                   player.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexHighBuffId &&
                   player.Numeric.Count == 1 &&
                   player.Attack == 16 &&
                   SnapshotRoundTrip(worldState, 630, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].RuntimeBuffId == highRuntimeBuffId &&
                       restoredPlayer.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexHighBuffId &&
                       restoredPlayer.Attack == 16);
        }

        private static bool MutexRejectRoundTrip()
        {
            DefaultBuffConfigProvider provider = new DefaultBuffConfigProvider();
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            long highRuntimeBuffId = BuffSystem.AddBuff(player, new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexHighBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 50,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            }, provider);
            long rejectedRuntimeBuffId = BuffSystem.AddBuff(player, new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexLowBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 51,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            }, provider);

            return highRuntimeBuffId > 0 &&
                   rejectedRuntimeBuffId == 0 &&
                   player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].RuntimeBuffId == highRuntimeBuffId &&
                   player.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexHighBuffId &&
                   player.Attack == 16 &&
                   SnapshotRoundTrip(worldState, 640, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].RuntimeBuffId == highRuntimeBuffId &&
                       restoredPlayer.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexHighBuffId &&
                       restoredPlayer.Attack == 16);
        }

        private static bool MutexSamePriority()
        {
            DefaultBuffConfigProvider provider = new DefaultBuffConfigProvider();
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            long firstRuntimeBuffId = BuffSystem.AddBuff(player, new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexHighBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 60,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            }, provider);
            long secondRuntimeBuffId = BuffSystem.AddBuff(player, new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexSamePriorityBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 61,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            }, provider);

            return firstRuntimeBuffId > 0 &&
                   secondRuntimeBuffId > 0 &&
                   firstRuntimeBuffId != secondRuntimeBuffId &&
                   player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].RuntimeBuffId == secondRuntimeBuffId &&
                   player.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexSamePriorityBuffId &&
                   player.Attack == 18 &&
                   SnapshotRoundTrip(worldState, 650, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].RuntimeBuffId == secondRuntimeBuffId &&
                       restoredPlayer.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexSamePriorityBuffId &&
                       restoredPlayer.Attack == 18);
        }

        private static bool MutexSameFrameTwoCommands()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            TestWorldBuffCommandSink commandSink = new TestWorldBuffCommandSink(worldState);
            commandSink.EnqueueApplyBuff(new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexSamePriorityBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 70,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            });
            commandSink.EnqueueApplyBuff(new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = DefaultBuffConfigProvider.MutexHighBuffId,
                DurationFrames = 0,
                StackCount = 1,
                FrameIndex = 70,
                Flags = BuffFlags.Duration | BuffFlags.Dispellable
            });
            commandSink.Process(70);

            return player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexSamePriorityBuffId &&
                   player.Attack == 18 &&
                   SnapshotRoundTrip(worldState, 660, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].BuffId == DefaultBuffConfigProvider.MutexSamePriorityBuffId &&
                       restoredPlayer.Attack == 18);
        }

        private static bool StackableBridgeUpgrade()
        {
            BattleWorldState worldState = new BattleWorldState();
            worldState.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            if (!worldState.TryGetPlayer(1, out PlayerState player))
            {
                return false;
            }

            ApplyBuffCommand command = new ApplyBuffCommand
            {
                CasterId = 1,
                TargetId = 1,
                BuffId = 7001,
                DurationFrames = 30,
                StackCount = 1,
                FrameIndex = 80,
                Flags = BuffFlags.Duration | BuffFlags.Stackable
            };

            long firstRuntimeBuffId = BuffSystem.AddBuff(player, command);
            command.FrameIndex = 81;
            long secondRuntimeBuffId = BuffSystem.AddBuff(player, command);

            return firstRuntimeBuffId == secondRuntimeBuffId &&
                   player.ActiveBuffs.Count == 1 &&
                   player.ActiveBuffs[0].RuntimeBuffId == firstRuntimeBuffId &&
                   player.ActiveBuffs[0].StackCount == 2 &&
                   player.ActiveBuffs[0].RemainingFrames == 30 &&
                   SnapshotRoundTrip(worldState, 670, restoredPlayer =>
                       restoredPlayer.ActiveBuffs.Count == 1 &&
                       restoredPlayer.ActiveBuffs[0].RuntimeBuffId == firstRuntimeBuffId &&
                       restoredPlayer.ActiveBuffs[0].StackCount == 2 &&
                       restoredPlayer.ActiveBuffs[0].RemainingFrames == 30);
        }

        private static bool SnapshotRoundTrip(
            BattleWorldState worldState,
            uint frameIndex,
            Func<PlayerState, bool> validate)
        {
            BattleWorldSnapshot snapshotA = worldState.TakeSnapshot().WithFrameIndex(frameIndex);
            ulong hashA = StateHasher.Hash(snapshotA);

            BattleWorldState restoredWorldState = new BattleWorldState();
            restoredWorldState.RestoreSnapshot(snapshotA);
            if (!restoredWorldState.TryGetPlayer(1, out PlayerState restoredPlayer))
            {
                return false;
            }

            BattleWorldSnapshot snapshotB = restoredWorldState.TakeSnapshot().WithFrameIndex(frameIndex);
            ulong hashB = StateHasher.Hash(snapshotB);
            return hashA == hashB && validate(restoredPlayer);
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

            public void EnsureBody(int bodyId, Fixed64 x, Fixed64 y)
            {
            }

            public void RemoveBody(int bodyId)
            {
            }

            public void SetBodyTransform(int bodyId, Fixed64 x, Fixed64 y, bool resetVelocity)
            {
            }

            public void SetBodyKinematicObstacle(int bodyId, bool isKinematicObstacle)
            {
            }

            public void SetBodyMovementInput(int bodyId, Fixed64 dx, Fixed64 dy)
            {
            }

            public void ApplyBodyImpulse(int bodyId, Fixed64 impulseX, Fixed64 impulseY)
            {
            }

            public void Step(Fixed64 dt)
            {
            }

            public bool TryGetBodySnapshot(int bodyId, out PhysicsBodySnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
        }

        private sealed class TestWorldBuffCommandSink : IBuffCommandSink
        {
            private readonly BattleWorldState _worldState;
            private readonly IBuffConfigProvider _configProvider = new DefaultBuffConfigProvider();
            private readonly System.Collections.Generic.List<ApplyBuffCommand> _pendingApplyCommands =
                new System.Collections.Generic.List<ApplyBuffCommand>();
            private readonly System.Collections.Generic.List<RemoveBuffCommand> _pendingRemoveCommands =
                new System.Collections.Generic.List<RemoveBuffCommand>();

            public TestWorldBuffCommandSink(BattleWorldState worldState)
            {
                _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            }

            public void EnqueueApplyBuff(ApplyBuffCommand command)
            {
                _pendingApplyCommands.Add(new ApplyBuffCommand
                {
                    CasterId = command.CasterId,
                    TargetId = command.TargetId,
                    BuffId = command.BuffId,
                    DurationFrames = command.DurationFrames,
                    StackCount = command.StackCount,
                    FrameIndex = command.FrameIndex,
                    Flags = command.Flags,
                    HasDisplacementVelocityOverride = command.HasDisplacementVelocityOverride,
                    DisplacementVelocityX = command.DisplacementVelocityX,
                    DisplacementVelocityY = command.DisplacementVelocityY
                });
                _pendingApplyCommands.Sort(CompareApplyCommands);
            }

            public void EnqueueRemoveBuff(RemoveBuffCommand command)
            {
                _pendingRemoveCommands.Add(new RemoveBuffCommand
                {
                    TargetId = command.TargetId,
                    RuntimeBuffId = command.RuntimeBuffId,
                    BuffId = command.BuffId,
                    RemoveReason = command.RemoveReason,
                    FrameIndex = command.FrameIndex
                });
                _pendingRemoveCommands.Sort(CompareRemoveCommands);
            }

            public bool HasBuff(long targetId, int buffId)
            {
                return _worldState.TryGetPlayer(targetId, out PlayerState targetState) &&
                       BuffSystem.HasBuff(targetState, buffId);
            }

            public int GetBuffStackCount(long targetId, int buffId)
            {
                return _worldState.TryGetPlayer(targetId, out PlayerState targetState)
                    ? BuffSystem.GetBuffStackCount(targetState, buffId)
                    : 0;
            }

            public void Process(uint frameIndex)
            {
                for (int i = 0; i < _pendingApplyCommands.Count; i++)
                {
                    ApplyBuffCommand command = _pendingApplyCommands[i];
                    if (command.FrameIndex > frameIndex)
                    {
                        continue;
                    }

                    if (_worldState.TryGetPlayer(command.TargetId, out PlayerState targetState))
                    {
                        BuffSystem.AddBuff(targetState, command, _configProvider);
                    }
                }

                for (int i = 0; i < _pendingRemoveCommands.Count; i++)
                {
                    RemoveBuffCommand command = _pendingRemoveCommands[i];
                    if (command.FrameIndex > frameIndex)
                    {
                        continue;
                    }

                    if (_worldState.TryGetPlayer(command.TargetId, out PlayerState targetState))
                    {
                        BuffSystem.RemoveBuff(targetState, command);
                    }
                }

                _pendingApplyCommands.Clear();
                _pendingRemoveCommands.Clear();
            }

            private static int CompareApplyCommands(ApplyBuffCommand left, ApplyBuffCommand right)
            {
                int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
                if (byFrame != 0)
                {
                    return byFrame;
                }

                int byTarget = left.TargetId.CompareTo(right.TargetId);
                if (byTarget != 0)
                {
                    return byTarget;
                }

                return left.BuffId.CompareTo(right.BuffId);
            }

            private static int CompareRemoveCommands(RemoveBuffCommand left, RemoveBuffCommand right)
            {
                int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
                if (byFrame != 0)
                {
                    return byFrame;
                }

                int byTarget = left.TargetId.CompareTo(right.TargetId);
                if (byTarget != 0)
                {
                    return byTarget;
                }

                return left.RuntimeBuffId.CompareTo(right.RuntimeBuffId);
            }
        }
    }
}
