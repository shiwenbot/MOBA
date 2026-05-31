using System;
using System.Collections.Generic;
using GameShared.Badminton.Config;
using GameShared.FrameSync.Command;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;
using UnityEngine;

namespace GameShared.Badminton
{
    public sealed class ShuttlecockEntity : ITickable, ISnapshotable<ShuttlecockSnapshot>
    {
        private const int DefaultSnapshotCapacity = 128;
        private const float DegreesToRadians = Mathf.Deg2Rad;
        private const float PositiveAngleVerticalSpeedScale = 0.4f;

        private readonly IShuttlecockShotConfigProvider _configProvider;
        private readonly SnapshotBuffer<ShuttlecockSnapshot> _snapshotBuffer;
        private readonly CommandPool<ShuttlecockLaunchCommand> _commandPool = new CommandPool<ShuttlecockLaunchCommand>();
        private readonly List<ShuttlecockLaunchCommand> _pendingCommands = new List<ShuttlecockLaunchCommand>();
        private readonly Dictionary<uint, ulong> _expectedHashes = new Dictionary<uint, ulong>();
        private uint _latestSnapshotFrame;

        public ShuttlecockEntity(
            IShuttlecockShotConfigProvider configProvider,
            int priority = 0,
            int snapshotCapacity = DefaultSnapshotCapacity)
        {
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _snapshotBuffer = new SnapshotBuffer<ShuttlecockSnapshot>(snapshotCapacity);
            State = new ShuttlecockState();
            State.Reset();
            Priority = priority;
            SaveSnapshot(0);
        }

        public int Priority { get; }
        public ShuttlecockState State { get; }
        public int PendingCommandCount => _pendingCommands.Count;

        public void EnqueueLaunch(ShuttlecockLaunchCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            ValidateDirection(command.DirectionXZ);
            if (!_configProvider.TryGet(command.ShotType, out _))
            {
                throw new InvalidOperationException($"Missing shuttlecock config for shot type: {command.ShotType}.");
            }

            ShuttlecockLaunchCommand queuedCommand = _commandPool.Rent();
            queuedCommand.TargetFrame = command.TargetFrame;
            queuedCommand.ShotType = command.ShotType;
            queuedCommand.OriginXZ = command.OriginXZ;
            queuedCommand.OriginY = command.OriginY;
            queuedCommand.DirectionXZ = command.DirectionXZ.normalized;
            InsertPendingCommand(queuedCommand);
        }

        public void Tick(uint frameIndex, float fixedDt)
        {
            DeterminismRules.AssertFixedDt(fixedDt);
            ConsumePendingCommands(frameIndex);
            if (State.Phase == ShuttlecockFlightPhase.Flying)
            {
                ShuttlecockPhysics.Step(State, fixedDt, frameIndex);
            }

            SaveSnapshot(frameIndex);
        }

        public void RollBack(uint targetFrame)
        {
            if (!_snapshotBuffer.TryGet(targetFrame, out ShuttlecockSnapshot snapshot))
            {
                throw new InvalidOperationException($"Missing shuttlecock snapshot for frame={targetFrame}.");
            }

            RestoreSnapshot(snapshot);
            RemovePendingCommandsAfter(targetFrame);
            RemoveExpectedHashesAfter(targetFrame);
        }

        public void CheckConsistency(uint frameIndex)
        {
            if (!_expectedHashes.TryGetValue(frameIndex, out ulong expectedHash))
            {
                return;
            }

            ulong actualHash = ShuttlecockStateHasher.Hash(TakeSnapshot().WithFrameIndex(frameIndex));
            if (actualHash != expectedHash)
            {
                throw new InvalidOperationException(
                    $"Shuttlecock consistency mismatch. frame={frameIndex}, expected={expectedHash}, actual={actualHash}.");
            }

            _expectedHashes.Remove(frameIndex);
        }

        public void SetExpectedHash(uint frameIndex, ulong expectedHash)
        {
            _expectedHashes[frameIndex] = expectedHash;
        }

        public bool TryGetSnapshot(uint frameIndex, out ShuttlecockSnapshot snapshot)
        {
            return _snapshotBuffer.TryGet(frameIndex, out snapshot);
        }

        public ShuttlecockSnapshot TakeSnapshot()
        {
            return new ShuttlecockSnapshot(
                _latestSnapshotFrame,
                State.XZ,
                State.Y,
                State.Vxz,
                State.Vy,
                State.Phase,
                State.LastValidFlyingFrame,
                State.LandingXZ,
                State.IsInBounds,
                State.ActiveShotType,
                State.HorizontalDrag);
        }

