using System;
using System.Collections.Generic;
using System.Diagnostics;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Command;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Network;
using GameShared.FrameSync.Snapshot;
using GameShared.SkillGraph;
using Log = TEngine.Log;

namespace GameLogic
{
    public sealed class BattleSimulation : IBuffCommandSink
    {
        private const int PingIntervalFrames = 30;
        private const int HashReportIntervalFrames = 30;
        private const int PredictionBufferCapacity = 32;
        private const int InputHistoryCapacity = 128;
        private const int AuthoritativeSnapshotHistoryCapacity = 32;
        private const float InitialRttEmaMs = 100f;
        private const float RttEmaAlpha = 0.2f;

        private readonly BattleWorldState _worldState;
        private readonly RemotePlayerBuffer _remotePlayers = new RemotePlayerBuffer();
        private readonly BattleInputSender _onSendInput;
        private readonly Action<ulong> _onSendPing;
        private readonly Action<uint, ulong>? _onSendHashReport;
        private readonly IBattleClock _clock;
        private readonly GameShared.FrameSync.Core.IFrameSyncLogger? _logger;
        private readonly BattleSkillGraphRuntime _skillGraphRuntime;
        private readonly HashSet<long> _stalePlayerIds = new HashSet<long>();
        private readonly Queue<PendingAuthoritativeSnapshot> _pendingServerSnapshots =
            new Queue<PendingAuthoritativeSnapshot>(4);
        private readonly Dictionary<uint, SelfPrediction> _selfPredictions =
            new Dictionary<uint, SelfPrediction>(PredictionBufferCapacity);
        private readonly Dictionary<uint, BufferedInput> _inputHistory =
            new Dictionary<uint, BufferedInput>(InputHistoryCapacity);
        private readonly SnapshotBuffer<BattleWorldSnapshot> _authoritativeSnapshots =
            new SnapshotBuffer<BattleWorldSnapshot>(AuthoritativeSnapshotHistoryCapacity);
        private readonly List<ApplyBuffCommand> _pendingApplyBuffCommands = new List<ApplyBuffCommand>();
        private readonly List<RemoveBuffCommand> _pendingRemoveBuffCommands = new List<RemoveBuffCommand>();
        private readonly CommandPool<ApplyBuffCommand> _applyBuffCommandPool = new CommandPool<ApplyBuffCommand>();
        private readonly CommandPool<RemoveBuffCommand> _removeBuffCommandPool = new CommandPool<RemoveBuffCommand>();
        private readonly IBuffConfigProvider _buffConfigProvider;
        private readonly PredictionErrorSmoother _predictionErrorSmoother = new PredictionErrorSmoother();

        private bool _isJoined;
        private bool _hasRttSample;
        private long _selfPlayerId;
        private uint _inputSeq;
        private uint _lastAppliedFrame;
        private uint _lastPredictedFrame;
        private uint _localFrame;
        private uint _leadFrames = InputBufferTuning.MinLeadFrames;
        private uint _baselineLeadFrames = InputBufferTuning.MinLeadFrames;
        private uint _authoritativeTargetLeadFrames;
        private bool _hasAuthoritativeTargetLead;
        private int _effectiveMaxLeadFrames = InputBufferTuning.MaxLeadFrames;

        private float _rttEmaMs = InitialRttEmaMs;
        private int _pingCount;
        private int _hashReportCount;
        private int _checked;
        private int _hits;
        private int _misses;
        private int _skippedNoRecord;
        private int _skippedEvicted;
        private int _leadDecreaseCooldownSnapshots;
        private int _lastServerBufferedInputFrames;
        private bool _hasLastSentInput;
        private Fixed64 _lastSentDx;
        private Fixed64 _lastSentDy;
        private bool _hasQueuedServerSnapshot;
        private uint _latestQueuedSnapshotFrame;
        private int _rollbackCount;
        private int _lastRollbackReplayFrames;
        private double _lastRollbackElapsedMs;
        private uint _lastRollbackFrame;
        private bool _hasLastRenderedSelfPosition;
        private float _lastRenderedSelfX;
        private float _lastRenderedSelfY;
        private bool _hasLatestAuthoritativeSelfPosition;
        private Fixed64 _latestAuthoritativeSelfX;
        private Fixed64 _latestAuthoritativeSelfY;

        public BattleSimulation(
            BattleWorldState worldState,
            BattleInputSender onSendInput,
            Action<ulong> onSendPing,
            GameShared.FrameSync.Core.IFrameSyncLogger? logger = null,
            IReadOnlyDictionary<int, RuntimeSkillGraph>? skillGraphs = null,
            IBattleClock? clock = null)
            : this(worldState, onSendInput, onSendPing, null, logger, skillGraphs, clock)
        {
        }

        public BattleSimulation(
            BattleWorldState worldState,
            BattleInputSender onSendInput,
            Action<ulong> onSendPing,
            Action<uint, ulong>? onSendHashReport,
            GameShared.FrameSync.Core.IFrameSyncLogger? logger = null,
            IReadOnlyDictionary<int, RuntimeSkillGraph>? skillGraphs = null,
            IBattleClock? clock = null)
        {
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _onSendInput = onSendInput ?? throw new ArgumentNullException(nameof(onSendInput));
            _onSendPing = onSendPing ?? throw new ArgumentNullException(nameof(onSendPing));
            _onSendHashReport = onSendHashReport;
            _clock = clock ?? SystemBattleClock.Instance;
            _logger = logger;
            _buffConfigProvider = new DefaultBuffConfigProvider();
            _skillGraphRuntime = new BattleSkillGraphRuntime(
                this,
                skillGraphs ?? BattleSkillGraphLibrary.LoadDefaultGraphs(),
                new DelegateSkillRuntimeServices(TryConsumeStamina));
        }

        public bool IsJoined => _isJoined;
        public long SelfPlayerId => _selfPlayerId;
        public uint LeadFrames => _leadFrames;
        public uint BaselineLeadFrames => _baselineLeadFrames;
        public uint AppliedTargetLeadFrames => _hasAuthoritativeTargetLead ? _authoritativeTargetLeadFrames : 0u;
        public bool HasAuthoritativeTargetLead => _hasAuthoritativeTargetLead;
        public int EffectiveMaxLeadFrames => _effectiveMaxLeadFrames;
        public float ClientRttEmaMs => _rttEmaMs;
        public bool HasClientRttSample => _hasRttSample;

