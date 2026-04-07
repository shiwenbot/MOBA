using Fantasy.Async;
using System;
using System.Collections.Generic;

namespace GameShared.SkillGraph
{
    public interface ISkillRuntimeServices
    {
        void Log(string message);

        FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null);

        FTask PlayAnimationAsync(
            SkillContext context,
            string prefabLocation,
            float speed,
            FCancellationToken? cancellationToken = null);
    }

    public sealed class SkillContext
    {
        public long CasterId { get; set; }

        public long TargetId { get; set; }

        public int SkillId { get; set; }

        public bool IsCancelled { get; set; }

        public SkillBlackboard Blackboard { get; set; } = new SkillBlackboard();

        public ISkillRuntimeServices? Runtime { get; set; }

        public FCancellationToken? CancellationToken { get; set; }
    }

    public sealed class SkillBlackboard
    {
        private readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _floats = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _bools = new Dictionary<string, bool>(StringComparer.Ordinal);

        public bool Contains(string key) =>
            !string.IsNullOrWhiteSpace(key) &&
            (_strings.ContainsKey(key) || _floats.ContainsKey(key) || _ints.ContainsKey(key) || _bools.ContainsKey(key));

        public void SetString(string key, string value)
        {
            ValidateKey(key);
            Remove(key);
            _strings[key] = value ?? string.Empty;
        }

        public void SetFloat(string key, float value)
        {
            ValidateKey(key);
            Remove(key);
            _floats[key] = value;
        }

        public void SetInt(string key, int value)
        {
            ValidateKey(key);
            Remove(key);
            _ints[key] = value;
        }

        public void SetBool(string key, bool value)
        {
            ValidateKey(key);
            Remove(key);
            _bools[key] = value;
        }

        public bool TryGetString(string key, out string value) => _strings.TryGetValue(key ?? string.Empty, out value);

        public bool TryGetFloat(string key, out float value) => _floats.TryGetValue(key ?? string.Empty, out value);

        public bool TryGetInt(string key, out int value) => _ints.TryGetValue(key ?? string.Empty, out value);

        public bool TryGetBool(string key, out bool value) => _bools.TryGetValue(key ?? string.Empty, out value);

        public void Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _strings.Remove(key);
            _floats.Remove(key);
            _ints.Remove(key);
            _bools.Remove(key);
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Blackboard key cannot be empty.", nameof(key));
        }
    }
}
