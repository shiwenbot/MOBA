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

            return AddBuff(
                target,
                command.CasterId,
                command.BuffId,
                command.DurationFrames,
                command.StackCount,
                command.FrameIndex,
                command.Flags);
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

                target.Numeric.RemoveBySource(buffState.RuntimeBuffId);
                target.ActiveBuffs.RemoveAt(i);
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

        private static int NormalizeRemainingFrames(int durationFrames, BuffFlags flags)
        {
            if ((flags & BuffFlags.Passive) != 0)
            {
                return -1;
            }

            return durationFrames < 0 ? 0 : durationFrames;
        }
    }
}
