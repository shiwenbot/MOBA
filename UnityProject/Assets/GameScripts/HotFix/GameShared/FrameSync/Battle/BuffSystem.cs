using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public static class BuffSystem
    {
        public static long AddBuff(PlayerState target, ApplyBuffCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return AddBuff(target, command, null);
        }

        public static long AddBuff(PlayerState target, ApplyBuffCommand command, IBuffConfigProvider configProvider)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            ResolvedBuffRule rule = ResolveRule(command.BuffId, command.DurationFrames, command.Flags, configProvider);
            if (!TryRemoveMutexConflicts(target, command.BuffId, rule, configProvider))
            {
                return 0;
            }

            return rule.OverlayType switch
            {
                BuffOverlayType.Stack => AddOrStackBuff(target, command, rule),
                BuffOverlayType.Refresh => AddOrRefreshBuff(target, command, rule),
                _ => AddIndependentBuff(target, command, rule)
            };
        }

        public static long AddBuff(
            PlayerState target,
            long casterId,
            int buffId,
            int durationFrames,
            int stackCount,
            uint appliedFrame,
            BuffFlags flags)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            long runtimeBuffId = target.AllocateRuntimeBuffId();
            BuffState buffState = new BuffState(
                runtimeBuffId,
                buffId,
                casterId,
                target.PlayerId,
                stackCount,
                NormalizeRemainingFrames(durationFrames, flags),
                appliedFrame,
                flags);
            InsertSorted(target.ActiveBuffs, buffState);
            return runtimeBuffId;
        }

        public static int ApplyTick(PlayerState target, uint frameIndex)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.ActiveBuffs.Count == 0)
            {
                return 0;
            }

            List<long> expiredRuntimeIds = null;
            for (int i = 0; i < target.ActiveBuffs.Count; i++)
            {
                BuffState buffState = target.ActiveBuffs[i];
                if (!buffState.HasDuration || buffState.IsPassive || buffState.RemainingFrames <= 0)
                {
                    continue;
                }

                BuffState updated = buffState.WithRemainingFrames(buffState.RemainingFrames - 1);
                if (updated.RemainingFrames <= 0)
                {
                    expiredRuntimeIds ??= new List<long>();
                    expiredRuntimeIds.Add(updated.RuntimeBuffId);
                }

                target.ActiveBuffs[i] = updated;
            }

            if (expiredRuntimeIds == null)
            {
                return 0;
            }

            int removed = 0;
            for (int i = 0; i < expiredRuntimeIds.Count; i++)
            {
                removed += RemoveBuff(target, expiredRuntimeIds[i], 0);
            }

            return removed;
        }

        public static int RemoveBuff(PlayerState target, RemoveBuffCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return RemoveBuff(target, command.RuntimeBuffId, command.BuffId);
        }

        public static int RemoveBuff(PlayerState target, long runtimeBuffId, int buffId)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            int removed = 0;
            for (int i = target.ActiveBuffs.Count - 1; i >= 0; i--)
            {
                BuffState buffState = target.ActiveBuffs[i];
                bool matchByRuntime = runtimeBuffId > 0 && buffState.RuntimeBuffId == runtimeBuffId;
                bool matchByBuffId = runtimeBuffId <= 0 && buffId > 0 && buffState.BuffId == buffId;
                if (!matchByRuntime && !matchByBuffId)
                {
                    continue;
                }

                RemoveBuffInternal(target, i);
                removed++;
            }

            if (removed > 0)
            {
                target.Numeric.Recalculate(target);
            }

            return removed;
        }

        public static bool HasBuff(PlayerState target, int buffId)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return FindFirst(target.ActiveBuffs, buffId) >= 0;
        }

        public static int GetBuffStackCount(PlayerState target, int buffId)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            int stackCount = 0;
            for (int i = 0; i < target.ActiveBuffs.Count; i++)
            {
                BuffState buffState = target.ActiveBuffs[i];
                if (buffState.BuffId == buffId)
                {
                    stackCount += buffState.StackCount;
                }
            }

            return stackCount;
        }

        private static long AddIndependentBuff(PlayerState target, ApplyBuffCommand command, ResolvedBuffRule rule)
        {
            long runtimeBuffId = target.AllocateRuntimeBuffId();
            int stackCount = NormalizeInitialStackCount(command.StackCount, rule.MaxStack);
            BuffState buffState = new BuffState(
                runtimeBuffId,
                command.BuffId,
                command.CasterId,
                target.PlayerId,
                stackCount,
                NormalizeRemainingFrames(rule.DurationFrames, rule.Flags),
                command.FrameIndex,
                rule.Flags);
            InsertSorted(target.ActiveBuffs, buffState);
            ApplyBuffEffects(target, runtimeBuffId, rule, stackCount);
            target.Numeric.Recalculate(target);
            return runtimeBuffId;
        }

        private static long AddOrStackBuff(PlayerState target, ApplyBuffCommand command, ResolvedBuffRule rule)
        {
            int existingIndex = FindBuffByBuffId(target.ActiveBuffs, command.BuffId);
            if (existingIndex < 0)
            {
                return AddIndependentBuff(target, command, rule);
            }

            BuffState existing = target.ActiveBuffs[existingIndex];
            int increment = NormalizeStackIncrement(command.StackCount);
            int nextStackCount = existing.StackCount >= rule.MaxStack
                ? existing.StackCount
                : ClampStackCount((long)existing.StackCount + increment, rule.MaxStack);
            BuffState updated = existing.WithRemainingFrames(NormalizeRemainingFrames(rule.DurationFrames, existing.Flags));
            if (updated.StackCount != nextStackCount)
            {
                updated = updated.WithStackCount(nextStackCount);
                target.Numeric.RemoveBySource(existing.RuntimeBuffId);
                ApplyBuffEffects(target, existing.RuntimeBuffId, rule, nextStackCount);
            }

            target.ActiveBuffs[existingIndex] = updated;
            target.Numeric.Recalculate(target);
            return existing.RuntimeBuffId;
        }

        private static long AddOrRefreshBuff(PlayerState target, ApplyBuffCommand command, ResolvedBuffRule rule)
        {
            int existingIndex = FindBuffByBuffId(target.ActiveBuffs, command.BuffId);
            if (existingIndex < 0)
            {
                return AddIndependentBuff(target, command, rule);
            }

            BuffState existing = target.ActiveBuffs[existingIndex];
            target.ActiveBuffs[existingIndex] =
                existing.WithRemainingFrames(NormalizeRemainingFrames(rule.DurationFrames, existing.Flags));
            target.Numeric.Recalculate(target);
            return existing.RuntimeBuffId;
        }

        private static bool TryRemoveMutexConflicts(
            PlayerState target,
            int incomingBuffId,
            ResolvedBuffRule incomingRule,
            IBuffConfigProvider configProvider)
        {
            if (incomingRule.MutexGroupId == 0)
            {
                return true;
            }

            List<int> mutexIndices = FindBuffsByMutexGroup(
                target.ActiveBuffs,
                incomingRule.MutexGroupId,
                incomingBuffId,
                incomingRule.OverlayType,
                configProvider);
            for (int i = 0; i < mutexIndices.Count; i++)
            {
                BuffState existing = target.ActiveBuffs[mutexIndices[i]];
                ResolvedBuffRule existingRule = ResolveRule(existing.BuffId, existing.RemainingFrames, existing.Flags, configProvider);
                if (existingRule.Priority > incomingRule.Priority)
                {
                    return false;
                }
            }

            for (int i = mutexIndices.Count - 1; i >= 0; i--)
            {
                RemoveBuffInternal(target, mutexIndices[i]);
            }

            return true;
        }

        private static void ApplyBuffEffects(PlayerState target, long runtimeBuffId, ResolvedBuffRule rule, int stackCount)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (rule.Effects == null || rule.Effects.Length == 0)
            {
                return;
            }

            for (int i = 0; i < rule.Effects.Length; i++)
            {
                BuffEffect effect = rule.Effects[i];
                target.Numeric.AddModifier(new NumericModifier(
                    runtimeBuffId,
                    effect.ValueType,
                    effect.AttributeKind,
                    MultiplyEffectValue(effect.Value, stackCount)));
            }
        }

        private static void InsertSorted(List<BuffState> activeBuffs, BuffState buffState)
        {
            int insertIndex = activeBuffs.Count;
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                if (Compare(activeBuffs[i], buffState) > 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            activeBuffs.Insert(insertIndex, buffState);
        }

        private static int FindBuffByBuffId(List<BuffState> activeBuffs, int buffId)
        {
            return FindFirst(activeBuffs, buffId);
        }

        private static List<int> FindBuffsByMutexGroup(
            List<BuffState> activeBuffs,
            int mutexGroupId,
            int incomingBuffId,
            BuffOverlayType incomingOverlayType,
            IBuffConfigProvider configProvider)
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                BuffState buffState = activeBuffs[i];
                if ((incomingOverlayType == BuffOverlayType.Stack || incomingOverlayType == BuffOverlayType.Refresh) &&
                    buffState.BuffId == incomingBuffId)
                {
                    continue;
                }

                ResolvedBuffRule existingRule = ResolveRule(buffState.BuffId, buffState.RemainingFrames, buffState.Flags, configProvider);
                if (existingRule.MutexGroupId == mutexGroupId)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }

        private static int FindFirst(List<BuffState> activeBuffs, int buffId)
        {
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                if (activeBuffs[i].BuffId == buffId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int Compare(BuffState left, BuffState right)
        {
            int byFrame = left.AppliedFrame.CompareTo(right.AppliedFrame);
            if (byFrame != 0)
            {
                return byFrame;
            }

            return left.RuntimeBuffId.CompareTo(right.RuntimeBuffId);
        }

        private static ResolvedBuffRule ResolveRule(
            int buffId,
            int durationFrames,
            BuffFlags flags,
            IBuffConfigProvider configProvider)
        {
            if (configProvider != null &&
                configProvider.TryGetBuffConfig(buffId, out BuffConfig config))
            {
                return new ResolvedBuffRule(
                    config.OverlayType,
                    NormalizeConfiguredMaxStack(config.MaxStack, config.OverlayType),
                    Math.Max(0, config.MutexGroupId),
                    config.Priority,
                    ResolveDurationFrames(durationFrames, config.DurationFrames),
                    ResolveFlags(flags, config.DefaultFlags),
                    config.Effects ?? Array.Empty<BuffEffect>());
            }

            BuffOverlayType bridgeOverlayType = (flags & BuffFlags.Stackable) != 0
                ? BuffOverlayType.Stack
                : BuffOverlayType.Independent;
            int bridgeMaxStack = bridgeOverlayType == BuffOverlayType.Stack
                ? int.MaxValue
                : 1;
            return new ResolvedBuffRule(
                bridgeOverlayType,
                bridgeMaxStack,
                0,
                0,
                durationFrames,
                flags,
                Array.Empty<BuffEffect>());
        }

        private static int ResolveDurationFrames(int commandDurationFrames, int configuredDurationFrames)
        {
            return commandDurationFrames != 0
                ? commandDurationFrames
                : configuredDurationFrames;
        }

        private static BuffFlags ResolveFlags(BuffFlags commandFlags, BuffFlags configuredFlags)
        {
            return commandFlags | configuredFlags;
        }

        private static int NormalizeConfiguredMaxStack(int maxStack, BuffOverlayType overlayType)
        {
            return overlayType == BuffOverlayType.Stack
                ? Math.Max(1, maxStack)
                : 1;
        }

        private static int NormalizeInitialStackCount(int requestedStackCount, int maxStack)
        {
            return ClampStackCount(NormalizeStackIncrement(requestedStackCount), maxStack);
        }

        private static int NormalizeStackIncrement(int requestedStackCount)
        {
            return requestedStackCount < 1 ? 1 : requestedStackCount;
        }

        private static int ClampStackCount(long stackCount, int maxStack)
        {
            long upperBound = maxStack < 1 ? 1L : maxStack;
            long normalized = Math.Clamp(stackCount, 1L, upperBound);
            return (int)normalized;
        }

        private static int MultiplyEffectValue(int value, int stackCount)
        {
            long multiplied = (long)value * Math.Max(1, stackCount);
            return multiplied > int.MaxValue
                ? int.MaxValue
                : multiplied < int.MinValue
                    ? int.MinValue
                    : (int)multiplied;
        }

        private static void RemoveBuffInternal(PlayerState target, int index)
        {
            BuffState buffState = target.ActiveBuffs[index];
            target.Numeric.RemoveBySource(buffState.RuntimeBuffId);
            target.ActiveBuffs.RemoveAt(index);
        }

        private static int NormalizeRemainingFrames(int durationFrames, BuffFlags flags)
        {
            if ((flags & BuffFlags.Passive) != 0)
            {
                return -1;
            }

            return durationFrames < 0 ? 0 : durationFrames;
        }

        private readonly struct ResolvedBuffRule
        {
            public ResolvedBuffRule(
                BuffOverlayType overlayType,
                int maxStack,
                int mutexGroupId,
                int priority,
                int durationFrames,
                BuffFlags flags,
                BuffEffect[] effects)
            {
                OverlayType = overlayType;
                MaxStack = maxStack;
                MutexGroupId = mutexGroupId;
                Priority = priority;
                DurationFrames = durationFrames;
                Flags = flags;
                Effects = effects;
            }

            public BuffOverlayType OverlayType { get; }
            public int MaxStack { get; }
            public int MutexGroupId { get; }
            public int Priority { get; }
            public int DurationFrames { get; }
            public BuffFlags Flags { get; }
            public BuffEffect[] Effects { get; }
        }
    }
}
