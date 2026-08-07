using System;
using System.Collections.Generic;
using FixedMathSharp;

namespace GameShared.SkillGraph
{
    public sealed class ActiveSkillExecutionSnapshot
    {
        public ActiveSkillExecutionSnapshot(
            long casterId,
            long targetId,
            int skillId,
            SkillExecutionSnapshot runnerSnapshot,
            Fixed64 directionX = default,
            Fixed64 directionY = default)
        {
            CasterId = casterId;
            TargetId = targetId;
            SkillId = skillId;
            RunnerSnapshot = SkillExecutionSnapshotCodec.Clone(runnerSnapshot);
            DirectionX = directionX;
            DirectionY = directionY;
        }

        public long CasterId { get; }
        public long TargetId { get; }
        public int SkillId { get; }
        public SkillExecutionSnapshot RunnerSnapshot { get; }
        public Fixed64 DirectionX { get; }
        public Fixed64 DirectionY { get; }
    }

    public static class SkillExecutionSnapshotCodec
    {
        public static SkillExecutionSnapshot Clone(SkillExecutionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            SkillBlackboardSnapshot blackboard = snapshot.Blackboard ?? new SkillBlackboardSnapshot();
            return new SkillExecutionSnapshot
            {
                CurrentNodeId = snapshot.CurrentNodeId,
                Status = snapshot.Status,
                ExecutedSteps = snapshot.ExecutedSteps,
                FrameIndex = snapshot.FrameIndex,
                Message = snapshot.Message ?? string.Empty,
                Blackboard = new SkillBlackboardSnapshot
                {
                    Strings = Copy(blackboard.Strings, StringComparer.Ordinal),
                    Floats = Copy(blackboard.Floats, StringComparer.Ordinal),
                    Ints = Copy(blackboard.Ints, StringComparer.Ordinal),
                    Bools = Copy(blackboard.Bools, StringComparer.Ordinal)
                },
                DelayRemainingFrames = Copy(snapshot.DelayRemainingFrames, null)
            };
        }

        public static Dictionary<long, ActiveSkillExecutionSnapshot> CloneExecutions(
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> executions)
        {
            Dictionary<long, ActiveSkillExecutionSnapshot> copy =
                new Dictionary<long, ActiveSkillExecutionSnapshot>();
            if (executions == null || executions.Count == 0)
            {
                return copy;
            }

            List<long> casterIds = new List<long>(executions.Keys);
            casterIds.Sort();
            for (int i = 0; i < casterIds.Count; i++)
            {
                long casterId = casterIds[i];
                ActiveSkillExecutionSnapshot execution = executions[casterId];
                copy[casterId] = new ActiveSkillExecutionSnapshot(
                    execution.CasterId,
                    execution.TargetId,
                    execution.SkillId,
                    execution.RunnerSnapshot,
                    execution.DirectionX,
                    execution.DirectionY);
            }

            return copy;
        }

        private static Dictionary<TKey, TValue> Copy<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> source,
            IEqualityComparer<TKey> comparer)
        {
            Dictionary<TKey, TValue> copy = comparer == null
                ? new Dictionary<TKey, TValue>()
                : new Dictionary<TKey, TValue>(comparer);
            if (source == null)
            {
                return copy;
            }

            foreach (KeyValuePair<TKey, TValue> pair in source)
            {
                copy[pair.Key] = pair.Value;
            }

            return copy;
        }
    }
}
