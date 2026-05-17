using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Box2DSharp.Collision.Collider;
using Box2DSharp.Collision.Shapes;
using Box2DSharp.Dynamics;
using Box2DSharp.Dynamics.Contacts;
using GameShared.FrameSync.Determinism;

namespace GameShared.FrameSync.Battle
{
    public sealed class FrameSyncPhysicsWorld : IPhysicsMovementWorld
    {
        private const float PlayerBodyRadius = 0.45f;
        private const float MinimumPlayerSeparation = PlayerBodyRadius * 2.0f;
        private const float PlayerDensity = 1.0f;
        private const short PlayerNoPushGroupIndex = -1;
        private const float OccupancyEpsilon = 0.0001f;
        private const float ContactTolerance = 0.001f;
        private const float MovementIntentEpsilon = 0.0001f;
        private const int OccupancyResolveIterations = 4;
        private const int VelocityIterations = 12;
        private const int PositionIterations = 8;
        private static readonly PropertyInfo BodyLinearVelocityProperty =
            typeof(Body).GetProperty(nameof(Body.LinearVelocity), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo BodyAngularVelocityProperty =
            typeof(Body).GetProperty(nameof(Body.AngularVelocity), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private readonly Dictionary<int, Body> _bodies = new Dictionary<int, Body>();
        private readonly Dictionary<int, Vector2> _pendingLinearVelocities = new Dictionary<int, Vector2>();
        private readonly Dictionary<ContactKey, PhysicsContactSnapshot> _physicsContacts = new Dictionary<ContactKey, PhysicsContactSnapshot>();
        private readonly Dictionary<ContactKey, PhysicsContactSnapshot> _occupancyContacts = new Dictionary<ContactKey, PhysicsContactSnapshot>();
        private readonly List<int> _sortedBodyIdsBuffer = new List<int>();
        private readonly ContactListener _contactListener;
        private readonly World _world;

        public FrameSyncPhysicsWorld()
        {
            Vector2 gravity = Vector2.Zero;
            _world = new World(in gravity);
            _contactListener = new ContactListener(_physicsContacts);
            _world.SetContactListener(_contactListener);
        }

        public void ClearBodies()
        {
            foreach (Body body in _bodies.Values)
            {
                _world.DestroyBody(body);
            }

            _bodies.Clear();
            _pendingLinearVelocities.Clear();
            _physicsContacts.Clear();
            _occupancyContacts.Clear();
            _sortedBodyIdsBuffer.Clear();
        }

        public void EnsureBody(int bodyId, float x, float y)
        {
            DeterminismRules.AssertFinite(x, nameof(x));
            DeterminismRules.AssertFinite(y, nameof(y));

            if (_bodies.ContainsKey(bodyId))
            {
                return;
            }

            BodyDef bodyDef = new BodyDef
            {
                BodyType = BodyType.DynamicBody,
                Position = new Vector2(x, y),
                LinearVelocity = Vector2.Zero,
                Bullet = true,
                FixedRotation = true,
                LinearDamping = 0.0f,
                AngularDamping = 0.0f,
                UserData = bodyId
            };
            bodyDef.AllowSleep = false;
            bodyDef.Awake = true;
            bodyDef.Enabled = true;
            bodyDef.GravityScale = 0.0f;

            Body body = _world.CreateBody(in bodyDef);
            CircleShape circleShape = new CircleShape
            {
                Position = Vector2.Zero,
                Radius = PlayerBodyRadius
            };
            FixtureDef fixtureDef = new FixtureDef
            {
                Shape = circleShape,
                Density = PlayerDensity,
                Friction = 0.0f,
                Restitution = 0.0f,
                RestitutionThreshold = 0.0f,
                IsSensor = false,
                Filter = new Filter
                {
                    GroupIndex = PlayerNoPushGroupIndex
                },
                UserData = bodyId
            };
            body.CreateFixture(fixtureDef);
            body.UserData = bodyId;
            _bodies[bodyId] = body;
        }

        public void RemoveBody(int bodyId)
        {
            if (!_bodies.TryGetValue(bodyId, out Body body))
            {
                return;
            }

            _world.DestroyBody(body);
            _bodies.Remove(bodyId);
            _pendingLinearVelocities.Remove(bodyId);
            RemoveContactsForBody(bodyId);
        }

        public void SetBodyTransform(int bodyId, float x, float y, bool resetVelocity)
        {
            DeterminismRules.AssertFinite(x, nameof(x));
            DeterminismRules.AssertFinite(y, nameof(y));

            EnsureBody(bodyId, x, y);
            Body body = _bodies[bodyId];
            Vector2 position = new Vector2(x, y);
            body.SetTransform(in position, body.GetAngle());

            if (resetVelocity)
            {
                SetBodyLinearVelocity(body, Vector2.Zero);
                SetBodyAngularVelocity(body, 0.0f);
                _pendingLinearVelocities.Remove(bodyId);
            }
        }

        public void SetBodyMovementInput(int bodyId, float dx, float dy)
        {
            DeterminismRules.AssertFinite(dx, nameof(dx));
            DeterminismRules.AssertFinite(dy, nameof(dy));

            NormalizeInput(ref dx, ref dy);
            EnsureBody(bodyId, 0.0f, 0.0f);
            _pendingLinearVelocities[bodyId] = new Vector2(
                dx * DeterminismRules.MoveSpeed,
                dy * DeterminismRules.MoveSpeed);
        }

        public void Step(float dt)
        {
            DeterminismRules.AssertFixedDt(dt);

            BuildSortedBodyBuffer();
            Dictionary<int, Vector2> requestedVelocities = new Dictionary<int, Vector2>(_bodies.Count);
            for (int i = 0; i < _sortedBodyIdsBuffer.Count; i++)
            {
                int bodyId = _sortedBodyIdsBuffer[i];
                Body body = _bodies[bodyId];
                Vector2 requestedVelocity = _pendingLinearVelocities.TryGetValue(bodyId, out Vector2 cachedVelocity)
                    ? cachedVelocity
                    : Vector2.Zero;
                requestedVelocities[bodyId] = requestedVelocity;
                SetBodyLinearVelocity(body, requestedVelocity);
                SetBodyAngularVelocity(body, 0.0f);
            }

            _world.Step(dt, VelocityIterations, PositionIterations);
            ResolvePlayerOccupancy(requestedVelocities);
            RebuildOccupancyContacts();
            _pendingLinearVelocities.Clear();
        }

        public bool TryGetBodySnapshot(int bodyId, out PhysicsBodySnapshot snapshot)
        {
            if (!_bodies.TryGetValue(bodyId, out Body body))
            {
                snapshot = default;
                return false;
            }

            snapshot = CaptureBodySnapshot(bodyId, body);
            return true;
        }

        public PhysicsWorldSnapshot TakeSnapshot()
        {
            PhysicsBodySnapshot[] bodies = new PhysicsBodySnapshot[_bodies.Count];
            int index = 0;
            foreach (KeyValuePair<int, Body> pair in _bodies)
            {
                bodies[index++] = CaptureBodySnapshot(pair.Key, pair.Value);
            }

            Array.Sort(bodies, PhysicsBodySnapshotComparer.Instance);

            PhysicsContactSnapshot[] contacts = BuildMergedContacts();
            Array.Sort(contacts, PhysicsContactSnapshotComparer.Instance);
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
                EnsureBody(bodySnapshot.BodyId, bodySnapshot.PositionX, bodySnapshot.PositionY);
                Body body = _bodies[bodySnapshot.BodyId];
                Vector2 position = new Vector2(bodySnapshot.PositionX, bodySnapshot.PositionY);
                body.SetTransform(in position, bodySnapshot.RotationRadians);
                SetBodyLinearVelocity(body, new Vector2(bodySnapshot.LinearVelocityX, bodySnapshot.LinearVelocityY));
                SetBodyAngularVelocity(body, bodySnapshot.AngularVelocity);
                body.IsAwake = bodySnapshot.IsAwake;
                body.IsEnabled = bodySnapshot.IsEnabled;
            }

            IReadOnlyList<PhysicsContactSnapshot> contacts = snapshot.Contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                PhysicsContactSnapshot contact = contacts[i];
                _occupancyContacts[new ContactKey(contact.BodyAId, contact.BodyBId)] = contact;
            }
        }

