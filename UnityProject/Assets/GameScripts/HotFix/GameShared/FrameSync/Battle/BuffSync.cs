using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public static class BuffSync
    {
        public const uint HasAnyChangeMask = 1u;

        public static BuffChange[] ComputeChanges(IReadOnlyList<BuffState> baselineBuffs, IReadOnlyList<BuffState> currentBuffs)
        {
            if ((baselineBuffs == null || baselineBuffs.Count == 0) &&
                (currentBuffs == null || currentBuffs.Count == 0))
            {
                return Array.Empty<BuffChange>();
            }

            Dictionary<long, BuffState> baselineByRuntimeId = BuildLookup(baselineBuffs);
            Dictionary<long, BuffState> currentByRuntimeId = BuildLookup(currentBuffs);
            List<BuffChange> changes = new List<BuffChange>();

            foreach (KeyValuePair<long, BuffState> pair in currentByRuntimeId)
            {
                if (!baselineByRuntimeId.TryGetValue(pair.Key, out BuffState baseline))
                {
                    changes.Add(new BuffChange(pair.Value, BuffDirtyFlags.Added));
                    continue;
                }

                if (!AreEquivalent(baseline, pair.Value))
                {
                    changes.Add(new BuffChange(pair.Value, BuffDirtyFlags.Updated));
                }
            }

            foreach (KeyValuePair<long, BuffState> pair in baselineByRuntimeId)
            {
                if (!currentByRuntimeId.ContainsKey(pair.Key))
                {
                    changes.Add(new BuffChange(pair.Value, BuffDirtyFlags.Removed));
                }
            }

            if (changes.Count == 0)
            {
                return Array.Empty<BuffChange>();
            }

            changes.Sort(CompareChanges);
            return changes.ToArray();
        }

        public static uint ComputeDirtyMask(IReadOnlyList<BuffState> baselineBuffs, IReadOnlyList<BuffState> currentBuffs)
        {
            return ComputeChanges(baselineBuffs, currentBuffs).Length > 0
                ? HasAnyChangeMask
                : 0u;
        }

        public static BuffState[] Merge(IReadOnlyList<BuffState> baselineBuffs, IReadOnlyList<BuffChange> changes)
        {
            if (changes == null || changes.Count == 0)
            {
                return CopyBuffs(baselineBuffs);
            }

            Dictionary<long, BuffState> mergedByRuntimeId = BuildLookup(baselineBuffs);
            for (int i = 0; i < changes.Count; i++)
            {
                BuffChange change = changes[i];
                switch (change.DirtyFlags)
                {
                    case BuffDirtyFlags.Added:
                    case BuffDirtyFlags.Updated:
                        mergedByRuntimeId[change.State.RuntimeBuffId] = change.State;
                        break;
                    case BuffDirtyFlags.Removed:
                        mergedByRuntimeId.Remove(change.State.RuntimeBuffId);
                        break;
                }
            }

            BuffState[] merged = new BuffState[mergedByRuntimeId.Count];
            int index = 0;
            foreach (KeyValuePair<long, BuffState> pair in mergedByRuntimeId)
            {
                merged[index++] = pair.Value;
            }

            Array.Sort(merged, CompareBuffStates);
            return merged;
        }

        public static BuffState[] CopyBuffs(IReadOnlyList<BuffState> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return Array.Empty<BuffState>();
            }

            BuffState[] copy = new BuffState[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                copy[i] = buffs[i];
            }

            return copy;
        }

        public static bool AreEquivalent(BuffState left, BuffState right)
        {
            return left.RuntimeBuffId == right.RuntimeBuffId &&
                   left.BuffId == right.BuffId &&
                   left.CasterId == right.CasterId &&
                   left.TargetId == right.TargetId &&
                   left.StackCount == right.StackCount &&
                   left.RemainingFrames == right.RemainingFrames &&
                   left.AppliedFrame == right.AppliedFrame &&
                   left.Flags == right.Flags;
        }

        private static Dictionary<long, BuffState> BuildLookup(IReadOnlyList<BuffState> buffs)
        {
            Dictionary<long, BuffState> lookup = new Dictionary<long, BuffState>();
            if (buffs == null)
            {
                return lookup;
            }

            for (int i = 0; i < buffs.Count; i++)
            {
                BuffState state = buffs[i];
                lookup[state.RuntimeBuffId] = state;
            }

            return lookup;
        }

        private static int CompareChanges(BuffChange left, BuffChange right)
        {
            return CompareBuffStates(left.State, right.State);
        }

        private static int CompareBuffStates(BuffState left, BuffState right)
        {
            int byFrame = left.AppliedFrame.CompareTo(right.AppliedFrame);
            if (byFrame != 0)
            {
                return byFrame;
            }

            return left.RuntimeBuffId.CompareTo(right.RuntimeBuffId);
        }

        public readonly struct BuffChange
        {
            public BuffChange(BuffState state, BuffDirtyFlags dirtyFlags)
            {
                State = state;
                DirtyFlags = dirtyFlags;
            }

            public BuffState State { get; }
            public BuffDirtyFlags DirtyFlags { get; }
        }
    }
}
