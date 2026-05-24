using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    public sealed class DefaultBuffConfigProvider : IBuffConfigProvider
    {
        public const int LifecycleBuffId = 9001;
        public const int StackTestBuffId = 9101;
        public const int RefreshTestBuffId = 9102;
        public const int MutexLowBuffId = 9201;
        public const int MutexHighBuffId = 9202;
        public const int MutexSamePriorityBuffId = 9203;
        public const int StackMaxCount = 3;
        public const int RefreshReapplyDelayFrames = 20;
        public const int MutexGroupId = 1;

        private static readonly BuffEffect[] NoEffects = Array.Empty<BuffEffect>();
        private static readonly BuffEffect[] StackEffects =
        {
            new BuffEffect(AttributeKind.Attack, ModifierValueType.Flat, 5)
        };
        private static readonly BuffEffect[] RefreshEffects =
        {
            new BuffEffect(AttributeKind.Attack, ModifierValueType.Flat, 7)
        };
        private static readonly BuffEffect[] MutexLowEffects =
        {
            new BuffEffect(AttributeKind.Attack, ModifierValueType.Flat, 2)
        };
        private static readonly BuffEffect[] MutexHighEffects =
        {
            new BuffEffect(AttributeKind.Attack, ModifierValueType.Flat, 6)
        };
        private static readonly BuffEffect[] MutexSamePriorityEffects =
        {
            new BuffEffect(AttributeKind.Attack, ModifierValueType.Flat, 8)
        };

        private readonly Dictionary<int, BuffConfig> _configs = new Dictionary<int, BuffConfig>();

        public DefaultBuffConfigProvider()
        {
            RegisterDefaults();
        }

        public bool TryGetBuffConfig(int buffId, out BuffConfig config)
        {
            return _configs.TryGetValue(buffId, out config);
        }

        private void RegisterDefaults()
        {
            Register(new BuffConfig(
                LifecycleBuffId,
                BuffOverlayType.Independent,
                1,
                0,
                0,
                45,
                BuffFlags.Duration | BuffFlags.Dispellable,
                NoEffects));
            Register(new BuffConfig(
                StackTestBuffId,
                BuffOverlayType.Stack,
                StackMaxCount,
                0,
                0,
                45,
                BuffFlags.Duration | BuffFlags.Dispellable,
                StackEffects));
            Register(new BuffConfig(
                RefreshTestBuffId,
                BuffOverlayType.Refresh,
                1,
                0,
                0,
                45,
                BuffFlags.Duration | BuffFlags.Dispellable,
                RefreshEffects));
            Register(new BuffConfig(
                MutexLowBuffId,
                BuffOverlayType.Independent,
                1,
                MutexGroupId,
                1,
                45,
                BuffFlags.Duration | BuffFlags.Dispellable,
                MutexLowEffects));
            Register(new BuffConfig(
                MutexHighBuffId,
                BuffOverlayType.Independent,
                1,
                MutexGroupId,
                5,
                45,
                BuffFlags.Duration | BuffFlags.Dispellable,
                MutexHighEffects));
            Register(new BuffConfig(
                MutexSamePriorityBuffId,
                BuffOverlayType.Independent,
                1,
                MutexGroupId,
                5,
                45,
                BuffFlags.Duration | BuffFlags.Dispellable,
                MutexSamePriorityEffects));
        }

        private void Register(BuffConfig config)
        {
            _configs[config.BuffId] = config;
        }
    }
}
