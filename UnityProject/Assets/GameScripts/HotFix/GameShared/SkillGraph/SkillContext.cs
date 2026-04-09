using Fantasy.Async;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameShared.SkillGraph
{
    public interface ISkillRuntimeServices
    {
        void Log(string message);

        FTask<bool> DelayAsync(int milliseconds, FCancellationToken? cancellationToken = null);

        FTask<bool> PlayAnimationAsync(
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

        public int MaxExecutionSteps { get; set; }

        public SkillBlackboard Blackboard { get; set; } = new SkillBlackboard();

        public ISkillRuntimeServices? Runtime { get; set; }

        public FCancellationToken? CancellationToken { get; set; }

        public bool IsCancellationRequested =>
            IsCancelled || (CancellationToken != null && CancellationToken.IsCancel);

        public void MarkCancelled()
        {
            IsCancelled = true;
        }
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

        public SkillBlackboardSnapshot CaptureSnapshot()
        {
            return new SkillBlackboardSnapshot
            {
                Strings = new Dictionary<string, string>(_strings, StringComparer.Ordinal),
                Floats = new Dictionary<string, float>(_floats, StringComparer.Ordinal),
                Ints = new Dictionary<string, int>(_ints, StringComparer.Ordinal),
                Bools = new Dictionary<string, bool>(_bools, StringComparer.Ordinal)
            };
        }

        public void RestoreSnapshot(SkillBlackboardSnapshot snapshot)
        {
            _strings.Clear();
            _floats.Clear();
            _ints.Clear();
            _bools.Clear();

            if (snapshot == null)
                return;

            CopyInto(snapshot.Strings, _strings);
            CopyInto(snapshot.Floats, _floats);
            CopyInto(snapshot.Ints, _ints);
            CopyInto(snapshot.Bools, _bools);
        }

        private static void CopyInto<TValue>(
            IReadOnlyDictionary<string, TValue> source,
            IDictionary<string, TValue> destination)
        {
            if (source == null)
                return;

            foreach (KeyValuePair<string, TValue> pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                destination[pair.Key] = pair.Value;
            }
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Blackboard key cannot be empty.", nameof(key));
        }
    }

    public sealed class SkillBlackboardSnapshot
    {
        [JsonProperty("strings")]
        public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        [JsonProperty("floats")]
        public Dictionary<string, float> Floats { get; set; } = new Dictionary<string, float>(StringComparer.Ordinal);

        [JsonProperty("ints")]
        public Dictionary<string, int> Ints { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        [JsonProperty("bools")]
        public Dictionary<string, bool> Bools { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
    }
}
