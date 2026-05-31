#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using System;
using GameShared.Badminton;
using GameShared.Badminton.Config;
using GameShared.FrameSync.Determinism;
using NUnit.Framework;
using UnityEngine;

namespace GameShared.Badminton.Tests
{
    [TestFixture]
    public sealed class ShuttlecockEntityTests
    {
        [Test]
        public void LaunchCommand_OnlyConsumesOnTargetFrame()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 2,
                ShotType = ShuttlecockShotType.Clear,
                OriginXZ = new Vector2(0.0f, -1.5f),
                OriginY = 1.2f,
                DirectionXZ = Vector2.up,
            });

            entity.Tick(1, DeterminismRules.FixedDeltaTime);
            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.Idle));
            Assert.That(entity.PendingCommandCount, Is.EqualTo(1));

            entity.Tick(2, DeterminismRules.FixedDeltaTime);
            Assert.That(entity.PendingCommandCount, Is.EqualTo(0));
            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.Flying));
            Assert.That(entity.State.ActiveShotType, Is.EqualTo(ShuttlecockShotType.Clear));
            Assert.That(entity.State.XZ.y, Is.GreaterThan(-1.5f));
        }

        [Test]
        public void Physics_TouchdownInBounds_BecomesLanded()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.State.XZ = new Vector2(0.0f, 0.0f);
            entity.State.Y = 0.1f;
            entity.State.Vxz = Vector2.zero;
            entity.State.Vy = -1.0f;
            entity.State.HorizontalDrag = 0.92f;
            entity.State.ActiveShotType = ShuttlecockShotType.NetShot;
            entity.State.Phase = ShuttlecockFlightPhase.Flying;

            TickUntilTerminal(entity, 10);

            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.Landed));
            Assert.That(entity.State.IsInBounds, Is.True);
            Assert.That(entity.State.Y, Is.EqualTo(0.0f));
            Assert.That(entity.State.LandingXZ, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void Physics_TouchdownOutsideBounds_BecomesOutOfBounds()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.State.XZ = new Vector2(CourtConstants.HalfSinglesWidth + 0.5f, 0.0f);
            entity.State.Y = 0.1f;
            entity.State.Vxz = Vector2.zero;
            entity.State.Vy = -1.0f;
            entity.State.HorizontalDrag = 0.92f;
            entity.State.ActiveShotType = ShuttlecockShotType.NetShot;
            entity.State.Phase = ShuttlecockFlightPhase.Flying;

            TickUntilTerminal(entity, 10);

            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.OutOfBounds));
            Assert.That(entity.State.IsInBounds, Is.False);
        }

        [Test]
        public void RollBack_RestoresSnapshotAndClearsFutureCommands()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 2,
                ShotType = ShuttlecockShotType.Drive,
                OriginXZ = Vector2.zero,
                OriginY = 1.0f,
                DirectionXZ = Vector2.up,
            });
            entity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 20,
                ShotType = ShuttlecockShotType.Clear,
                OriginXZ = new Vector2(1.0f, 1.0f),
                OriginY = 2.0f,
                DirectionXZ = Vector2.right,
            });

            for (uint frame = 1; frame <= 10; frame++)
            {
                entity.Tick(frame, DeterminismRules.FixedDeltaTime);
            }

            Assert.That(entity.TryGetSnapshot(5, out ShuttlecockSnapshot snapshotAtFive), Is.True);
            entity.RollBack(5);

            ShuttlecockSnapshot restored = entity.TakeSnapshot().WithFrameIndex(5);
            AssertSnapshotsEqual(snapshotAtFive, restored);
            Assert.That(entity.PendingCommandCount, Is.EqualTo(0));
        }

        [Test]
        public void CheckConsistency_ThrowsWhenHashDiffers()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.SetExpectedHash(0, 1UL);

            Assert.Throws<InvalidOperationException>(() => entity.CheckConsistency(0));
        }

        [Test]
        public void DeterminismAndRollbackSelfTest_DoNotThrow()
        {
            ShuttlecockLaunchCommand command = new ShuttlecockLaunchCommand
            {
                TargetFrame = 1,
                ShotType = ShuttlecockShotType.Drop,
                OriginXZ = new Vector2(0.0f, -2.0f),
                OriginY = 1.3f,
                DirectionXZ = Vector2.up,
            };

            Assert.DoesNotThrow(() => ShuttlecockEntity.DeterminismSelfTest(command, ShuttlecockShotConfigFallback.Instance));
            Assert.DoesNotThrow(() => ShuttlecockEntity.RollbackSelfTest(command, ShuttlecockShotConfigFallback.Instance));
        }

        [Test]
        public void AcceptanceGuideScenarios_ClearAndSmashStayWithinFlightWindows()
        {
            float clearFlightTime = MeasureFlightTime(
                new ShuttlecockShotDefinition(ShuttlecockShotType.Clear, 14.0f, 55.0f, 0.92f),
                originY: 2.0f);
            float smashFlightTime = MeasureFlightTime(
                new ShuttlecockShotDefinition(ShuttlecockShotType.Smash, 22.0f, -15.0f, 0.92f),
                originY: 2.5f);

            Assert.That(clearFlightTime, Is.InRange(1.2f, 1.5f));
            Assert.That(smashFlightTime, Is.InRange(0.3f, 0.4f));
        }

        private static ShuttlecockEntity CreateEntity()
        {
            return new ShuttlecockEntity(ShuttlecockShotConfigFallback.Instance);
        }

        private static void TickUntilTerminal(ShuttlecockEntity entity, uint maxFrames)
        {
            for (uint frame = 1; frame <= maxFrames; frame++)
            {
                entity.Tick(frame, DeterminismRules.FixedDeltaTime);
                if (entity.State.IsTerminal)
                {
                    return;
                }
            }

            Assert.Fail($"Entity did not reach terminal state within {maxFrames} frames.");
        }

        private static float MeasureFlightTime(ShuttlecockShotDefinition definition, float originY)
        {
            SingleShotProvider provider = new SingleShotProvider(definition);
            ShuttlecockEntity entity = new ShuttlecockEntity(provider);
            entity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 1,
                ShotType = definition.ShotType,
                OriginXZ = new Vector2(0.0f, -3.0f),
                OriginY = originY,
                DirectionXZ = Vector2.up
            });

            uint finalFrame = 0;
            for (uint frame = 1; frame <= 180; frame++)
            {
                entity.Tick(frame, DeterminismRules.FixedDeltaTime);
                if (entity.State.IsTerminal)
                {
                    finalFrame = frame;
                    break;
                }
            }

            Assert.That(finalFrame, Is.GreaterThan(0u));
            return finalFrame * DeterminismRules.FixedDeltaTime;
        }

        private static void AssertSnapshotsEqual(ShuttlecockSnapshot expected, ShuttlecockSnapshot actual)
        {
            Assert.That(BitConverter.SingleToInt32Bits(actual.XZ.x), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.XZ.x)));
            Assert.That(BitConverter.SingleToInt32Bits(actual.XZ.y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.XZ.y)));
            Assert.That(BitConverter.SingleToInt32Bits(actual.Y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Y)));
            Assert.That(BitConverter.SingleToInt32Bits(actual.Vxz.x), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Vxz.x)));
            Assert.That(BitConverter.SingleToInt32Bits(actual.Vxz.y), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Vxz.y)));
            Assert.That(BitConverter.SingleToInt32Bits(actual.Vy), Is.EqualTo(BitConverter.SingleToInt32Bits(expected.Vy)));
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(actual.LastValidFlyingFrame, Is.EqualTo(expected.LastValidFlyingFrame));
            Assert.That(actual.IsInBounds, Is.EqualTo(expected.IsInBounds));
            Assert.That(actual.ActiveShotType, Is.EqualTo(expected.ActiveShotType));
        }

        private sealed class SingleShotProvider : IShuttlecockShotConfigProvider
        {
            private readonly ShuttlecockShotDefinition _definition;

            public SingleShotProvider(ShuttlecockShotDefinition definition)
            {
                _definition = definition;
            }

            public bool TryGet(ShuttlecockShotType shotType, out ShuttlecockShotDefinition definition)
            {
                definition = _definition;
                return shotType == _definition.ShotType;
            }
        }
    }
}
#endif
