using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public sealed class PhysicsWorldSnapshot
    {
        private static readonly IReadOnlyList<PhysicsBodySnapshot> EmptyBodies = Array.Empty<PhysicsBodySnapshot>();
        private static readonly IReadOnlyList<PhysicsContactSnapshot> EmptyContacts = Array.Empty<PhysicsContactSnapshot>();

        public PhysicsWorldSnapshot(
            IReadOnlyList<PhysicsBodySnapshot> bodies,
            IReadOnlyList<PhysicsContactSnapshot> contacts = null)
        {
            Bodies = bodies ?? EmptyBodies;
            Contacts = contacts ?? EmptyContacts;
        }

        public IReadOnlyList<PhysicsBodySnapshot> Bodies { get; }
        public IReadOnlyList<PhysicsContactSnapshot> Contacts { get; }
        public bool HasBodies => Bodies.Count > 0;
        public bool HasContacts => Contacts.Count > 0;
    }
}
