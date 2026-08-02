#if FANTASY_UNITY && UNITY_INCLUDE_TESTS
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using NUnit.Framework;

namespace GameShared.FixedPoint.Tests
{
    [TestFixture]
    public sealed class FixedMathSharpTests
    {
        [Test]
        public void Fixed64_AccumulationMatchesRawMultiplication()
        {
            Fixed64 step = DeterminismRules.FixedDeltaTimeFixed64;
            Fixed64 accumulated = Fixed64.Zero;

            for (int i = 0; i < 30; i++)
            {
                accumulated += step;
            }

            AssertFixedRawEqual(step * 30, accumulated);
        }

        [Test]
        public void Vector2d_NormalizationIsRawStable()
        {
            Vector2d first = new Vector2d(3, 4).Normal;
            Vector2d second = new Vector2d(3, 4).Normal;

            AssertFixedRawEqual(first.x, second.x);
            AssertFixedRawEqual(first.y, second.y);
            Assert.That((double)first.Magnitude, Is.InRange(0.999, 1.001));
        }

        [Test]
        public void FixedMath_TranscendentalsAreRawStable()
        {
            Fixed64 angle = FixedMath.DegToRad(new Fixed64(55.0));
            Fixed64 sinFirst = FixedMath.Sin(angle);
            Fixed64 sinSecond = FixedMath.Sin(angle);

            Fixed64 drag = FixedMath.Exp(-new Fixed64(0.92) * DeterminismRules.FixedDeltaTimeFixed64);
            Fixed64 dragRepeat = FixedMath.Exp(-new Fixed64(0.92) * DeterminismRules.FixedDeltaTimeFixed64);

            AssertFixedRawEqual(sinFirst, sinSecond);
            AssertFixedRawEqual(drag, dragRepeat);
            Assert.That((double)sinFirst, Is.InRange(0.81, 0.83));
        }

        [Test]
        public void TickAccumulatorDefaultDelta_MatchesSharedFixedDelta()
        {
            Fixed64 accumulatorDelta = (Fixed64)TickAccumulator.DefaultFixedDeltaTime;

            AssertFixedRawEqual(DeterminismRules.FixedDeltaTimeFixed64, accumulatorDelta);
        }

        [Test]
        public void MoveSystem_SameFixedInputProducesSameRawState()
        {
            BattleWorldState firstWorld = new BattleWorldState();
            BattleWorldState secondWorld = new BattleWorldState();
            firstWorld.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);
            secondWorld.AddOrUpdatePlayer(1, Fixed64.Zero, Fixed64.Zero);

            Assert.That(firstWorld.TryGetPlayer(1, out PlayerState firstPlayer), Is.True);
            Assert.That(secondWorld.TryGetPlayer(1, out PlayerState secondPlayer), Is.True);

            Fixed64 dt = DeterminismRules.FixedDeltaTimeFixed64;
            Fixed64 dx = new Fixed64(0.75);
            Fixed64 dy = new Fixed64(0.25);
            MoveSystem.Apply(firstWorld, firstPlayer, dx, dy, dt);
            MoveSystem.Apply(secondWorld, secondPlayer, dx, dy, dt);
            firstWorld.PhysicsWorld.Step(dt);
            secondWorld.PhysicsWorld.Step(dt);
            MoveSystem.SyncFromPhysics(firstWorld, firstPlayer);
            MoveSystem.SyncFromPhysics(secondWorld, secondPlayer);

            AssertFixedRawEqual(firstPlayer.X, secondPlayer.X);
            AssertFixedRawEqual(firstPlayer.Y, secondPlayer.Y);
        }

        [Test]
        public void PhysicsWorld_SameFixedInputProducesSameRawSnapshot()
        {
            FrameSyncPhysicsWorld firstWorld = new FrameSyncPhysicsWorld();
            FrameSyncPhysicsWorld secondWorld = new FrameSyncPhysicsWorld();
            Fixed64 dt = DeterminismRules.FixedDeltaTimeFixed64;

            firstWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            secondWorld.EnsureBody(1, Fixed64.Zero, Fixed64.Zero);
            firstWorld.SetBodyMovementInput(1, Fixed64.One, Fixed64.Zero);
            secondWorld.SetBodyMovementInput(1, Fixed64.One, Fixed64.Zero);
            firstWorld.Step(dt);
            secondWorld.Step(dt);

            PhysicsWorldSnapshot firstSnapshot = firstWorld.TakeSnapshot();
            PhysicsWorldSnapshot secondSnapshot = secondWorld.TakeSnapshot();

            Assert.That(secondSnapshot.Bodies.Count, Is.EqualTo(firstSnapshot.Bodies.Count));
            for (int i = 0; i < firstSnapshot.Bodies.Count; i++)
            {
                PhysicsBodySnapshot expected = firstSnapshot.Bodies[i];
                PhysicsBodySnapshot actual = secondSnapshot.Bodies[i];
                Assert.That(actual.BodyId, Is.EqualTo(expected.BodyId));
                AssertFixedRawEqual(expected.PositionX, actual.PositionX);
                AssertFixedRawEqual(expected.PositionY, actual.PositionY);
                AssertFixedRawEqual(expected.RotationRadians, actual.RotationRadians);
                AssertFixedRawEqual(expected.LinearVelocityX, actual.LinearVelocityX);
                AssertFixedRawEqual(expected.LinearVelocityY, actual.LinearVelocityY);
                AssertFixedRawEqual(expected.AngularVelocity, actual.AngularVelocity);
            }
        }

        [Test]
        public void PhysicsWorld_ClampsPlayerToGameplayRoom()
        {
            FrameSyncPhysicsWorld world = new FrameSyncPhysicsWorld();
            Fixed64 dt = DeterminismRules.FixedDeltaTimeFixed64;

            world.EnsureBody(1, GameplayRoomSettings.PlayerMaxX, Fixed64.Zero);
            world.SetBodyMovementInput(1, Fixed64.One, Fixed64.Zero);
            world.Step(dt);

            Assert.That(world.TryGetBodySnapshot(1, out PhysicsBodySnapshot snapshot), Is.True);
            AssertFixedRawEqual(GameplayRoomSettings.PlayerMaxX, snapshot.PositionX);
            AssertFixedRawEqual(Fixed64.Zero, snapshot.LinearVelocityX);
        }

        private static void AssertFixedRawEqual(Fixed64 expected, Fixed64 actual)
        {
            Assert.That(actual.m_rawValue, Is.EqualTo(expected.m_rawValue));
        }
    }
}
#endif
