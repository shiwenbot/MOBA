#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using System;
using FixedMathSharp;
using GameShared.Badminton;
using GameShared.Badminton.Config;
using GameShared.FrameSync.Determinism;
using NUnit.Framework;

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
                OriginXZ = new Vector2d(0.0, -1.5),
                OriginY = new Fixed64(1.2),
                DirectionXZ = Vector2d.Forward,
            });

            entity.Tick(1, DeterminismRules.FixedDeltaTime);
            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.Idle));
            Assert.That(entity.PendingCommandCount, Is.EqualTo(1));

            entity.Tick(2, DeterminismRules.FixedDeltaTime);
            Assert.That(entity.PendingCommandCount, Is.EqualTo(0));
            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.Flying));
            Assert.That(entity.State.ActiveShotType, Is.EqualTo(ShuttlecockShotType.Clear));
            Assert.That(entity.State.XZ.y, Is.GreaterThan(new Fixed64(-1.5)));
        }

        [Test]
        public void Physics_TouchdownInBounds_BecomesLanded()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.State.XZ = Vector2d.Zero;
            entity.State.Y = new Fixed64(0.1);
            entity.State.Vxz = Vector2d.Zero;
            entity.State.Vy = new Fixed64(-1.0);
            entity.State.HorizontalDrag = new Fixed64(0.92);
            entity.State.ActiveShotType = ShuttlecockShotType.NetShot;
            entity.State.Phase = ShuttlecockFlightPhase.Flying;

            TickUntilTerminal(entity, 10);

            Assert.That(entity.State.Phase, Is.EqualTo(ShuttlecockFlightPhase.Landed));
            Assert.That(entity.State.IsInBounds, Is.True);
            Assert.That(entity.State.Y, Is.EqualTo(Fixed64.Zero));
            Assert.That(entity.State.LandingXZ, Is.EqualTo(Vector2d.Zero));
        }

        [Test]
        public void Physics_TouchdownOutsideBounds_BecomesOutOfBounds()
        {
            ShuttlecockEntity entity = CreateEntity();
            entity.State.XZ = new Vector2d(CourtConstants.HalfSinglesWidth + new Fixed64(0.5), Fixed64.Zero);
            entity.State.Y = new Fixed64(0.1);
            entity.State.Vxz = Vector2d.Zero;
            entity.State.Vy = new Fixed64(-1.0);
            entity.State.HorizontalDrag = new Fixed64(0.92);
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
                OriginXZ = Vector2d.Zero,
                OriginY = Fixed64.One,
                DirectionXZ = Vector2d.Forward,
            });
            entity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 20,
                ShotType = ShuttlecockShotType.Clear,
                OriginXZ = new Vector2d(1, 1),
                OriginY = Fixed64.Two,
                DirectionXZ = Vector2d.Right,
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
                OriginXZ = new Vector2d(0.0, -2.0),
                OriginY = new Fixed64(1.3),
                DirectionXZ = Vector2d.Forward,
            };

            Assert.DoesNotThrow(() => ShuttlecockEntity.DeterminismSelfTest(command, ShuttlecockShotConfigFallback.Instance));
            Assert.DoesNotThrow(() => ShuttlecockEntity.RollbackSelfTest(command, ShuttlecockShotConfigFallback.Instance));
        }

        [Test]
        public void AcceptanceGuideScenarios_ClearAndSmashStayWithinFlightWindows()
        {
            float clearFlightTime = MeasureFlightTime(
                new ShuttlecockShotDefinition(
                    ShuttlecockShotType.Clear,
                    new Fixed64(14.0),
                    new Fixed64(55.0),
                    new Fixed64(0.92)),
                originY: new Fixed64(2.0));
            float smashFlightTime = MeasureFlightTime(
                new ShuttlecockShotDefinition(
                    ShuttlecockShotType.Smash,
                    new Fixed64(22.0),
                    new Fixed64(-15.0),
                    new Fixed64(0.92)),
                originY: new Fixed64(2.5));

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

        private static float MeasureFlightTime(ShuttlecockShotDefinition definition, Fixed64 originY)
        {
            SingleShotProvider provider = new SingleShotProvider(definition);
            ShuttlecockEntity entity = new ShuttlecockEntity(provider);
            entity.EnqueueLaunch(new ShuttlecockLaunchCommand
            {
                TargetFrame = 1,
                ShotType = definition.ShotType,
                OriginXZ = new Vector2d(0.0, -3.0),
                OriginY = originY,
                DirectionXZ = Vector2d.Forward
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
            AssertFixedRawEqual(expected.XZ.x, actual.XZ.x);
            AssertFixedRawEqual(expected.XZ.y, actual.XZ.y);
            AssertFixedRawEqual(expected.Y, actual.Y);
            AssertFixedRawEqual(expected.Vxz.x, actual.Vxz.x);
            AssertFixedRawEqual(expected.Vxz.y, actual.Vxz.y);
            AssertFixedRawEqual(expected.Vy, actual.Vy);
            AssertFixedRawEqual(expected.LandingXZ.x, actual.LandingXZ.x);
            AssertFixedRawEqual(expected.LandingXZ.y, actual.LandingXZ.y);
            AssertFixedRawEqual(expected.HorizontalDrag, actual.HorizontalDrag);
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(actual.LastValidFlyingFrame, Is.EqualTo(expected.LastValidFlyingFrame));
            Assert.That(actual.IsInBounds, Is.EqualTo(expected.IsInBounds));
            Assert.That(actual.ActiveShotType, Is.EqualTo(expected.ActiveShotType));
        }

        private static void AssertFixedRawEqual(Fixed64 expected, Fixed64 actual)
        {
            Assert.That(actual.m_rawValue, Is.EqualTo(expected.m_rawValue));
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