        private PhysicsBodySnapshot CaptureBodySnapshot(int bodyId, Body body)
        {
            Vector2 position = body.GetPosition();
            Vector2 linearVelocity = body.LinearVelocity;
            return new PhysicsBodySnapshot(
                bodyId,
                position.X,
                position.Y,
                body.GetAngle(),
                linearVelocity.X,
                linearVelocity.Y,
                body.AngularVelocity,
                body.IsAwake,
                body.IsEnabled);
        }

        private PhysicsContactSnapshot[] BuildMergedContacts()
        {
            PhysicsContactSnapshot[] contacts = new PhysicsContactSnapshot[_physicsContacts.Count + _occupancyContacts.Count];
            int index = 0;
            foreach (PhysicsContactSnapshot contact in _physicsContacts.Values)
            {
                contacts[index++] = contact;
            }

            foreach (KeyValuePair<ContactKey, PhysicsContactSnapshot> pair in _occupancyContacts)
            {
                if (_physicsContacts.ContainsKey(pair.Key))
                {
                    continue;
                }

                contacts[index++] = pair.Value;
            }

            if (index == contacts.Length)
            {
                return contacts;
            }

            Array.Resize(ref contacts, index);
            return contacts;
        }

        private void RemoveContactsForBody(int bodyId)
        {
            RemoveContactsForBody(bodyId, _physicsContacts);
            RemoveContactsForBody(bodyId, _occupancyContacts);
        }

