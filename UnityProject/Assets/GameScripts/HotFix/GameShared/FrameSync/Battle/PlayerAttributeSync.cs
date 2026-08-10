using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public static class PlayerAttributeSync
    {
        public static PlayerAttributeDirtyFlags ComputeDirtyMask(
            bool hasPrevious,
            PlayerAttributeSnapshot previous,
            PlayerAttributeSnapshot current)
        {
            if (!hasPrevious)
            {
                return PlayerAttributeDirtyFlags.All;
            }

            PlayerAttributeDirtyFlags dirtyMask = PlayerAttributeDirtyFlags.None;
            if (previous.Health != current.Health)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Health;
            }

            if (previous.MaxHealth != current.MaxHealth)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.MaxHealth;
            }

            if (previous.Mana != current.Mana)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Mana;
            }

            if (previous.MaxMana != current.MaxMana)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.MaxMana;
            }

            if (previous.Attack != current.Attack)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Attack;
            }

            if (previous.Stamina != current.Stamina)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.Stamina;
            }

            if (previous.MaxStamina != current.MaxStamina)
            {
                dirtyMask |= PlayerAttributeDirtyFlags.MaxStamina;
            }

            return dirtyMask;
        }

        public static PlayerAttributeSnapshot Merge(
            PlayerAttributeSnapshot baseline,
            PlayerAttributeDirtyFlags dirtyMask,
            int health,
            int maxHealth,
            int mana,
            int maxMana,
            int attack,
            int stamina,
            int maxStamina)
        {
            return new PlayerAttributeSnapshot(
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Health) ? health : baseline.Health,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.MaxHealth) ? maxHealth : baseline.MaxHealth,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Mana) ? mana : baseline.Mana,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.MaxMana) ? maxMana : baseline.MaxMana,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Attack) ? attack : baseline.Attack,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.Stamina) ? stamina : baseline.Stamina,
                HasFlag(dirtyMask, PlayerAttributeDirtyFlags.MaxStamina) ? maxStamina : baseline.MaxStamina);
        }

        public static int SelectSerializedValue(
            PlayerAttributeDirtyFlags dirtyMask,
            PlayerAttributeDirtyFlags flag,
            int value)
        {
            return HasFlag(dirtyMask, flag) ? value : 0;
        }

        public static bool HasFlag(PlayerAttributeDirtyFlags dirtyMask, PlayerAttributeDirtyFlags flag)
        {
            return (dirtyMask & flag) != 0;
        }
    }

    /// <summary>
    /// Baselines are owned by an observer connection, not globally by the target player.
    /// A reconnect creates a new observer id and therefore starts without stale baselines.
    /// </summary>
    public sealed class ObserverTargetBaselineMap<TValue> where TValue : class
    {
        private readonly Dictionary<ObserverTargetKey, TValue> _values =
            new Dictionary<ObserverTargetKey, TValue>();
        private readonly List<ObserverTargetKey> _removeBuffer =
            new List<ObserverTargetKey>();

        public int Count => _values.Count;

        public bool TryGet(long observerId, long targetPlayerId, out TValue value)
        {
            return _values.TryGetValue(new ObserverTargetKey(observerId, targetPlayerId), out value);
        }

        public void Set(long observerId, long targetPlayerId, TValue value)
        {
            if (observerId == 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(observerId));
            }

            if (targetPlayerId == 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(targetPlayerId));
            }

            _values[new ObserverTargetKey(observerId, targetPlayerId)] =
                value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool Remove(long observerId, long targetPlayerId)
        {
            return _values.Remove(new ObserverTargetKey(observerId, targetPlayerId));
        }

        /// <summary>
        /// Zero is a wildcard. This supports connection cleanup, player cleanup, or both.
        /// </summary>
        public int RemoveMatching(long observerId, long targetPlayerId)
        {
            if (_values.Count == 0)
            {
                return 0;
            }

            _removeBuffer.Clear();
            foreach (KeyValuePair<ObserverTargetKey, TValue> pair in _values)
            {
                bool observerMatches = observerId == 0L || pair.Key.ObserverId == observerId;
                bool targetMatches = targetPlayerId == 0L || pair.Key.TargetPlayerId == targetPlayerId;
                if (observerMatches && targetMatches)
                {
                    _removeBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                _values.Remove(_removeBuffer[i]);
            }

            int removed = _removeBuffer.Count;
            _removeBuffer.Clear();
            return removed;
        }
    }

    public readonly struct ObserverTargetKey : IEquatable<ObserverTargetKey>
    {
        public ObserverTargetKey(long observerId, long targetPlayerId)
        {
            ObserverId = observerId;
            TargetPlayerId = targetPlayerId;
        }

        public long ObserverId { get; }
        public long TargetPlayerId { get; }

        public bool Equals(ObserverTargetKey other)
        {
            return ObserverId == other.ObserverId && TargetPlayerId == other.TargetPlayerId;
        }

        public override bool Equals(object obj)
        {
            return obj is ObserverTargetKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ObserverId.GetHashCode() * 397) ^ TargetPlayerId.GetHashCode();
            }
        }
    }
}
