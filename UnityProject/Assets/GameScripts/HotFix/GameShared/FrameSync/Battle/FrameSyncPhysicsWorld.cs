using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Battle
{
    public sealed class FrameSyncPhysicsWorld : IPhysicsMovementWorld
    {
        private static readonly Fixed64 PlayerBodyRadius = GameplayRoomSettings.PlayerRadius;
        private static readonly Fixed64 MinimumPlayerSeparation = PlayerBodyRadius * Fixed64.Two;
        private static readonly Fixed64 OccupancyEpsilon = Fixed64.One / (Fixed64)10000;
        private static readonly Fixed64 ContactTolerance = Fixed64.One / (Fixed64)1000;
        private static readonly Fixed64 MovementIntentEpsilon = Fixed64.One / (Fixed64)10000;
        private static readonly Fixed64 Half = Fixed64.One / (Fixed64)2;
        private const int OccupancyResolveIterations = 4;

        private readonly Dictionary<int, FixedPhysicsBody> _bodies = new Dictionary<int, FixedPhysicsBody>();
        private readonly Dictionary<int, FixedPhysicsVector> _pendingLinearVelocities = new Dictionary<int, FixedPhysicsVector>();
        private readonly Dictionary<int, FixedPhysicsVector> _pendingImpulses = new Dictionary<int, FixedPhysicsVector>();
        private readonly Dictionary<ContactKey, PhysicsContactSnapshot> _occupancyContacts = new Dictionary<ContactKey, PhysicsContactSnapshot>();
        private readonly List<int> _sortedBodyIdsBuffer = new List<int>();

        public void ClearBodies()
        {
            _bodies.Clear();
            _pendingLinearVelocities.Clear();
            _pendingImpulses.Clear();
            _occupancyContacts.Clear();
            _sortedBodyIdsBuffer.Clear();
        }

        public void EnsureBody(int bodyId, Fixed64 x, Fixed64 y)
        {
            if (_bodies.ContainsKey(bodyId))
            {
                return;
            }

            _bodies[bodyId] = new FixedPhysicsBody(x, y);
        }

        public void RemoveBody(int bodyId)
        {
            if (!_bodies.Remove(bodyId))
            {
                return;
            }

            _pendingLinearVelocities.Remove(bodyId);
            _pendingImpulses.Remove(bodyId);
            RemoveContactsForBody(bodyId);
        }

        public void SetBodyTransform(int bodyId, Fixed64 x, Fixed64 y, bool resetVelocity)
        {
            EnsureBody(bodyId, x, y);
            FixedPhysicsBody body = _bodies[bodyId];
            body.PositionX = x;
            body.PositionY = y;

            if (resetVelocity)
            {
                body.LinearVelocityX = Fixed64.Zero;
                body.LinearVelocityY = Fixed64.Zero;
                body.AngularVelocity = Fixed64.Zero;
                _pendingLinearVelocities.Remove(bodyId);
                _pendingImpulses.Remove(bodyId);
            }

            _bodies[bodyId] = body;
        }

        public void SetBodyMovementInput(int bodyId, Fixed64 dx, Fixed64 dy)
        {
            NormalizeInput(ref dx, ref dy);
            EnsureBody(bodyId, Fixed64.Zero, Fixed64.Zero);
            _pendingLinearVelocities[bodyId] = new FixedPhysicsVector(
                dx * DeterminismRules.MoveSpeed,
                dy * DeterminismRules.MoveSpeed);
        }

        public void SetBodyKinematicObstacle(int bodyId, bool isKinematicObstacle)
        {
            EnsureBody(bodyId, Fixed64.Zero, Fixed64.Zero);
            FixedPhysicsBody body = _bodies[bodyId];
            body.IsKinematicObstacle = isKinematicObstacle;
            if (isKinematicObstacle)
            {
                body.LinearVelocityX = Fixed64.Zero;
                body.LinearVelocityY = Fixed64.Zero;
                body.AngularVelocity = Fixed64.Zero;
                _pendingLinearVelocities.Remove(bodyId);
                _pendingImpulses.Remove(bodyId);
            }

            _bodies[bodyId] = body;
        }

        public void ApplyBodyImpulse(int bodyId, Fixed64 impulseX, Fixed64 impulseY)
        {
            EnsureBody(bodyId, Fixed64.Zero, Fixed64.Zero);
            FixedPhysicsVector currentImpulse = GetPendingImpulse(bodyId);
            _pendingImpulses[bodyId] = new FixedPhysicsVector(
                currentImpulse.X + impulseX,
                currentImpulse.Y + impulseY);
        }

        public void Step(Fixed64 dt)
        {
            DeterminismRules.AssertFixedDt(dt);

            BuildSortedBodyBuffer();
            Dictionary<int, FixedPhysicsVector> requestedVelocities =
                new Dictionary<int, FixedPhysicsVector>(_bodies.Count);
            for (int i = 0; i < _sortedBodyIdsBuffer.Count; i++)
            {
                int bodyId = _sortedBodyIdsBuffer[i];
                FixedPhysicsBody body = _bodies[bodyId];
                FixedPhysicsVector inputVelocity = GetRequestedVelocity(bodyId, _pendingLinearVelocities);
                FixedPhysicsVector impulse = GetPendingImpulse(bodyId);
                FixedPhysicsVector requestedVelocity = new FixedPhysicsVector(
                    inputVelocity.X + impulse.X,
                    inputVelocity.Y + impulse.Y);
                if (body.IsKinematicObstacle)
                {
                    requestedVelocity = FixedPhysicsVector.Zero;
                }

                requestedVelocities[bodyId] = requestedVelocity;

                body.LinearVelocityX = requestedVelocity.X;
                body.LinearVelocityY = requestedVelocity.Y;
                body.AngularVelocity = Fixed64.Zero;
                if (requestedVelocity.X != Fixed64.Zero || requestedVelocity.Y != Fixed64.Zero)
                {
                    body.IsAwake = true;
                }

                if (body.IsEnabled && body.IsAwake)
                {
                    body.PositionX += body.LinearVelocityX * dt;
                    body.PositionY += body.LinearVelocityY * dt;
                }

                _bodies[bodyId] = body;
            }

            ResolvePlayerOccupancy(requestedVelocities);
            ClampPlayersToRoom();
            RebuildOccupancyContacts();
            _pendingLinearVelocities.Clear();
            _pendingImpulses.Clear();
        }

        public bool TryGetBodySnapshot(int bodyId, out PhysicsBodySnapshot snapshot)
        {
            if (!_bodies.TryGetValue(bodyId, out FixedPhysicsBody body))
            {
                snapshot = default;
                return false;
            }

            snapshot = body.ToSnapshot(bodyId);
            return true;
        }

        public PhysicsWorldSnapshot TakeSnapshot()
        {
            List<PhysicsBodySnapshot> bodies = new List<PhysicsBodySnapshot>(_bodies.Count);
            HashSet<int> snapshotBodyIds = new HashSet<int>();
            foreach (KeyValuePair<int, FixedPhysicsBody> pair in _bodies)
            {
                if (pair.Value.IsKinematicObstacle)
                {
                    continue;
                }

                bodies.Add(pair.Value.ToSnapshot(pair.Key));
                snapshotBodyIds.Add(pair.Key);
            }

            bodies.Sort(PhysicsBodySnapshotComparer.Instance);

            List<PhysicsContactSnapshot> contacts = new List<PhysicsContactSnapshot>(_occupancyContacts.Count);
            foreach (PhysicsContactSnapshot contact in _occupancyContacts.Values)
            {
                if (snapshotBodyIds.Contains(contact.BodyAId) && snapshotBodyIds.Contains(contact.BodyBId))
                {
                    contacts.Add(contact);
                }
            }

            contacts.Sort(PhysicsContactSnapshotComparer.Instance);
            return new PhysicsWorldSnapshot(bodies, contacts);
        }

        public void RestoreSnapshot(PhysicsWorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            ClearBodies();

            IReadOnlyList<PhysicsBodySnapshot> bodies = snapshot.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                PhysicsBodySnapshot bodySnapshot = bodies[i];
                _bodies[bodySnapshot.BodyId] = new FixedPhysicsBody(bodySnapshot);
            }

            IReadOnlyList<PhysicsContactSnapshot> contacts = snapshot.Contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                PhysicsContactSnapshot contact = contacts[i];
                _occupancyContacts[new ContactKey(contact.BodyAId, contact.BodyBId)] = contact;
            }
        }

        private void ClampPlayersToRoom()
        {
            for (int i = 0; i < _sortedBodyIdsBuffer.Count; i++)
            {
                int bodyId = _sortedBodyIdsBuffer[i];
                FixedPhysicsBody body = _bodies[bodyId];
                Fixed64 clampedX = Clamp(body.PositionX, GameplayRoomSettings.PlayerMinX, GameplayRoomSettings.PlayerMaxX);
                Fixed64 clampedY = Clamp(body.PositionY, GameplayRoomSettings.PlayerMinY, GameplayRoomSettings.PlayerMaxY);
                bool touchedHorizontalBoundary = clampedX != body.PositionX;
                bool touchedVerticalBoundary = clampedY != body.PositionY;

                if (!touchedHorizontalBoundary && !touchedVerticalBoundary)
                {
                    continue;
                }

                body.PositionX = clampedX;
                body.PositionY = clampedY;
                if (touchedHorizontalBoundary &&
                    ((clampedX <= GameplayRoomSettings.PlayerMinX && body.LinearVelocityX < Fixed64.Zero) ||
                     (clampedX >= GameplayRoomSettings.PlayerMaxX && body.LinearVelocityX > Fixed64.Zero)))
                {
                    body.LinearVelocityX = Fixed64.Zero;
                }

                if (touchedVerticalBoundary &&
                    ((clampedY <= GameplayRoomSettings.PlayerMinY && body.LinearVelocityY < Fixed64.Zero) ||
                     (clampedY >= GameplayRoomSettings.PlayerMaxY && body.LinearVelocityY > Fixed64.Zero)))
                {
                    body.LinearVelocityY = Fixed64.Zero;
                }

                _bodies[bodyId] = body;
            }
        }

        private void ResolvePlayerOccupancy(IReadOnlyDictionary<int, FixedPhysicsVector> requestedVelocities)
        {
            if (_sortedBodyIdsBuffer.Count <= 1)
            {
                _occupancyContacts.Clear();
                return;
            }

            for (int iteration = 0; iteration < OccupancyResolveIterations; iteration++)
            {
                bool resolvedAnyPair = false;
                for (int i = 0; i < _sortedBodyIdsBuffer.Count - 1; i++)
                {
                    int bodyAId = _sortedBodyIdsBuffer[i];
                    FixedPhysicsBody bodyA = _bodies[bodyAId];
                    Fixed64 positionAX = bodyA.PositionX;
                    Fixed64 positionAY = bodyA.PositionY;

                    for (int j = i + 1; j < _sortedBodyIdsBuffer.Count; j++)
                    {
                        int bodyBId = _sortedBodyIdsBuffer[j];
                        FixedPhysicsBody bodyB = _bodies[bodyBId];
                        Fixed64 deltaX = bodyB.PositionX - positionAX;
                        Fixed64 deltaY = bodyB.PositionY - positionAY;
                        Fixed64 distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                        Fixed64 distance = distanceSquared > OccupancyEpsilon
                            ? FixedMath.Sqrt(distanceSquared)
                            : Fixed64.Zero;
                        Fixed64 penetration = MinimumPlayerSeparation - distance;
                        if (penetration <= OccupancyEpsilon)
                        {
                            continue;
                        }

                        DetermineSeparationNormal(
                            bodyAId,
                            bodyBId,
                            deltaX,
                            deltaY,
                            distanceSquared,
                            requestedVelocities,
                            out Fixed64 normalX,
                            out Fixed64 normalY);
                        CalculateSeparationShares(
                            bodyAId,
                            bodyBId,
                            normalX,
                            normalY,
                            penetration,
                            requestedVelocities,
                            out Fixed64 moveBodyA,
                            out Fixed64 moveBodyB);

                        if (moveBodyA > Fixed64.Zero)
                        {
                            bodyA.PositionX = positionAX - (normalX * moveBodyA);
                            bodyA.PositionY = positionAY - (normalY * moveBodyA);
                            _bodies[bodyAId] = bodyA;
                            positionAX = bodyA.PositionX;
                            positionAY = bodyA.PositionY;
                        }

                        if (moveBodyB > Fixed64.Zero)
                        {
                            bodyB.PositionX += normalX * moveBodyB;
                            bodyB.PositionY += normalY * moveBodyB;
                            _bodies[bodyBId] = bodyB;
                        }

                        resolvedAnyPair = true;
                    }
                }

                if (!resolvedAnyPair)
                {
                    break;
                }
            }
        }

        private void RebuildOccupancyContacts()
        {
            _occupancyContacts.Clear();
            if (_sortedBodyIdsBuffer.Count <= 1)
            {
                return;
            }

            Fixed64 contactDistance = MinimumPlayerSeparation + ContactTolerance;
            Fixed64 contactDistanceSquared = contactDistance * contactDistance;
            for (int i = 0; i < _sortedBodyIdsBuffer.Count - 1; i++)
            {
                int bodyAId = _sortedBodyIdsBuffer[i];
                FixedPhysicsBody bodyA = _bodies[bodyAId];

                for (int j = i + 1; j < _sortedBodyIdsBuffer.Count; j++)
                {
                    int bodyBId = _sortedBodyIdsBuffer[j];
                    FixedPhysicsBody bodyB = _bodies[bodyBId];
                    Fixed64 deltaX = bodyB.PositionX - bodyA.PositionX;
                    Fixed64 deltaY = bodyB.PositionY - bodyA.PositionY;
                    if ((deltaX * deltaX) + (deltaY * deltaY) > contactDistanceSquared)
                    {
                        continue;
                    }

                    ContactKey key = new ContactKey(bodyAId, bodyBId);
                    _occupancyContacts[key] = new PhysicsContactSnapshot(key.BodyAId, key.BodyBId, true);
                }
            }
        }

        private void BuildSortedBodyBuffer()
        {
            _sortedBodyIdsBuffer.Clear();
            foreach (int bodyId in _bodies.Keys)
            {
                _sortedBodyIdsBuffer.Add(bodyId);
            }

            _sortedBodyIdsBuffer.Sort();
        }

        private static void DetermineSeparationNormal(
            int bodyAId,
            int bodyBId,
            Fixed64 deltaX,
            Fixed64 deltaY,
            Fixed64 distanceSquared,
            IReadOnlyDictionary<int, FixedPhysicsVector> requestedVelocities,
            out Fixed64 normalX,
            out Fixed64 normalY)
        {
            FixedPhysicsVector requestedVelocityA = GetRequestedVelocity(bodyAId, requestedVelocities);
            FixedPhysicsVector requestedVelocityB = GetRequestedVelocity(bodyBId, requestedVelocities);
            SeparationNormalResolver.Resolve(
                bodyAId,
                bodyBId,
                deltaX,
                deltaY,
                distanceSquared,
                requestedVelocityA.X,
                requestedVelocityA.Y,
                requestedVelocityB.X,
                requestedVelocityB.Y,
                true,
                out normalX,
                out normalY);
        }

        private void CalculateSeparationShares(
            int bodyAId,
            int bodyBId,
            Fixed64 normalX,
            Fixed64 normalY,
            Fixed64 penetration,
            IReadOnlyDictionary<int, FixedPhysicsVector> requestedVelocities,
            out Fixed64 moveBodyA,
            out Fixed64 moveBodyB)
        {
            bool bodyAKinematic = _bodies.TryGetValue(bodyAId, out FixedPhysicsBody bodyA) &&
                                  bodyA.IsKinematicObstacle;
            bool bodyBKinematic = _bodies.TryGetValue(bodyBId, out FixedPhysicsBody bodyB) &&
                                  bodyB.IsKinematicObstacle;
            if (bodyAKinematic || bodyBKinematic)
            {
                if (bodyAKinematic && bodyBKinematic)
                {
                    moveBodyA = Fixed64.Zero;
                    moveBodyB = Fixed64.Zero;
                    return;
                }

                moveBodyA = bodyAKinematic ? Fixed64.Zero : penetration;
                moveBodyB = bodyBKinematic ? Fixed64.Zero : penetration;
                return;
            }

            Fixed64 bodyATowardIntent = GetTowardIntent(bodyAId, normalX, normalY, requestedVelocities);
            Fixed64 bodyBTowardIntent = GetTowardIntent(bodyBId, -normalX, -normalY, requestedVelocities);
            bool bodyAPushing = bodyATowardIntent > MovementIntentEpsilon;
            bool bodyBPushing = bodyBTowardIntent > MovementIntentEpsilon;

            if (bodyAPushing && bodyBPushing)
            {
                Fixed64 totalIntent = bodyATowardIntent + bodyBTowardIntent;
                if (totalIntent <= MovementIntentEpsilon)
                {
                    moveBodyA = penetration * Half;
                    moveBodyB = penetration - moveBodyA;
                    return;
                }

                moveBodyA = penetration * (bodyATowardIntent / totalIntent);
                moveBodyB = penetration - moveBodyA;
                return;
            }

            if (bodyAPushing)
            {
                moveBodyA = penetration;
                moveBodyB = Fixed64.Zero;
                return;
            }

            if (bodyBPushing)
            {
                moveBodyA = Fixed64.Zero;
                moveBodyB = penetration;
                return;
            }

            moveBodyA = penetration * Half;
            moveBodyB = penetration - moveBodyA;
        }

        private static Fixed64 GetTowardIntent(
            int bodyId,
            Fixed64 normalX,
            Fixed64 normalY,
            IReadOnlyDictionary<int, FixedPhysicsVector> requestedVelocities)
        {
            FixedPhysicsVector requestedVelocity = GetRequestedVelocity(bodyId, requestedVelocities);
            Fixed64 towardIntent = (requestedVelocity.X * normalX) + (requestedVelocity.Y * normalY);
            return towardIntent > Fixed64.Zero ? towardIntent : Fixed64.Zero;
        }

        private static FixedPhysicsVector GetRequestedVelocity(
            int bodyId,
            IReadOnlyDictionary<int, FixedPhysicsVector> requestedVelocities)
        {
            return requestedVelocities.TryGetValue(bodyId, out FixedPhysicsVector requestedVelocity)
                ? requestedVelocity
                : FixedPhysicsVector.Zero;
        }

        private FixedPhysicsVector GetPendingImpulse(int bodyId)
        {
            return _pendingImpulses.TryGetValue(bodyId, out FixedPhysicsVector impulse)
                ? impulse
                : FixedPhysicsVector.Zero;
        }

        private static Fixed64 Clamp(Fixed64 value, Fixed64 minimum, Fixed64 maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private void RemoveContactsForBody(int bodyId)
        {
            List<ContactKey> keysToRemove = new List<ContactKey>();
            foreach (ContactKey key in _occupancyContacts.Keys)
            {
                if (key.BodyAId == bodyId || key.BodyBId == bodyId)
                {
                    keysToRemove.Add(key);
                }
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                _occupancyContacts.Remove(keysToRemove[i]);
            }
        }

        private static void NormalizeInput(ref Fixed64 dx, ref Fixed64 dy)
        {
            Fixed64 lengthSquared = (dx * dx) + (dy * dy);
            if (lengthSquared <= Fixed64.One)
            {
                return;
            }

            Fixed64 inverseLength = Fixed64.One / FixedMath.Sqrt(lengthSquared);
            dx *= inverseLength;
            dy *= inverseLength;
        }

        private readonly struct ContactKey : IEquatable<ContactKey>
        {
            public ContactKey(int bodyAId, int bodyBId)
            {
                if (bodyAId <= bodyBId)
                {
                    BodyAId = bodyAId;
                    BodyBId = bodyBId;
                }
                else
                {
                    BodyAId = bodyBId;
                    BodyBId = bodyAId;
                }
            }

            public int BodyAId { get; }
            public int BodyBId { get; }

            public bool Equals(ContactKey other)
            {
                return BodyAId == other.BodyAId && BodyBId == other.BodyBId;
            }

#nullable enable
            public override bool Equals(object? obj)
            {
                return obj is ContactKey other && Equals(other);
            }
#nullable restore

            public override int GetHashCode()
            {
                return HashCode.Combine(BodyAId, BodyBId);
            }
        }

        private sealed class PhysicsBodySnapshotComparer : IComparer<PhysicsBodySnapshot>
        {
            public static readonly PhysicsBodySnapshotComparer Instance = new PhysicsBodySnapshotComparer();

            public int Compare(PhysicsBodySnapshot x, PhysicsBodySnapshot y)
            {
                return x.BodyId.CompareTo(y.BodyId);
            }
        }

        private sealed class PhysicsContactSnapshotComparer : IComparer<PhysicsContactSnapshot>
        {
            public static readonly PhysicsContactSnapshotComparer Instance = new PhysicsContactSnapshotComparer();

            public int Compare(PhysicsContactSnapshot x, PhysicsContactSnapshot y)
            {
                int left = x.BodyAId.CompareTo(y.BodyAId);
                return left != 0 ? left : x.BodyBId.CompareTo(y.BodyBId);
            }
        }
    }
}