        private void ResolvePlayerOccupancy(IReadOnlyDictionary<int, Vector2> requestedVelocities)
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
                    Body bodyA = _bodies[bodyAId];
                    Vector2 positionA = bodyA.GetPosition();

                    for (int j = i + 1; j < _sortedBodyIdsBuffer.Count; j++)
                    {
                        int bodyBId = _sortedBodyIdsBuffer[j];
                        Body bodyB = _bodies[bodyBId];
                        Vector2 positionB = bodyB.GetPosition();
                        Vector2 delta = positionB - positionA;
                        float distanceSquared = delta.LengthSquared();
                        float distance = distanceSquared > OccupancyEpsilon
                            ? MathF.Sqrt(distanceSquared)
                            : 0.0f;
                        float penetration = MinimumPlayerSeparation - distance;
                        if (penetration <= OccupancyEpsilon)
                        {
                            continue;
                        }

                        Vector2 normal = DetermineSeparationNormal(
                            bodyAId,
                            bodyBId,
                            delta,
                            distanceSquared,
                            requestedVelocities);
                        CalculateSeparationShares(
                            bodyAId,
                            bodyBId,
                            normal,
                            penetration,
                            requestedVelocities,
                            out float moveBodyA,
                            out float moveBodyB);

                        if (moveBodyA > 0.0f)
                        {
                            Vector2 correctedPositionA = positionA - (normal * moveBodyA);
                            bodyA.SetTransform(in correctedPositionA, bodyA.GetAngle());
                            positionA = correctedPositionA;
                        }

                        if (moveBodyB > 0.0f)
                        {
                            Vector2 correctedPositionB = positionB + (normal * moveBodyB);
                            bodyB.SetTransform(in correctedPositionB, bodyB.GetAngle());
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

            float contactDistance = MinimumPlayerSeparation + ContactTolerance;
            float contactDistanceSquared = contactDistance * contactDistance;
            for (int i = 0; i < _sortedBodyIdsBuffer.Count - 1; i++)
            {
                int bodyAId = _sortedBodyIdsBuffer[i];
                Vector2 positionA = _bodies[bodyAId].GetPosition();

                for (int j = i + 1; j < _sortedBodyIdsBuffer.Count; j++)
                {
                    int bodyBId = _sortedBodyIdsBuffer[j];
                    Vector2 positionB = _bodies[bodyBId].GetPosition();
                    Vector2 delta = positionB - positionA;
                    if (delta.LengthSquared() > contactDistanceSquared)
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

        private static Vector2 DetermineSeparationNormal(
            int bodyAId,
            int bodyBId,
            Vector2 delta,
            float distanceSquared,
            IReadOnlyDictionary<int, Vector2> requestedVelocities)
        {
            if (distanceSquared > OccupancyEpsilon)
            {
                float inverseDistance = 1.0f / MathF.Sqrt(distanceSquared);
                return delta * inverseDistance;
            }

            Vector2 relativeRequestedVelocity =
                GetRequestedVelocity(bodyAId, requestedVelocities) - GetRequestedVelocity(bodyBId, requestedVelocities);
            float relativeVelocitySquared = relativeRequestedVelocity.LengthSquared();
            if (relativeVelocitySquared > OccupancyEpsilon)
            {
                float inverseLength = 1.0f / MathF.Sqrt(relativeVelocitySquared);
                return relativeRequestedVelocity * inverseLength;
            }

            return bodyAId <= bodyBId ? Vector2.UnitX : -Vector2.UnitX;
        }

        private static void CalculateSeparationShares(
            int bodyAId,
            int bodyBId,
            Vector2 normal,
            float penetration,
            IReadOnlyDictionary<int, Vector2> requestedVelocities,
            out float moveBodyA,
            out float moveBodyB)
        {
            float bodyATowardIntent = GetTowardIntent(bodyAId, normal, requestedVelocities);
            float bodyBTowardIntent = GetTowardIntent(bodyBId, -normal, requestedVelocities);
            bool bodyAPushing = bodyATowardIntent > MovementIntentEpsilon;
            bool bodyBPushing = bodyBTowardIntent > MovementIntentEpsilon;

            if (bodyAPushing && bodyBPushing)
            {
                float totalIntent = bodyATowardIntent + bodyBTowardIntent;
                if (totalIntent <= MovementIntentEpsilon)
                {
                    moveBodyA = penetration * 0.5f;
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
                moveBodyB = 0.0f;
                return;
            }

            if (bodyBPushing)
            {
                moveBodyA = 0.0f;
                moveBodyB = penetration;
                return;
            }

            moveBodyA = penetration * 0.5f;
            moveBodyB = penetration - moveBodyA;
        }

        private static float GetTowardIntent(
            int bodyId,
            Vector2 normal,
            IReadOnlyDictionary<int, Vector2> requestedVelocities)
        {
            Vector2 requestedVelocity = GetRequestedVelocity(bodyId, requestedVelocities);
            float towardIntent = Vector2.Dot(requestedVelocity, normal);
            return towardIntent > 0.0f ? towardIntent : 0.0f;
        }

        private static Vector2 GetRequestedVelocity(
            int bodyId,
            IReadOnlyDictionary<int, Vector2> requestedVelocities)
        {
            return requestedVelocities.TryGetValue(bodyId, out Vector2 requestedVelocity)
                ? requestedVelocity
                : Vector2.Zero;
        }

        private static void RemoveContactsForBody(
            int bodyId,
            Dictionary<ContactKey, PhysicsContactSnapshot> contacts)
        {
            List<ContactKey> keysToRemove = new List<ContactKey>();
            foreach (ContactKey key in contacts.Keys)
            {
                if (key.BodyAId == bodyId || key.BodyBId == bodyId)
                {
                    keysToRemove.Add(key);
                }
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                contacts.Remove(keysToRemove[i]);
            }
        }

        private static void NormalizeInput(ref float dx, ref float dy)
        {
            float lenSq = dx * dx + dy * dy;
            if (lenSq <= 1.0f)
            {
                return;
            }

            float invLen = 1.0f / MathF.Sqrt(lenSq);
            dx *= invLen;
            dy *= invLen;
        }

        private static void SetBodyLinearVelocity(Body body, Vector2 linearVelocity)
        {
            BodyLinearVelocityProperty?.SetValue(body, linearVelocity);
        }

        private static void SetBodyAngularVelocity(Body body, float angularVelocity)
        {
            BodyAngularVelocityProperty?.SetValue(body, angularVelocity);
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

            public override bool Equals(object obj)
            {
                return obj is ContactKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(BodyAId, BodyBId);
            }
        }

        private sealed class ContactListener : IContactListener
        {
            private readonly Dictionary<ContactKey, PhysicsContactSnapshot> _contacts;

            public ContactListener(Dictionary<ContactKey, PhysicsContactSnapshot> contacts)
            {
                _contacts = contacts;
            }

            public void BeginContact(Contact contact)
            {
                Update(contact, true);
            }

            public void EndContact(Contact contact)
            {
                Update(contact, false);
            }

            public void PreSolve(Contact contact, in Manifold oldManifold)
            {
                Update(contact, contact.IsTouching);
            }

            public void PostSolve(Contact contact, in ContactImpulse impulse)
            {
                Update(contact, contact.IsTouching);
            }

            private void Update(Contact contact, bool isTouching)
            {
                if (contact == null || contact.FixtureA == null || contact.FixtureB == null)
                {
                    return;
                }

                if (contact.FixtureA.Body?.UserData is not int bodyAId || contact.FixtureB.Body?.UserData is not int bodyBId)
                {
                    return;
                }

                ContactKey key = new ContactKey(bodyAId, bodyBId);
                if (!isTouching)
                {
                    _contacts.Remove(key);
                    return;
                }

                _contacts[key] = new PhysicsContactSnapshot(key.BodyAId, key.BodyBId, true);
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