        public uint LastAppliedFrame => _lastAppliedFrame;
        public uint LastPredictedFrame => _lastPredictedFrame;
        public uint LocalFrame => _localFrame;
        public uint InitialAlignedFrame => unchecked(_lastAppliedFrame + _leadFrames);
        public int ConsistencyChecked => _checked;
        public int ConsistencyHits => _hits;
        public int ConsistencyMisses => _misses;
        public int ConsistencySkippedNoRecord => _skippedNoRecord;
        public int ConsistencySkippedEvicted => _skippedEvicted;
        public int StateMismatchCount { get; private set; }
        public int PositionMismatchCount { get; private set; }
        public int StaminaMismatchCount { get; private set; }
        /// <summary>
        /// Position mismatches observed while the authoritative self body was touching
        /// another body. This isolates the bounded remote-body mirror residual from
        /// gameplay-state corrections.
        /// </summary>
        public int ContactMismatchFrames { get; private set; }
        public int HashReportsSent { get; private set; }
        public int LastServerBufferedInputFrames => _lastServerBufferedInputFrames;
        public int RollbackCount => _rollbackCount;
        public int LastRollbackReplayFrames => _lastRollbackReplayFrames;
        public double LastRollbackElapsedMs => _lastRollbackElapsedMs;
        public uint LastRollbackFrame => _lastRollbackFrame;
        public float RenderErrorOffsetX => _predictionErrorSmoother.OffsetX;
        public float RenderErrorOffsetY => _predictionErrorSmoother.OffsetY;
        public float LastPredictionCorrectionMagnitude => _predictionErrorSmoother.LastCorrectionMagnitude;
        public float PredictionSmoothingRemainingSeconds => _predictionErrorSmoother.RemainingSeconds;
        public RemotePlayerBuffer RemotePlayers => _remotePlayers;
        public int ActiveSkillExecutionCount => _skillGraphRuntime.ActiveExecutionCount;

        public void AdvancePredictionErrorSmoothing(float deltaTime)
        {
            _predictionErrorSmoother.Advance(deltaTime);
        }

        public void RecordRenderedSelfPosition(float x, float y)
        {
            if (!_isJoined)
            {
                return;
            }

            _lastRenderedSelfX = x;
            _lastRenderedSelfY = y;
            _hasLastRenderedSelfPosition = true;
        }

        public bool TryGetLatestAuthoritativeSelfPosition(out Fixed64 x, out Fixed64 y)
        {
            x = _latestAuthoritativeSelfX;
            y = _latestAuthoritativeSelfY;
            return _hasLatestAuthoritativeSelfPosition;
        }

        public void EnqueueServerSnapshot(BattleWorldSnapshot snapshot, uint selfLatestAcceptedInputFrame)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.FrameIndex <= _lastAppliedFrame)
            {
                return;
            }

            if (_hasQueuedServerSnapshot && snapshot.FrameIndex <= _latestQueuedSnapshotFrame)
            {
                return;
            }