        public void RestoreSnapshot(ShuttlecockSnapshot snapshot)
        {
            State.XZ = snapshot.XZ;
            State.Y = snapshot.Y;
            State.Vxz = snapshot.Vxz;
            State.Vy = snapshot.Vy;
            State.Phase = snapshot.Phase;
            State.LastValidFlyingFrame = snapshot.LastValidFlyingFrame;
            State.LandingXZ = snapshot.LandingXZ;
            State.IsInBounds = snapshot.IsInBounds;
            State.ActiveShotType = snapshot.ActiveShotType;
            State.HorizontalDrag = snapshot.HorizontalDrag;
            _latestSnapshotFrame = snapshot.FrameIndex;
        }

        public static void DeterminismSelfTest(
            ShuttlecockLaunchCommand templateCommand,
            IShuttlecockShotConfigProvider configProvider,
            uint totalFrames = 90)
        {
            if (templateCommand == null)
            {
                throw new ArgumentNullException(nameof(templateCommand));
            }

            ShuttlecockSnapshot[] firstRun = Simulate(templateCommand, configProvider, totalFrames);
            ShuttlecockSnapshot[] secondRun = Simulate(templateCommand, configProvider, totalFrames);

            for (int i = 0; i < firstRun.Length; i++)
            {
                EnsureSnapshotsMatch(firstRun[i], secondRun[i], i);
            }
        }

        public static void RollbackSelfTest(
            ShuttlecockLaunchCommand templateCommand,
            IShuttlecockShotConfigProvider configProvider,
            uint rollbackFrame = 30,
            uint totalFrames = 60)
        {
            if (templateCommand == null)
            {
                throw new ArgumentNullException(nameof(templateCommand));
            }

            ShuttlecockSnapshot[] baseline = Simulate(templateCommand, configProvider, totalFrames);

            ShuttlecockEntity replayEntity = new ShuttlecockEntity(configProvider);
            replayEntity.EnqueueLaunch(templateCommand);
            for (uint frame = 1; frame <= totalFrames; frame++)
            {
                replayEntity.Tick(frame, DeterminismRules.FixedDeltaTime);
            }

            replayEntity.RollBack(rollbackFrame);
            for (uint frame = rollbackFrame + 1; frame <= totalFrames; frame++)
            {
                replayEntity.Tick(frame, DeterminismRules.FixedDeltaTime);
                EnsureSnapshotsMatch(
                    baseline[frame],
                    replayEntity.TakeSnapshot().WithFrameIndex(frame),
                    unchecked((int)frame));
            }
        }

        private static ShuttlecockSnapshot[] Simulate(
            ShuttlecockLaunchCommand templateCommand,
            IShuttlecockShotConfigProvider configProvider,
            uint totalFrames)
        {
            ShuttlecockEntity entity = new ShuttlecockEntity(configProvider);
            entity.EnqueueLaunch(templateCommand);
            ShuttlecockSnapshot[] snapshots = new ShuttlecockSnapshot[totalFrames + 1];
            snapshots[0] = entity.TakeSnapshot().WithFrameIndex(0);
            for (uint frame = 1; frame <= totalFrames; frame++)
            {
                entity.Tick(frame, DeterminismRules.FixedDeltaTime);
                snapshots[frame] = entity.TakeSnapshot().WithFrameIndex(frame);
            }

            return snapshots;
        }

        private static void EnsureSnapshotsMatch(ShuttlecockSnapshot left, ShuttlecockSnapshot right, int frameIndex)
        {
            EnsureEqualBits(left.XZ.x, right.XZ.x, frameIndex, nameof(ShuttlecockSnapshot.XZ));
            EnsureEqualBits(left.XZ.y, right.XZ.y, frameIndex, nameof(ShuttlecockSnapshot.XZ));
            EnsureEqualBits(left.Y, right.Y, frameIndex, nameof(ShuttlecockSnapshot.Y));
            EnsureEqualBits(left.Vxz.x, right.Vxz.x, frameIndex, nameof(ShuttlecockSnapshot.Vxz));
            EnsureEqualBits(left.Vxz.y, right.Vxz.y, frameIndex, nameof(ShuttlecockSnapshot.Vxz));
            EnsureEqualBits(left.Vy, right.Vy, frameIndex, nameof(ShuttlecockSnapshot.Vy));
            EnsureEqualBits(left.HorizontalDrag, right.HorizontalDrag, frameIndex, nameof(ShuttlecockSnapshot.HorizontalDrag));

            if (left.Phase != right.Phase ||
                left.LastValidFlyingFrame != right.LastValidFlyingFrame ||
                left.IsInBounds != right.IsInBounds ||
                left.ActiveShotType != right.ActiveShotType)
            {
                throw new InvalidOperationException($"Shuttlecock deterministic mismatch at frame={frameIndex}.");
            }
        }

        private static void EnsureEqualBits(float left, float right, int frameIndex, string fieldName)
        {
            if (BitConverter.SingleToInt32Bits(left) != BitConverter.SingleToInt32Bits(right))
            {
                throw new InvalidOperationException(
                    $"Shuttlecock deterministic mismatch at frame={frameIndex}, field={fieldName}, left={left}, right={right}.");
            }
        }

        private void ConsumePendingCommands(uint frameIndex)
        {
            while (_pendingCommands.Count > 0 && _pendingCommands[0].TargetFrame <= frameIndex)
            {
                ShuttlecockLaunchCommand command = _pendingCommands[0];
                _pendingCommands.RemoveAt(0);
                try
                {
                    if (command.TargetFrame == frameIndex)
                    {
                        ExecuteLaunch(command);
                    }
                }
                finally
                {
                    _commandPool.Return(command);
                }
            }
        }

        private void ExecuteLaunch(ShuttlecockLaunchCommand command)
        {
            if (!_configProvider.TryGet(command.ShotType, out ShuttlecockShotDefinition definition))
            {
                throw new InvalidOperationException($"Missing shuttlecock config for shot type: {command.ShotType}.");
            }

            float angleRadians = definition.LaunchAngleDegrees * DegreesToRadians;
            float horizontalSpeed = definition.HorizontalSpeed;
            float verticalScale = angleRadians > 0.0f ? PositiveAngleVerticalSpeedScale : 1.0f;
            float verticalSpeed = horizontalSpeed * MathF.Sin(angleRadians) * verticalScale;

            State.XZ = command.OriginXZ;
            State.Y = MathF.Max(0.0f, command.OriginY);
            State.Vxz = command.DirectionXZ.normalized * horizontalSpeed;
            State.Vy = verticalSpeed;
            State.Phase = ShuttlecockFlightPhase.Flying;
            State.LastValidFlyingFrame = unchecked((int)command.TargetFrame);
            State.LandingXZ = Vector2.zero;
            State.IsInBounds = false;
            State.ActiveShotType = definition.ShotType;
            State.HorizontalDrag = definition.HorizontalDrag;
        }

        private void SaveSnapshot(uint frameIndex)
        {
            _latestSnapshotFrame = frameIndex;
            _snapshotBuffer.Save(frameIndex, TakeSnapshot());
        }

        private void InsertPendingCommand(ShuttlecockLaunchCommand command)
        {
            int insertIndex = _pendingCommands.Count;
            for (int i = 0; i < _pendingCommands.Count; i++)
            {
                if (_pendingCommands[i].TargetFrame > command.TargetFrame)
                {
                    insertIndex = i;
                    break;
                }
            }

            _pendingCommands.Insert(insertIndex, command);
        }

        private void RemovePendingCommandsAfter(uint targetFrame)
        {
            for (int i = _pendingCommands.Count - 1; i >= 0; i--)
            {
                ShuttlecockLaunchCommand command = _pendingCommands[i];
                if (command.TargetFrame <= targetFrame)
                {
                    continue;
                }

                _pendingCommands.RemoveAt(i);
                _commandPool.Return(command);
            }
        }

        private void RemoveExpectedHashesAfter(uint targetFrame)
        {
            if (_expectedHashes.Count == 0)
            {
                return;
            }

            List<uint> framesToRemove = new List<uint>();
            foreach (KeyValuePair<uint, ulong> pair in _expectedHashes)
            {
                if (pair.Key > targetFrame)
                {
                    framesToRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < framesToRemove.Count; i++)
            {
                _expectedHashes.Remove(framesToRemove[i]);
            }
        }

        private static void ValidateDirection(Vector2 direction)
        {
            DeterminismRules.AssertFinite(direction.x, nameof(direction));
            DeterminismRules.AssertFinite(direction.y, nameof(direction));
            if (direction.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentOutOfRangeException(nameof(direction), "Launch direction must be non-zero.");
            }
        }
    }
}