            _pendingServerSnapshots.Enqueue(new PendingAuthoritativeSnapshot(snapshot, selfLatestAcceptedInputFrame));
            _hasQueuedServerSnapshot = true;
            _latestQueuedSnapshotFrame = snapshot.FrameIndex;
        }

        public void ProcessPong(long rttMs)
        {
            if (rttMs < 0)
            {
                return;
            }

            if (!_hasRttSample)
            {
                _rttEmaMs = rttMs;
                _hasRttSample = true;
            }
            else
            {
                _rttEmaMs = (RttEmaAlpha * rttMs) + ((1.0f - RttEmaAlpha) * _rttEmaMs);
            }

            RefreshBaselineLeadFrames();
        }

        /// <summary>
        /// Apply server-issued target lead. 0 means disabled → fall back to client RTT.
        /// Must be called from downlink gate release callback together with the snapshot.
        /// </summary>
        public void ApplyAuthoritativeTargetLead(uint targetLeadFrames)
        {
            if (targetLeadFrames == 0u)
            {
                ClearAuthoritativeTargetLead();
                return;
            }

            _authoritativeTargetLeadFrames = targetLeadFrames;
            _hasAuthoritativeTargetLead = true;
            RefreshBaselineLeadFrames();
        }

        public void ClearAuthoritativeTargetLead()
        {
            if (!_hasAuthoritativeTargetLead && _authoritativeTargetLeadFrames == 0u)
            {
                _effectiveMaxLeadFrames = InputBufferTuning.MaxLeadFrames;
                return;
            }

            _hasAuthoritativeTargetLead = false;
            _authoritativeTargetLeadFrames = 0u;
            RefreshBaselineLeadFrames();
        }


        public TickResult Tick(uint frameIndex, Fixed64 fixedDt, Fixed64 dx, Fixed64 dy, int skillId = 0)
        {
            if (!_isJoined)
            {
                return default;
            }

            try
            {
                DeterminismRules.AssertFixedDt(fixedDt);

                _localFrame = frameIndex;

                SaveInputHistory(frameIndex, dx, dy, skillId);
                LogInputEdgeIfNeeded(frameIndex, dx, dy);
                SendPingIfNeeded();
                _onSendInput(frameIndex, ++_inputSeq, dx, dy, skillId);

                bool snapshotApplied = ApplyPendingServerSnapshot(
                    frameIndex,
                    fixedDt,
                    out int catchUpFrames,
                    out _,
                    out bool consistencyMismatch);
                AdvancePredictionTo(frameIndex, fixedDt);
                SendHashReportIfNeeded();

                uint targetFrame = unchecked(_lastAppliedFrame + _leadFrames);
                uint targetFrameExclusive = unchecked(targetFrame + 1u);

                return new TickResult(
                    snapshotApplied,
                    _lastAppliedFrame,
                    catchUpFrames,
                    targetFrameExclusive,
                    consistencyMismatch);
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, $"BattleSimulation.Tick failed. Frame={frameIndex}.");
                throw;
            }
        }

        public void SetJoined(long playerId, uint serverFrame, float x, float y)
        {
            SetJoined(playerId, serverFrame, (Fixed64)x, (Fixed64)y);
        }

        public void SetJoined(long playerId, uint serverFrame, Fixed64 x, Fixed64 y)
        {
            ClearWorldState();

            _selfPlayerId = playerId;
            _lastAppliedFrame = serverFrame;
            _lastPredictedFrame = serverFrame;
            _localFrame = serverFrame;
            _leadFrames = InputBufferTuning.MinLeadFrames;
            _baselineLeadFrames = InputBufferTuning.MinLeadFrames;
            _rttEmaMs = InitialRttEmaMs;
            _hasRttSample = false;
            _hasAuthoritativeTargetLead = false;
            _authoritativeTargetLeadFrames = 0u;
            _effectiveMaxLeadFrames = InputBufferTuning.MaxLeadFrames;
            RefreshBaselineLeadFrames();
            _pingCount = 0;
            _hashReportCount = 0;
            HashReportsSent = 0;
            _checked = 0;
            _hits = 0;
            _misses = 0;
            _skippedNoRecord = 0;
            _skippedEvicted = 0;
            StateMismatchCount = 0;
            PositionMismatchCount = 0;
            StaminaMismatchCount = 0;
            ContactMismatchFrames = 0;
            _leadDecreaseCooldownSnapshots = 0;
            _lastServerBufferedInputFrames = 0;
            _hasLastSentInput = false;
            _lastSentDx = Fixed64.Zero;
            _lastSentDy = Fixed64.Zero;
            _selfPredictions.Clear();
            _inputHistory.Clear();
            _pendingServerSnapshots.Clear();
            _authoritativeSnapshots.Clear();
            ClearPendingBuffCommands();
            _skillGraphRuntime.Clear();
            _hasQueuedServerSnapshot = false;
            _latestQueuedSnapshotFrame = serverFrame;
            _rollbackCount = 0;
            _lastRollbackReplayFrames = 0;
            _lastRollbackElapsedMs = 0.0d;
            _lastRollbackFrame = 0u;
            _hasLastRenderedSelfPosition = false;
            _lastRenderedSelfX = 0.0f;
            _lastRenderedSelfY = 0.0f;
            _hasLatestAuthoritativeSelfPosition = false;
            _latestAuthoritativeSelfX = Fixed64.Zero;
            _latestAuthoritativeSelfY = Fixed64.Zero;
            _predictionErrorSmoother.Reset();
            _isJoined = true;

            _worldState.AddOrUpdatePlayer(playerId, x, y);
        }


        public void AlignLocalFrame(uint frameIndex)
        {
            if (!_isJoined)
            {
                throw new InvalidOperationException("Cannot align local frame before joining battle.");
            }

            _localFrame = frameIndex;
        }

        public void RollBack(uint targetFrame)
        {
            if (!_isJoined)
            {
                return;
            }

            if (!_authoritativeSnapshots.TryGet(targetFrame, out BattleWorldSnapshot snapshot))
            {
                Log.Warning($"[Rollback] Missing authoritative snapshot for frame={targetFrame}.");
                return;
            }

            // 手动入口：强制回滚。
            ReconcileAuthoritativeSnapshot(
                snapshot,
                _localFrame,
                DeterminismRules.FixedDeltaTimeFixed64,
                ConsistencyResult.Miss,
                forceRollback: true,
                "manual");
        }

        public static bool RunSelfTest(out string failedCase)
        {
            return BattlePredictionSelfTestSuite.Run(out failedCase);
        }

        private bool ApplyPendingServerSnapshot(
            uint currentFrame,
            Fixed64 fixedDt,
            out int catchUpFrames,
            out uint targetFrameExclusive,
            out bool consistencyMismatch)
        {
            catchUpFrames = 0;
            targetFrameExclusive = 0;
            consistencyMismatch = false;
            if (!_hasQueuedServerSnapshot || _pendingServerSnapshots.Count == 0)
            {
                return false;
            }

            bool snapshotApplied = false;
            while (_pendingServerSnapshots.Count > 0)
            {
                PendingAuthoritativeSnapshot pendingSnapshot = _pendingServerSnapshots.Dequeue();
                BattleWorldSnapshot snapshot = pendingSnapshot.Snapshot;
                if (snapshot.FrameIndex <= _lastAppliedFrame)
                {
                    continue;
                }

                UpdateLeadFramesFromAcceptedInput(snapshot.FrameIndex, pendingSnapshot.SelfLatestAcceptedInputFrame);
                ConsistencyResult consistencyResult = CheckConsistency(snapshot);
                bool mismatch = consistencyResult == ConsistencyResult.Miss;
                consistencyMismatch |= mismatch;

                uint replayTargetFrame = currentFrame;
                if (_pendingServerSnapshots.Count > 0)
                {
                    uint nextSnapshotFrame = _pendingServerSnapshots.Peek().Snapshot.FrameIndex;
                    if (nextSnapshotFrame < replayTargetFrame)
                    {
                        replayTargetFrame = nextSnapshotFrame;
                    }
                }

                ReconcileAuthoritativeSnapshot(
                    snapshot,
                    replayTargetFrame,
                    fixedDt,
                    consistencyResult,
                    forceRollback: false,
                    mismatch ? "consistency-miss" : string.Empty);
                snapshotApplied = true;
            }

            _hasQueuedServerSnapshot = false;
            _latestQueuedSnapshotFrame = _lastAppliedFrame;

            if (!snapshotApplied)
            {
                return false;
            }

            uint targetLocalFrame = unchecked(_lastAppliedFrame + _leadFrames);
            int frameDeltaToTarget = unchecked((int)(targetLocalFrame - currentFrame));
            catchUpFrames = frameDeltaToTarget > 0 ? frameDeltaToTarget : 0;
            targetFrameExclusive = catchUpFrames > 0
                ? unchecked(targetLocalFrame + 1u)
                : 0u;
            return true;
        }

        private ConsistencyResult CheckConsistency(BattleWorldSnapshot snapshot)
        {
            bool hasAuthoritativeSelf = TryGetAuthoritativeSelf(snapshot, out PlayerStateSnapshot authoritativeSelf);
            bool hasPrediction = _selfPredictions.TryGetValue(snapshot.FrameIndex, out SelfPrediction prediction);

            if (hasPrediction)
            {
                _selfPredictions.Remove(snapshot.FrameIndex);
            }

            if (!hasAuthoritativeSelf || !hasPrediction)
            {
                _skippedNoRecord++;
                return ConsistencyResult.NoRecord;
            }

            _checked++;
            bool matched = prediction.X.m_rawValue == authoritativeSelf.X.m_rawValue &&
                           prediction.Y.m_rawValue == authoritativeSelf.Y.m_rawValue &&
                           ArePlayerSnapshotsEquivalent(prediction.Snapshot, authoritativeSelf);
            if (matched)
            {
                _hits++;
                return ConsistencyResult.Hit;
            }

            _misses++;
            string mismatchLabel = GetMismatchLabel(prediction.Snapshot, authoritativeSelf);
            if (string.Equals(mismatchLabel, "PositionMismatch", StringComparison.Ordinal))
            {
                PositionMismatchCount++;
                if (IsSelfTouching(snapshot, _selfPlayerId))
                {
                    ContactMismatchFrames++;
                }
            }
            else
            {
                StateMismatchCount++;
                if (string.Equals(mismatchLabel, "StaminaMismatch", StringComparison.Ordinal))
                {
                    StaminaMismatchCount++;
                }
            }
            Fixed64 deltaX = authoritativeSelf.X - prediction.X;
            Fixed64 deltaY = authoritativeSelf.Y - prediction.Y;
            Log.Warning(
                $"[Consistency][{mismatchLabel}] MISMATCH frame={snapshot.FrameIndex} " +
                $"predPos=({prediction.X},{prediction.Y}) authPos=({authoritativeSelf.X},{authoritativeSelf.Y}) deltaPos=({(float)deltaX:F4},{(float)deltaY:F4}) " +
                $"predAttr=(hp:{prediction.Attributes.Health}/{prediction.Attributes.MaxHealth},mp:{prediction.Attributes.Mana}/{prediction.Attributes.MaxMana},st:{prediction.Attributes.Stamina}/{prediction.Attributes.MaxStamina},atk:{prediction.Attributes.Attack}) " +
                $"authAttr=(hp:{authoritativeSelf.Attributes.Health}/{authoritativeSelf.Attributes.MaxHealth},mp:{authoritativeSelf.Attributes.Mana}/{authoritativeSelf.Attributes.MaxMana},st:{authoritativeSelf.Attributes.Stamina}/{authoritativeSelf.Attributes.MaxStamina},atk:{authoritativeSelf.Attributes.Attack}) " +
                $"predBuffs={FormatBuffs(prediction.Snapshot.ActiveBuffs)} authBuffs={FormatBuffs(authoritativeSelf.ActiveBuffs)}");
            return ConsistencyResult.Miss;
        }

        private static bool IsSelfTouching(BattleWorldSnapshot snapshot, long selfPlayerId)
        {
            if (snapshot == null || snapshot.PhysicsSnapshot == null)
            {
                return false;
            }

            IReadOnlyList<PhysicsContactSnapshot> contacts = snapshot.PhysicsSnapshot.Contacts;
            for (int i = 0; i < contacts.Count; i++)
            {
                PhysicsContactSnapshot contact = contacts[i];
                if (contact.IsTouching &&
                    (contact.BodyAId == selfPlayerId || contact.BodyBId == selfPlayerId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetAuthoritativeSelf(BattleWorldSnapshot snapshot, out PlayerStateSnapshot selfSnapshot)
        {
            IReadOnlyList<PlayerStateSnapshot> players = snapshot.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                if (player.PlayerId == _selfPlayerId)
                {
                    selfSnapshot = player;
                    return true;
                }
            }

            selfSnapshot = default;
            return false;
        }

        private void ApplyAuthoritativeSnapshot(
            BattleWorldSnapshot snapshot,
            ConsistencyResult consistencyResult,
            bool forceRollback)
        {
            if (TryGetAuthoritativeSelf(snapshot, out PlayerStateSnapshot authoritativeSelf))
            {
                _latestAuthoritativeSelfX = authoritativeSelf.X;
                _latestAuthoritativeSelfY = authoritativeSelf.Y;
                _hasLatestAuthoritativeSelfPosition = true;
            }
            else
            {
                _hasLatestAuthoritativeSelfPosition = false;
            }

            _lastAppliedFrame = snapshot.FrameIndex;

            // 别人永远不进 _worldState，只进 RemotePlayerBuffer；物理层
            // 仅保留 kinematic 镜像供本地占位预测。
            _stalePlayerIds.Clear();
            _remotePlayers.ApplyAuthoritative(snapshot, _selfPlayerId, _stalePlayerIds);
            foreach (long removedPlayerId in _stalePlayerIds)
            {
                _worldState.PhysicsWorld.RemoveBody(checked((int)removedPlayerId));
            }
            _stalePlayerIds.Clear();

            // Hit：自己预测已与权威一致，保持现状。
            // Miss / NoRecord / 手动回滚：RestoreSelfOnly 只恢复自己。
            bool restoreSelf = forceRollback || consistencyResult != ConsistencyResult.Hit;
            if (restoreSelf)
            {
                _worldState.RestoreSelfOnly(snapshot, _selfPlayerId);
            }

            MirrorRemotePhysicsBodies();

            // 只有真的拨回自己时才回退预测游标并逐位恢复技能执行态。
            if (restoreSelf)
            {
                _lastPredictedFrame = snapshot.FrameIndex;
                ClearPendingBuffCommands();
                _skillGraphRuntime.RestoreExecutions(authoritativeSelf.SkillExecutions);
            }

            RemoveConfirmedInputHistory(snapshot.FrameIndex);
        }

        private void ReconcileAuthoritativeSnapshot(
            BattleWorldSnapshot snapshot,
            uint replayTargetFrame,
            Fixed64 fixedDt,
            ConsistencyResult consistencyResult,
            bool forceRollback,
            string rollbackReason)
        {
            // S3 依赖：权威快照必须先入库，禁止因一致短路吞掉 Save。
            _authoritativeSnapshots.Save(snapshot.FrameIndex, snapshot);
            ApplyAuthoritativeSnapshot(snapshot, consistencyResult, forceRollback);

            // 一致时保留后续帧预测：同一 tick 内后续快照还要用它们做 CheckConsistency。
            // 失配/无记录/手动回滚才丢弃 snapshot 帧及之后的预测。
            bool restoreSelf = forceRollback || consistencyResult != ConsistencyResult.Hit;
            if (restoreSelf)
            {
                DiscardPredictionsAtOrAfter(snapshot.FrameIndex);
            }

            // logRollback 同时控制日志与回滚重放：true = 失配或强制回滚。
            // NoRecord 会 RestoreSelfOnly，但不计 rollback、不在此处重放（Tick 末尾 AdvancePredictionTo 补齐）。
            bool logRollback = forceRollback || consistencyResult == ConsistencyResult.Miss;
            int replayFrames = 0;
            Stopwatch? rollbackTimer = null;
            if (logRollback)
            {
                rollbackTimer = Stopwatch.StartNew();
            }

            // 一致时跳过重放；正常向前推进仍由 Tick 末尾的 AdvancePredictionTo 负责。
            if (logRollback && replayTargetFrame > snapshot.FrameIndex)
            {
                replayFrames = unchecked((int)(replayTargetFrame - snapshot.FrameIndex));
                AdvancePredictionTo(replayTargetFrame, fixedDt);
            }

            // Matching authoritative snapshots must not re-baseline the current
            // render offset; otherwise each snapshot restarts the smoothing timer.
            if (logRollback)
            {
                CapturePredictionErrorAfterReconciliation();
            }

            if (!logRollback)
            {
                return;
            }

            rollbackTimer?.Stop();
            _rollbackCount++;
            _lastRollbackFrame = snapshot.FrameIndex;
            _lastRollbackReplayFrames = replayFrames;
            _lastRollbackElapsedMs = rollbackTimer?.Elapsed.TotalMilliseconds ?? 0.0d;

            Log.Info(
                $"[Rollback] reason={rollbackReason} frame={snapshot.FrameIndex} replayTo={replayTargetFrame} replayFrames={replayFrames} elapsedMs={_lastRollbackElapsedMs:F3}");
        }

        private enum ConsistencyResult
        {
            Hit,
            Miss,
            NoRecord
        }

        private void CapturePredictionErrorAfterReconciliation()
        {
            if (!_hasLastRenderedSelfPosition ||
                !_worldState.TryGetPlayer(_selfPlayerId, out PlayerState reconciledSelf))
            {
                _predictionErrorSmoother.SetOffset(0.0f, 0.0f);
                return;
            }

            _predictionErrorSmoother.SetOffset(
                _lastRenderedSelfX - (float)reconciledSelf.X,
                _lastRenderedSelfY - (float)reconciledSelf.Y);
        }

        private void SendPingIfNeeded()
        {
            _pingCount++;
            if (_pingCount < PingIntervalFrames)
            {
                return;
            }

            _pingCount = 0;
            _onSendPing(checked((ulong)_clock.NowMs));
        }

        private void SendHashReportIfNeeded()
        {
            if (_onSendHashReport == null)
            {
                return;
            }

            _hashReportCount++;
            if (_hashReportCount < HashReportIntervalFrames)
            {
                return;
            }

            if (!_authoritativeSnapshots.TryGet(_lastAppliedFrame, out BattleWorldSnapshot snapshot))
            {
                return;
            }

            _hashReportCount = 0;
            _onSendHashReport(_lastAppliedFrame, StateHasher.Hash(snapshot));
            HashReportsSent++;
        }

        private void LogInputEdgeIfNeeded(uint frameIndex, Fixed64 dx, Fixed64 dy)
        {
            if (_hasLastSentInput && AreInputsEqual(dx, dy, _lastSentDx, _lastSentDy))
            {
                return;
            }

            _hasLastSentInput = true;
            _lastSentDx = dx;
            _lastSentDy = dy;
        }

        private void RefreshBaselineLeadFrames()
        {
            int leadFrames;
            if (_hasAuthoritativeTargetLead && _authoritativeTargetLeadFrames > 0u)
            {
                leadFrames = (int)_authoritativeTargetLeadFrames;
                _effectiveMaxLeadFrames = TargetLeadCalculator.ComputeEffectiveMaxLead(leadFrames);
            }
            else
            {
                leadFrames = TargetLeadCalculator.Compute(_rttEmaMs);
                _effectiveMaxLeadFrames = InputBufferTuning.MaxLeadFrames;
            }

            _baselineLeadFrames = (uint)Math.Clamp(
                leadFrames,
                InputBufferTuning.MinLeadFrames,
                _effectiveMaxLeadFrames);

            // Soft max only bounds future raises. Never snap lead down on target decrease —
            // cooldown feedback owns the slow fall (decision five).
            if (_leadFrames < _baselineLeadFrames)
            {
                _leadFrames = _baselineLeadFrames;
            }
        }

        // Closed-loop lead control: keep the server-side accepted-input queue in a healthy range.
        private void UpdateLeadFramesFromAcceptedInput(uint snapshotFrame, uint latestAcceptedInputFrame)
        {
            int previousLead = (int)_leadFrames;
            int serverBufferedFrames = latestAcceptedInputFrame > snapshotFrame
                ? unchecked((int)(latestAcceptedInputFrame - snapshotFrame))
                : 0;
            _lastServerBufferedInputFrames = serverBufferedFrames;

            if (serverBufferedFrames < InputBufferTuning.MinAcceptedInputBufferFrames)
            {
                int deficit = InputBufferTuning.MinAcceptedInputBufferFrames - serverBufferedFrames;
                int raisedLead = Math.Max((int)_baselineLeadFrames, (int)_leadFrames + deficit);
                // Soft max caps how high we may raise; never clamp existing lead downward here.
                int cappedRaise = Math.Min(raisedLead, _effectiveMaxLeadFrames);
                if (cappedRaise > (int)_leadFrames)
                {
                    _leadFrames = (uint)Math.Max(InputBufferTuning.MinLeadFrames, cappedRaise);
                }

                _leadDecreaseCooldownSnapshots = InputBufferTuning.LeadDecreaseCooldownSnapshots;
            }


            else if (serverBufferedFrames > InputBufferTuning.MaxAcceptedInputBufferFrames)
            {
                if (_leadDecreaseCooldownSnapshots > 0)
                {
                    _leadDecreaseCooldownSnapshots--;
                }
                else if (_leadFrames > _baselineLeadFrames)
                {
                    _leadFrames--;
                    _leadDecreaseCooldownSnapshots = InputBufferTuning.LeadDecreaseCooldownSnapshots;
                }
            }
            else
            {
                if (_leadDecreaseCooldownSnapshots > 0)
                {
                    _leadDecreaseCooldownSnapshots--;
                }

                if (_leadFrames < _baselineLeadFrames)
                {
                    _leadFrames = _baselineLeadFrames;
                }
            }

            if (previousLead == _leadFrames)
            {
                return;
            }
        }

        private void AdvancePredictionTo(uint targetFrame, Fixed64 fixedDt)
        {
            int frameCount = unchecked((int)(targetFrame - _lastPredictedFrame));
            if (frameCount <= 0)
            {
                return;
            }

            if (!_worldState.TryGetPlayer(_selfPlayerId, out _))
            {
                return;
            }

            for (int i = 1; i <= frameCount; i++)
            {
                uint replayFrame = unchecked(_lastPredictedFrame + (uint)i);
                BufferedInput input = _inputHistory.TryGetValue(replayFrame, out BufferedInput bufferedInput)
                    ? bufferedInput
                    : default;
                ApplyLocalPrediction(replayFrame, input.Dx, input.Dy, input.SkillId, fixedDt);
            }

            _lastPredictedFrame = targetFrame;
        }

        private void SaveInputHistory(uint frameIndex, Fixed64 dx, Fixed64 dy, int skillId)
        {
            if (!_inputHistory.ContainsKey(frameIndex) && _inputHistory.Count >= InputHistoryCapacity)
            {
                uint oldest = uint.MaxValue;
                foreach (uint key in _inputHistory.Keys)
                {
                    if (key < oldest)
                    {
                        oldest = key;
                    }
                }

                if (oldest != uint.MaxValue)
                {
                    _inputHistory.Remove(oldest);
                }
            }

            _inputHistory[frameIndex] = new BufferedInput(dx, dy, skillId);
        }

        private void RemoveConfirmedInputHistory(uint confirmedFrame)
        {
            if (_inputHistory.Count == 0)
            {
                return;
            }

            List<uint> framesToRemove = new List<uint>();
            foreach (uint frame in _inputHistory.Keys)
            {
                if (frame <= confirmedFrame)
                {
                    framesToRemove.Add(frame);
                }
            }

            if (framesToRemove.Count == 0)
            {
                return;
            }

            for (int i = 0; i < framesToRemove.Count; i++)
            {
                _inputHistory.Remove(framesToRemove[i]);
            }
        }

        private void DiscardPredictionsAtOrAfter(uint frameIndex)
        {
            if (_selfPredictions.Count == 0)
            {
                return;
            }

            List<uint> framesToRemove = new List<uint>();
            foreach (uint predictedFrame in _selfPredictions.Keys)
            {
                if (predictedFrame >= frameIndex)
                {
                    framesToRemove.Add(predictedFrame);
                }
            }

            for (int i = 0; i < framesToRemove.Count; i++)
            {
                _selfPredictions.Remove(framesToRemove[i]);
            }
        }

        private void ApplyLocalPrediction(uint frameIndex, Fixed64 dx, Fixed64 dy, int skillId, Fixed64 fixedDt)
        {
            if (!_worldState.TryGetPlayer(_selfPlayerId, out PlayerState selfPlayer))
            {
                return;
            }

            if (skillId > 0)
            {
                _skillGraphRuntime.QueueSkillRequest(
                    _selfPlayerId,
                    _selfPlayerId,
                    skillId,
                    frameIndex,
                    dx,
                    dy);
            }

            _skillGraphRuntime.Step(frameIndex);
            ProcessBuffCommands(frameIndex);
            MoveSystem.Apply(_worldState, selfPlayer, dx, dy, fixedDt);
            DisplacementSystem.Apply(selfPlayer, _worldState.PhysicsWorld);
            DisplacementSystem.Decay(selfPlayer);
            _worldState.PhysicsWorld.Step(fixedDt);
            SyncAllPlayersFromPhysics();
            ApplyBuffTicks(frameIndex);
            StaminaSystem.Tick(selfPlayer);
            RecalculateNumericStates();
            SyncSkillExecutionsToSelf(selfPlayer);
            SaveSelfPrediction(frameIndex, selfPlayer);
        }

        private void SyncAllPlayersFromPhysics()
        {
            foreach (PlayerState player in _worldState.Players)
            {
                MoveSystem.SyncFromPhysics(_worldState, player);
            }
        }

        private bool TryConsumeStamina(long playerId, int amount)
        {
            return _worldState.TryGetPlayer(playerId, out PlayerState state) &&
                   StaminaSystem.TryConsume(state, amount);
        }

        private void SyncSkillExecutionsToSelf(PlayerState selfPlayer)
        {
            Dictionary<long, ActiveSkillExecutionSnapshot> executions = _skillGraphRuntime.CaptureExecutions();
            selfPlayer.SkillExecutions.Clear();
            if (executions.TryGetValue(selfPlayer.PlayerId, out ActiveSkillExecutionSnapshot execution))
            {
                selfPlayer.SkillExecutions[selfPlayer.PlayerId] = execution;
            }
        }

        private void MirrorRemotePhysicsBodies()
        {
            foreach (PlayerStateSnapshot remote in _remotePlayers.Players)
            {
                int bodyId = checked((int)remote.PlayerId);
                _worldState.PhysicsWorld.EnsureBody(bodyId, remote.X, remote.Y);
                _worldState.PhysicsWorld.SetBodyTransform(bodyId, remote.X, remote.Y, true);
                _worldState.PhysicsWorld.SetBodyKinematicObstacle(bodyId, true);
            }
        }

        private void SaveSelfPrediction(uint frameIndex, PlayerState selfPlayer)
        {
            if (!_selfPredictions.ContainsKey(frameIndex) && _selfPredictions.Count >= PredictionBufferCapacity)
            {
                uint oldest = uint.MaxValue;
                foreach (uint key in _selfPredictions.Keys)
                {
                    if (key < oldest)
                    {
                        oldest = key;
                    }
                }

                if (oldest != uint.MaxValue)
                {
                    _selfPredictions.Remove(oldest);
                    _skippedEvicted++;
                }
            }

            _selfPredictions[frameIndex] = new SelfPrediction(
                new PlayerStateSnapshot(
                    selfPlayer.PlayerId,
                    selfPlayer.X,
                    selfPlayer.Y,
                    selfPlayer.CaptureAttributeSnapshot(),
                    selfPlayer.ActiveBuffs,
                    selfPlayer.NextRuntimeBuffId,
                    selfPlayer.Numeric.CaptureSnapshot(),
                    selfPlayer.StaminaRegenCounterFrames,
                    selfPlayer.DashVelocityX,
                    selfPlayer.DashVelocityY,
                    selfPlayer.DashRemainingFrames,
                    selfPlayer.DashRuntimeBuffId,
                    selfPlayer.KnockbackVelocityX,
                    selfPlayer.KnockbackVelocityY,
                    selfPlayer.KnockbackRemainingFrames,
                    selfPlayer.KnockbackRuntimeBuffId,
                    selfPlayer.SkillExecutions));
        }

        private void ClearWorldState()
        {
            _stalePlayerIds.Clear();
            foreach (PlayerState player in _worldState.Players)
            {
                _stalePlayerIds.Add(player.PlayerId);
            }

            foreach (long playerId in _stalePlayerIds)
            {
                _worldState.RemovePlayer(playerId);
            }

            _stalePlayerIds.Clear();
            _worldState.PhysicsWorld.ClearBodies();
            _remotePlayers.Clear();
        }

        public void EnqueueApplyBuff(ApplyBuffCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            ApplyBuffCommand queuedCommand = _applyBuffCommandPool.Rent();
            queuedCommand.CasterId = command.CasterId;
            queuedCommand.TargetId = command.TargetId;
            queuedCommand.BuffId = command.BuffId;
            queuedCommand.DurationFrames = command.DurationFrames;
            queuedCommand.StackCount = command.StackCount;
            queuedCommand.FrameIndex = command.FrameIndex;
            queuedCommand.Flags = command.Flags;
            queuedCommand.HasDisplacementVelocityOverride = command.HasDisplacementVelocityOverride;
            queuedCommand.DisplacementVelocityX = command.DisplacementVelocityX;
            queuedCommand.DisplacementVelocityY = command.DisplacementVelocityY;
            InsertApplyCommand(queuedCommand);
        }

        public void EnqueueRemoveBuff(RemoveBuffCommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            RemoveBuffCommand queuedCommand = _removeBuffCommandPool.Rent();
            queuedCommand.TargetId = command.TargetId;
            queuedCommand.RuntimeBuffId = command.RuntimeBuffId;
            queuedCommand.BuffId = command.BuffId;
            queuedCommand.RemoveReason = command.RemoveReason;
            queuedCommand.FrameIndex = command.FrameIndex;
            InsertRemoveCommand(queuedCommand);
        }

        public bool HasBuff(long targetId, int buffId)
        {
            return _worldState.TryGetPlayer(targetId, out PlayerState targetState) &&
                   BuffSystem.HasBuff(targetState, buffId);
        }

        public int GetBuffStackCount(long targetId, int buffId)
        {
            return _worldState.TryGetPlayer(targetId, out PlayerState targetState)
                ? BuffSystem.GetBuffStackCount(targetState, buffId)
                : 0;
        }

        private static bool AreInputsEqual(Fixed64 leftDx, Fixed64 leftDy, Fixed64 rightDx, Fixed64 rightDy)
        {
            return leftDx.m_rawValue == rightDx.m_rawValue && leftDy.m_rawValue == rightDy.m_rawValue;
        }

        private static bool AreAttributesEqual(PlayerAttributeSnapshot left, PlayerAttributeSnapshot right)
        {
            return left.Health == right.Health &&
                   left.MaxHealth == right.MaxHealth &&
                   left.Mana == right.Mana &&
                   left.MaxMana == right.MaxMana &&
                   left.Attack == right.Attack &&
                   left.Stamina == right.Stamina &&
                   left.MaxStamina == right.MaxStamina;
        }

        private bool ArePlayerSnapshotsEquivalent(PlayerStateSnapshot predicted, PlayerStateSnapshot authoritative)
        {
            return predicted.X.m_rawValue == authoritative.X.m_rawValue &&
                   predicted.Y.m_rawValue == authoritative.Y.m_rawValue &&
                   AreAttributesEqual(predicted.Attributes, authoritative.Attributes) &&
                   predicted.StaminaRegenCounterFrames == authoritative.StaminaRegenCounterFrames &&
                   predicted.DashVelocityX.m_rawValue == authoritative.DashVelocityX.m_rawValue &&
                   predicted.DashVelocityY.m_rawValue == authoritative.DashVelocityY.m_rawValue &&
                   predicted.DashRemainingFrames == authoritative.DashRemainingFrames &&
                   predicted.DashRuntimeBuffId == authoritative.DashRuntimeBuffId &&
                   predicted.KnockbackVelocityX.m_rawValue == authoritative.KnockbackVelocityX.m_rawValue &&
                   predicted.KnockbackVelocityY.m_rawValue == authoritative.KnockbackVelocityY.m_rawValue &&
                   predicted.KnockbackRemainingFrames == authoritative.KnockbackRemainingFrames &&
                   predicted.KnockbackRuntimeBuffId == authoritative.KnockbackRuntimeBuffId &&
                   predicted.NextRuntimeBuffId == authoritative.NextRuntimeBuffId &&
                   AreBuffsEqual(predicted.ActiveBuffs, authoritative.ActiveBuffs) &&
                   AreNumericSnapshotsEqual(predicted.Numeric, authoritative.Numeric) &&
                   AreSkillExecutionsEqual(predicted.SkillExecutions, authoritative.SkillExecutions);
        }

        private static string GetMismatchLabel(
            PlayerStateSnapshot predicted,
            PlayerStateSnapshot authoritative)
        {
            if (predicted.Stamina != authoritative.Stamina ||
                predicted.MaxStamina != authoritative.MaxStamina ||
                predicted.StaminaRegenCounterFrames != authoritative.StaminaRegenCounterFrames)
            {
                return "StaminaMismatch";
            }

            if (predicted.DashVelocityX.m_rawValue != authoritative.DashVelocityX.m_rawValue ||
                predicted.DashVelocityY.m_rawValue != authoritative.DashVelocityY.m_rawValue ||
                predicted.DashRemainingFrames != authoritative.DashRemainingFrames ||
                predicted.DashRuntimeBuffId != authoritative.DashRuntimeBuffId ||
                !AreSkillExecutionsEqual(predicted.SkillExecutions, authoritative.SkillExecutions))
            {
                return "DashMismatch";
            }

            if (predicted.KnockbackVelocityX.m_rawValue != authoritative.KnockbackVelocityX.m_rawValue ||
                predicted.KnockbackVelocityY.m_rawValue != authoritative.KnockbackVelocityY.m_rawValue ||
                predicted.KnockbackRemainingFrames != authoritative.KnockbackRemainingFrames ||
                predicted.KnockbackRuntimeBuffId != authoritative.KnockbackRuntimeBuffId)
            {
                return "KnockbackMismatch";
            }

            if (predicted.X.m_rawValue != authoritative.X.m_rawValue ||
                predicted.Y.m_rawValue != authoritative.Y.m_rawValue)
            {
                return "PositionMismatch";
            }

            return "StateMismatch";
        }

        private static bool AreSkillExecutionsEqual(
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> left,
            IReadOnlyDictionary<long, ActiveSkillExecutionSnapshot> right)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            if (leftCount == 0)
            {
                return true;
            }

            foreach (KeyValuePair<long, ActiveSkillExecutionSnapshot> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out ActiveSkillExecutionSnapshot rightExecution))
                {
                    return false;
                }

                ActiveSkillExecutionSnapshot leftExecution = pair.Value;
                if (leftExecution.CasterId != rightExecution.CasterId ||
                    leftExecution.TargetId != rightExecution.TargetId ||
                    leftExecution.SkillId != rightExecution.SkillId ||
                    leftExecution.DirectionX.m_rawValue != rightExecution.DirectionX.m_rawValue ||
                    leftExecution.DirectionY.m_rawValue != rightExecution.DirectionY.m_rawValue ||
                    !AreRunnerSnapshotsEqual(leftExecution.RunnerSnapshot, rightExecution.RunnerSnapshot))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreRunnerSnapshotsEqual(
            SkillExecutionSnapshot left,
            SkillExecutionSnapshot right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null ||
                left.CurrentNodeId != right.CurrentNodeId ||
                left.Status != right.Status ||
                left.ExecutedSteps != right.ExecutedSteps ||
                left.FrameIndex != right.FrameIndex ||
                !string.Equals(left.Message, right.Message, StringComparison.Ordinal))
            {
                return false;
            }

            SkillBlackboardSnapshot leftBlackboard = left.Blackboard;
            SkillBlackboardSnapshot rightBlackboard = right.Blackboard;
            return AreDictionariesEqual(leftBlackboard?.Strings, rightBlackboard?.Strings, StringComparer.Ordinal) &&
                   AreFloatDictionariesEqual(leftBlackboard?.Floats, rightBlackboard?.Floats) &&
                   AreDictionariesEqual(leftBlackboard?.Ints, rightBlackboard?.Ints, EqualityComparer<int>.Default) &&
                   AreDictionariesEqual(leftBlackboard?.Bools, rightBlackboard?.Bools, EqualityComparer<bool>.Default) &&
                   AreDictionariesEqual(left.DelayRemainingFrames, right.DelayRemainingFrames, EqualityComparer<int>.Default);
        }

        private static bool AreFloatDictionariesEqual(
            IReadOnlyDictionary<string, float> left,
            IReadOnlyDictionary<string, float> right)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            if (leftCount == 0)
            {
                return true;
            }

            foreach (KeyValuePair<string, float> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out float value) ||
                    BitConverter.SingleToInt32Bits(pair.Value) != BitConverter.SingleToInt32Bits(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreDictionariesEqual<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> left,
            IReadOnlyDictionary<TKey, TValue> right,
            IEqualityComparer<TValue> comparer)
        {
            int leftCount = left?.Count ?? 0;
            int rightCount = right?.Count ?? 0;
            if (leftCount != rightCount)
            {
                return false;
            }

            if (leftCount == 0)
            {
                return true;
            }

            foreach (KeyValuePair<TKey, TValue> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out TValue value) || !comparer.Equals(pair.Value, value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreBuffsEqual(IReadOnlyList<BuffState> left, IReadOnlyList<BuffState> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                BuffState leftBuff = left[i];
                BuffState rightBuff = right[i];
                if (leftBuff.RuntimeBuffId != rightBuff.RuntimeBuffId ||
                    leftBuff.BuffId != rightBuff.BuffId ||
                    leftBuff.CasterId != rightBuff.CasterId ||
                    leftBuff.TargetId != rightBuff.TargetId ||
                    leftBuff.StackCount != rightBuff.StackCount ||
                    leftBuff.RemainingFrames != rightBuff.RemainingFrames ||
                    leftBuff.AppliedFrame != rightBuff.AppliedFrame ||
                    leftBuff.Flags != rightBuff.Flags)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreNumericSnapshotsEqual(NumericModifierSnapshot left, NumericModifierSnapshot right)
        {
            if (!AreAttributesEqual(left.BaseAttributes, right.BaseAttributes) ||
                left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Modifiers.Count; i++)
            {
                NumericModifier leftModifier = left.Modifiers[i];
                NumericModifier rightModifier = right.Modifiers[i];
                if (leftModifier.SourceBuffId != rightModifier.SourceBuffId ||
                    leftModifier.ValueType != rightModifier.ValueType ||
                    leftModifier.AttributeKind != rightModifier.AttributeKind ||
                    leftModifier.Value != rightModifier.Value)
                {
                    return false;
                }
            }

            return true;
        }

        private static string FormatBuffs(IReadOnlyList<BuffState> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return "[]";
            }

            List<string> values = new List<string>(buffs.Count);
            for (int i = 0; i < buffs.Count; i++)
            {
                BuffState buff = buffs[i];
                values.Add($"{buff.RuntimeBuffId}:{buff.BuffId}:{buff.StackCount}:{buff.RemainingFrames}");
            }

            return $"[{string.Join(",", values)}]";
        }

        private void ProcessBuffCommands(uint frameIndex)
        {
            while (_pendingApplyBuffCommands.Count > 0 && _pendingApplyBuffCommands[0].FrameIndex <= frameIndex)
            {
                ApplyBuffCommand command = _pendingApplyBuffCommands[0];
                _pendingApplyBuffCommands.RemoveAt(0);
                if (_worldState.TryGetPlayer(command.TargetId, out PlayerState targetState))
                {
                    BuffSystem.AddBuff(targetState, command, _buffConfigProvider);
                }

                _applyBuffCommandPool.Return(command);
            }

            while (_pendingRemoveBuffCommands.Count > 0 && _pendingRemoveBuffCommands[0].FrameIndex <= frameIndex)
            {
                RemoveBuffCommand command = _pendingRemoveBuffCommands[0];
                _pendingRemoveBuffCommands.RemoveAt(0);
                if (_worldState.TryGetPlayer(command.TargetId, out PlayerState targetState))
                {
                    BuffSystem.RemoveBuff(targetState, command);
                }

                _removeBuffCommandPool.Return(command);
            }
        }

        private void RecalculateNumericStates()
        {
            foreach (PlayerState player in _worldState.Players)
            {
                player.Numeric.Recalculate(player);
            }
        }

        private void ApplyBuffTicks(uint frameIndex)
        {
            foreach (PlayerState player in _worldState.Players)
            {
                BuffSystem.ApplyTick(player, frameIndex);
            }
        }

        private void ClearPendingBuffCommands()
        {
            for (int i = 0; i < _pendingApplyBuffCommands.Count; i++)
            {
                _applyBuffCommandPool.Return(_pendingApplyBuffCommands[i]);
            }

            _pendingApplyBuffCommands.Clear();

            for (int i = 0; i < _pendingRemoveBuffCommands.Count; i++)
            {
                _removeBuffCommandPool.Return(_pendingRemoveBuffCommands[i]);
            }

            _pendingRemoveBuffCommands.Clear();
        }

        private void InsertApplyCommand(ApplyBuffCommand command)
        {
            int insertIndex = _pendingApplyBuffCommands.Count;
            for (int i = 0; i < _pendingApplyBuffCommands.Count; i++)
            {
                if (Compare(_pendingApplyBuffCommands[i], command) > 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            _pendingApplyBuffCommands.Insert(insertIndex, command);
        }

        private void InsertRemoveCommand(RemoveBuffCommand command)
        {
            int insertIndex = _pendingRemoveBuffCommands.Count;
            for (int i = 0; i < _pendingRemoveBuffCommands.Count; i++)
            {
                if (Compare(_pendingRemoveBuffCommands[i], command) > 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            _pendingRemoveBuffCommands.Insert(insertIndex, command);
        }

        private static int Compare(ApplyBuffCommand left, ApplyBuffCommand right)
        {
            int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
            if (byFrame != 0)
            {
                return byFrame;
            }

            int byTarget = left.TargetId.CompareTo(right.TargetId);
            if (byTarget != 0)
            {
                return byTarget;
            }

            return left.BuffId.CompareTo(right.BuffId);
        }

        private static int Compare(RemoveBuffCommand left, RemoveBuffCommand right)
        {
            int byFrame = left.FrameIndex.CompareTo(right.FrameIndex);
            if (byFrame != 0)
            {
                return byFrame;
            }

            int byTarget = left.TargetId.CompareTo(right.TargetId);
            if (byTarget != 0)
            {
                return byTarget;
            }

            return left.RuntimeBuffId.CompareTo(right.RuntimeBuffId);
        }
    }

    internal readonly struct SelfPrediction
    {
        public SelfPrediction(PlayerStateSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public PlayerStateSnapshot Snapshot { get; }
        public Fixed64 X => Snapshot.X;
        public Fixed64 Y => Snapshot.Y;
        public PlayerAttributeSnapshot Attributes => Snapshot.Attributes;
    }

    internal readonly struct BufferedInput
    {
        public BufferedInput(Fixed64 dx, Fixed64 dy, int skillId)
        {
            Dx = dx;
            Dy = dy;
            SkillId = skillId;
        }

        public Fixed64 Dx { get; }
        public Fixed64 Dy { get; }
        public int SkillId { get; }
    }

    internal readonly struct PendingAuthoritativeSnapshot
    {
        public PendingAuthoritativeSnapshot(BattleWorldSnapshot snapshot, uint selfLatestAcceptedInputFrame)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            SelfLatestAcceptedInputFrame = selfLatestAcceptedInputFrame;
        }

        public BattleWorldSnapshot Snapshot { get; }
        public uint SelfLatestAcceptedInputFrame { get; }
    }

    public readonly struct TickResult
    {
        public TickResult(
            bool snapshotApplied,
            uint lastAppliedFrame,
            int catchUpFrames,
            uint targetFrameExclusive = 0,
            bool consistencyMismatch = false)
        {
            SnapshotApplied = snapshotApplied;
            LastAppliedFrame = lastAppliedFrame;
            CatchUpFrames = catchUpFrames;
            TargetFrameExclusive = targetFrameExclusive;
            ConsistencyMismatch = consistencyMismatch;
        }

        public bool SnapshotApplied { get; }
        public uint LastAppliedFrame { get; }
        public int CatchUpFrames { get; }
        public uint TargetFrameExclusive { get; }
        public bool ConsistencyMismatch { get; }
    }
}
