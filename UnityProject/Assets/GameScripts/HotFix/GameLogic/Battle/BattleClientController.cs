using System;
using System.Collections.Generic;
using Fantasy;
using Fantasy.Async;
using Fantasy.Network.Interface;
using FixedMathSharp;
using GameLogic.FrameSync;
using GameShared.InputBuffering;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Network;
using GameShared.FrameSync.Snapshot;
using GameShared.SkillGraph;
using TEngine;
using UnityEngine;
using UnityEngine.Rendering;
using Log = TEngine.Log;

namespace GameLogic
{
    public sealed class BattleClientController : MonoBehaviour, ITickable
    {
        private const string BattleServerAddress = "127.0.0.1";
        private const int BattleServerPort = 20101;
        private const int SkillInputBufferFrames = 2;
#if BATTLE_PREDICTION_SELF_TEST
        private static bool s_predictionSelfTestExecuted;
#endif

        private readonly Dictionary<long, GameObject> _playerSpheres = new Dictionary<long, GameObject>();
        private readonly Dictionary<long, PlayerAttributeSnapshot> _authoritativeAttributesByPlayerId = new Dictionary<long, PlayerAttributeSnapshot>();
        private readonly Dictionary<long, AuthoritativeBuffBaseline> _authoritativeBuffsByPlayerId = new Dictionary<long, AuthoritativeBuffBaseline>();
        private readonly HashSet<long> _activePlayers = new HashSet<long>();
        private readonly Dictionary<long, RenderTarget> _renderTargetsByPlayerId = new Dictionary<long, RenderTarget>();
        private readonly HashSet<long> _authoritativePlayersInSnapshot = new HashSet<long>();
        private readonly HashSet<long> _playersAwaitingBuffFullSync = new HashSet<long>();
        private readonly List<long> _staleAuthoritativePlayers = new List<long>();
        private readonly List<long> _staleRenderTargetPlayerIds = new List<long>();
        private readonly InputBuffer<BufferedInputKind, int> _inputBuffer = new InputBuffer<BufferedInputKind, int>();

        private ClientTickDriver _tickDriver;
        private BattleSimulation _simulation;
        private IBattleClock _battleClock;
        private BattleNetworkGate _networkGate;
        private Action<IMessage> _snapshotHandler;
        private Action<IMessage> _pongHandler;
        private Action<IMessage> _bandwidthStatsHandler;
        private bool _snapshotRegistered;
        private bool _pongRegistered;
        private bool _bandwidthStatsRegistered;
        private bool _isInitialized;
        private bool _joinSucceeded;
        private string _joinFailureReason = string.Empty;
        private int _snapshotMessageCount;
        private int _pongMessageCount;
        private float _cachedDx;
        private float _cachedDy;
        private IBattleAutomationInputSource _automationInputSource;
        private uint _nextStatusLogFrame;
        private GameObject _authoritativeGhostSphere;
        private bool _hasAuthoritativeGhostTarget;
        private Fixed64 _authoritativeGhostTargetX;
        private Fixed64 _authoritativeGhostTargetY;

        public int Priority => 0;

        /// <summary>
        /// 最近一次收到的服务端带宽统计快照（值拷贝，避免消息对象被回收后失效）。
        /// <see cref="HasBandwidthStats"/> 为 false 时本结构无意义。
        /// </summary>
        public BandwidthStatsSnapshot LatestBandwidthStats { get; private set; }

        /// <summary>是否已收到过至少一次带宽统计上报。</summary>
        public bool HasBandwidthStats { get; private set; }

        public PredictionErrorSnapshot LatestPredictionError { get; private set; }

        public bool HasPredictionError { get; private set; }

        private enum BufferedInputKind
        {
            Skill = 1
        }

        public void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            EnsureTickDriver();
            EnsureSimulation();
            RegisterSnapshotHandler();
            JoinBattleAsync().Coroutine();
        }

        public void DisposeController()
        {
            if (_tickDriver != null)
            {
                _tickDriver.Dispatcher.Unregister(this);
                _tickDriver = null;
            }

            if (_snapshotRegistered && _snapshotHandler != null)
            {
                GameClient.Instance.UnRegisterMsgHandler(OuterOpcode.S2C_FrameSnapshot, _snapshotHandler);
            }

            if (_pongRegistered && _pongHandler != null)
            {
                GameClient.Instance.UnRegisterMsgHandler(OuterOpcode.S2C_Pong, _pongHandler);
            }

            if (_bandwidthStatsRegistered && _bandwidthStatsHandler != null)
            {
                GameClient.Instance.UnRegisterMsgHandler(OuterOpcode.S2C_BandwidthStats, _bandwidthStatsHandler);
            }

            _snapshotRegistered = false;
            _pongRegistered = false;
            _bandwidthStatsRegistered = false;
            _snapshotHandler = null;
            _pongHandler = null;
            _bandwidthStatsHandler = null;
            _networkGate?.ClearPending();
            _networkGate = null;
            _battleClock = null;
            _simulation = null;
            _isInitialized = false;
            _joinSucceeded = false;
            _joinFailureReason = string.Empty;
            _snapshotMessageCount = 0;
            _pongMessageCount = 0;
            _cachedDx = 0.0f;
            _cachedDy = 0.0f;
            _nextStatusLogFrame = 0u;
            _inputBuffer.Clear();
            _automationInputSource = null;
            LatestBandwidthStats = default;
            HasBandwidthStats = false;
            LatestPredictionError = default;
            HasPredictionError = false;
            _hasAuthoritativeGhostTarget = false;
            _authoritativeGhostTargetX = Fixed64.Zero;
            _authoritativeGhostTargetY = Fixed64.Zero;

            if (_authoritativeGhostSphere != null)
            {
                Destroy(_authoritativeGhostSphere);
                _authoritativeGhostSphere = null;
            }

            foreach (KeyValuePair<long, GameObject> pair in _playerSpheres)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            _playerSpheres.Clear();
            _activePlayers.Clear();
            _renderTargetsByPlayerId.Clear();
            _staleRenderTargetPlayerIds.Clear();
            _authoritativeAttributesByPlayerId.Clear();
            _authoritativeBuffsByPlayerId.Clear();
            _authoritativePlayersInSnapshot.Clear();
            _playersAwaitingBuffFullSync.Clear();
            _staleAuthoritativePlayers.Clear();
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                return;
            }

            ReadKeyboardDirection(out _cachedDx, out _cachedDy);
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.J))
            {
                int skillId = BattleSkillGraphLibrary.ResolveConfiguredSkillId();
                if (skillId > 0)
                {
                    _inputBuffer.Record(BufferedInputKind.Skill, skillId, SkillInputBufferFrames);
                }
            }

            UpdateRendering();
        }

        public void Tick(uint frameIndex, Fixed64 fixedDt)
        {
            long nowMs = _battleClock?.NowMs ?? 0L;
            _networkGate?.PumpDownlink(nowMs);

            if (_simulation == null || !_simulation.IsJoined)
            {
                return;
            }

            float dx = _cachedDx;
            float dy = _cachedDy;
            if (_automationInputSource != null &&
                _automationInputSource.TryGetInput(frameIndex, out float automationDx, out float automationDy))
            {
                dx = automationDx;
                dy = automationDy;
            }

            int skillId = 0;
            if (_automationInputSource != null &&
                _automationInputSource.TryGetSkillRequest(frameIndex, out int automationSkillId))
            {
                skillId = automationSkillId;
            }
            else
            {
                _inputBuffer.TryConsume(BufferedInputKind.Skill, out skillId);
            }

            TickResult tickResult = _simulation.Tick(frameIndex, fixedDt, (Fixed64)dx, (Fixed64)dy, skillId);
            _networkGate?.PumpUplink(_battleClock?.NowMs ?? nowMs);
            _inputBuffer.TickDecay();
            SyncRendering();
            LogP1FrameStatus(frameIndex);

            if (tickResult.TargetFrameExclusive > 0 && _tickDriver != null)
            {
                _tickDriver.SetTargetFrame(tickResult.TargetFrameExclusive);
            }
        }

        public void RollBack(uint targetFrame)
        {
            _simulation?.RollBack(targetFrame);
        }

        public void SetAutomationInputSource(IBattleAutomationInputSource inputSource)
        {
            _automationInputSource = inputSource;
        }

        public BattleAutomationClientSnapshot CaptureAutomationSnapshot(string clientId)
        {
            NetworkConditionConfig networkConfig = _networkGate?.Config ?? NetworkConditionConfig.Disabled;
            BattleWorldState worldState = _tickDriver?.WorldState;
            List<BattleAutomationPlayerSnapshot> players = new List<BattleAutomationPlayerSnapshot>();
            int totalActiveBuffCount = 0;
            if (worldState != null)
            {
                foreach (PlayerState player in worldState.Players)
                {
                    bool isSelf = _simulation != null && player.PlayerId == _simulation.SelfPlayerId;
                    BattleAutomationBuffSnapshot[] activeBuffs = BuildAutomationBuffSnapshots(player.ActiveBuffs);
                    totalActiveBuffCount += activeBuffs.Length;
                    players.Add(new BattleAutomationPlayerSnapshot
                    {
                        playerId = player.PlayerId,
                        isSelf = isSelf,
                        x = (float)player.X,
                        y = (float)player.Y,
                        health = player.Health,
                        maxHealth = player.MaxHealth,
                        mana = player.Mana,
                        maxMana = player.MaxMana,
                        attack = player.Attack,
                        activeBuffCount = activeBuffs.Length,
                        activeBuffs = activeBuffs,
                        nextRuntimeBuffId = player.NextRuntimeBuffId,
                        numericModifierCount = player.Numeric.Count
                    });
                }
            }

            return new BattleAutomationClientSnapshot
            {
                clientId = clientId ?? string.Empty,
                joined = _joinSucceeded && _simulation != null && _simulation.IsJoined,
                joinFailureReason = _joinFailureReason ?? string.Empty,
                selfPlayerId = _simulation?.SelfPlayerId ?? 0L,
                localFrame = (int)(_simulation?.LocalFrame ?? 0u),
                lastAppliedFrame = (int)(_simulation?.LastAppliedFrame ?? 0u),
                leadFrames = (int)(_simulation?.LeadFrames ?? 0u),
                activePlayerCount = players.Count,
                snapshotMessageCount = _snapshotMessageCount,
                pongMessageCount = _pongMessageCount,
                consistencyChecked = _simulation?.ConsistencyChecked ?? 0,
                consistencyHits = _simulation?.ConsistencyHits ?? 0,
                consistencyMisses = _simulation?.ConsistencyMisses ?? 0,
                hashReportsSent = _simulation?.HashReportsSent ?? 0,
                consistencySkippedNoRecord = _simulation?.ConsistencySkippedNoRecord ?? 0,
                consistencySkippedEvicted = _simulation?.ConsistencySkippedEvicted ?? 0,
                rollbackCount = _simulation?.RollbackCount ?? 0,
                lastRollbackReplayFrames = _simulation?.LastRollbackReplayFrames ?? 0,
                lastRollbackElapsedMs = (float)(_simulation?.LastRollbackElapsedMs ?? 0.0d),
                networkSimulationEnabled = networkConfig.IsEnabled,
                networkUplinkDelayMs = networkConfig.UplinkDelayMs,
                networkDownlinkDelayMs = networkConfig.DownlinkDelayMs,
                networkUplinkJitterMs = networkConfig.UplinkJitterMs,
                networkDownlinkJitterMs = networkConfig.DownlinkJitterMs,
                networkUplinkLossPercent = networkConfig.UplinkLossPercent,
                networkDownlinkLossPercent = networkConfig.DownlinkLossPercent,
                networkSeed = networkConfig.Seed,
                networkUplinkSent = _networkGate?.UplinkSent ?? 0L,
                networkUplinkDropped = _networkGate?.UplinkDropped ?? 0L,
                networkDownlinkDelivered = _networkGate?.DownlinkDelivered ?? 0L,
                networkDownlinkDropped = _networkGate?.DownlinkDropped ?? 0L,
                networkMaxQueueDepth = _networkGate?.MaxQueueDepth ?? 0,
                networkOverflowDropped = _networkGate?.OverflowDropped ?? 0L,
                totalActiveBuffCount = totalActiveBuffCount,
                players = players.ToArray()
            };
        }

        private void OnDestroy()
        {
            DisposeController();
        }

        private void EnsureTickDriver()
        {
            _tickDriver = FindObjectOfType<ClientTickDriver>();
            if (_tickDriver == null)
            {
                GameObject tickDriverObject = new GameObject("ClientTickDriver");
                _tickDriver = tickDriverObject.AddComponent<ClientTickDriver>();
            }

            _tickDriver.Dispatcher.Register(this);
        }

        private void EnsureSimulation()
        {
            if (_simulation != null || _tickDriver == null)
            {
                return;
            }

            _battleClock = SystemBattleClock.Instance;
            _networkGate = new BattleNetworkGate(BattleAutomationConfig.Current.NetworkCondition, _battleClock);
            BattleInputSender sendInput = _networkGate.WrapSendInput(SendInputCommand);
            Action<ulong> sendPing = _networkGate.WrapSendPing(SendPingCommand);
            Action<uint, ulong> sendHashReport = _networkGate.WrapSendHashReport(SendHashReportCommand);
            _simulation = new BattleSimulation(
                _tickDriver.WorldState,
                sendInput,
                sendPing,
                sendHashReport,
                _tickDriver.Logger,
                clock: _battleClock);

#if BATTLE_PREDICTION_SELF_TEST
            if (s_predictionSelfTestExecuted)
            {
                return;
            }

            s_predictionSelfTestExecuted = true;
            string failedCase;
            bool passed = BattleSimulation.RunSelfTest(out failedCase);
            if (passed)
            {
                Log.Info("[PredictSelfTest] ALL PASS");
            }
            else
            {
                Log.Warning($"[PredictSelfTest] FAIL: {failedCase}");
            }
#endif
        }

        private async FTask JoinBattleAsync()
        {
            string battleServerAddress = BattleAutomationConfig.Current.Enabled
                ? BattleAutomationConfig.Current.BattleServerAddress
                : BattleServerAddress;
            int battleServerPort = BattleAutomationConfig.Current.Enabled
                ? BattleAutomationConfig.Current.BattleServerPort
                : BattleServerPort;
            bool connected = await DataCenterSys.Instance.ConnectBattle(battleServerAddress, battleServerPort);
            if (!connected)
            {
                _joinFailureReason = $"connect-battle-failed:{battleServerAddress}:{battleServerPort}";
                return;
            }

            C2B_JoinBattleResponse response = (C2B_JoinBattleResponse)await GameClient.Instance.Call(new C2B_JoinBattle());
            if (response == null)
            {
                _joinFailureReason = "join-battle-response-null";
                Log.Warning("[Battle] JoinBattle response is null.");
                return;
            }

            if (response.ErrorCode != 0)
            {
                _joinFailureReason = $"join-battle-error:{response.ErrorCode}";
                Log.Warning($"[Battle] JoinBattle failed, ErrorCode={response.ErrorCode}");
                return;
            }

            _joinSucceeded = true;
            _joinFailureReason = string.Empty;
            _simulation?.SetJoined(
                response.PlayerId,
                response.ServerFrameIndex,
                Fixed64.FromRaw(response.XRaw),
                Fixed64.FromRaw(response.YRaw));
            if (_tickDriver != null && _simulation != null)
            {
                uint alignedFrame = _simulation.InitialAlignedFrame;
                _simulation.AlignLocalFrame(alignedFrame);
                _tickDriver.AlignToFrame(alignedFrame);
                Log.Info(
                    $"[Battle] Join aligned. player={response.PlayerId} serverFrame={response.ServerFrameIndex} localFrame={alignedFrame} lead={_simulation.LeadFrames}");
            }

            SyncRendering();
        }

        private void RegisterSnapshotHandler()
        {
            if (_snapshotRegistered)
            {
                return;
            }

            _snapshotHandler = OnSnapshotMessage;
            _pongHandler = OnPongMessage;
            _bandwidthStatsHandler = OnBandwidthStatsMessage;
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_FrameSnapshot, _snapshotHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_Pong, _pongHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_BandwidthStats, _bandwidthStatsHandler);
            _snapshotRegistered = true;
            _pongRegistered = true;
            _bandwidthStatsRegistered = true;
        }

        private void OnSnapshotMessage(IMessage message)
        {
            if (message is not S2C_FrameSnapshot snapshot)
            {
                return;
            }

            long nowMs = _battleClock?.NowMs ?? SystemBattleClock.Instance.NowMs;
            if (_networkGate != null && !_networkGate.TryAcceptSnapshotMessage(nowMs))
            {
                return;
            }

            _snapshotMessageCount++;
            BattleWorldSnapshot authoritativeSnapshot = ConvertSnapshot(snapshot, out uint selfLatestAcceptedInputFrame);
            if (_networkGate == null)
            {
                _simulation?.EnqueueServerSnapshot(authoritativeSnapshot, selfLatestAcceptedInputFrame);
                return;
            }

            _networkGate.EnqueueConvertedSnapshot(
                nowMs,
                authoritativeSnapshot,
                value => _simulation?.EnqueueServerSnapshot(value, selfLatestAcceptedInputFrame));
        }

        private void OnPongMessage(IMessage message)
        {
            if (message is not S2C_Pong pong)
            {
                return;
            }

            ulong sendTimestampMs = pong.SendTimestampMs;
            long nowMs = _battleClock?.NowMs ?? SystemBattleClock.Instance.NowMs;
            if (_networkGate == null)
            {
                ProcessPong(sendTimestampMs);
                return;
            }

            if (_networkGate.TryAcceptPong(nowMs, sendTimestampMs, ProcessPong))
            {
                _pongMessageCount++;
            }
        }

        private void ProcessPong(ulong sendTimestampMs)
        {
            long nowMs = _battleClock?.NowMs ?? SystemBattleClock.Instance.NowMs;
            long rttMs = nowMs - checked((long)sendTimestampMs);
            if (rttMs >= 0)
            {
                _simulation?.ProcessPong(rttMs);
            }
        }

        private void OnBandwidthStatsMessage(IMessage message)
        {
            if (message is not S2C_BandwidthStats stats)
            {
                return;
            }

            // 消息回调返回后会被对象池回收，必须在这里把字段值拷出来。
            LatestBandwidthStats = new BandwidthStatsSnapshot
            {
                FrameIndex = stats.FrameIndex,
                MeasureFullSyncBaseline = stats.MeasureFullSyncBaseline,
                HasSamples = stats.HasSamples,
                ActualPayloadBytes = stats.ActualPayloadBytes,
                FullSyncPayloadBytes = stats.FullSyncPayloadBytes,
                DirtySyncSavedRatio = stats.DirtySyncSavedRatio,
                DirtySyncSavedBytes = stats.DirtySyncSavedBytes
            };
            HasBandwidthStats = true;
            GameEvent.Get<IBattleUI>().OnBandwidthStatsUpdated();
        }

        private void SyncRendering()
        {
            BattleWorldState worldState = _tickDriver?.WorldState;
            if (worldState == null || _simulation == null)
            {
                return;
            }

            _activePlayers.Clear();
            foreach (PlayerState player in worldState.Players)
            {
                _activePlayers.Add(player.PlayerId);
                _renderTargetsByPlayerId[player.PlayerId] = new RenderTarget(player.X, player.Y);
                bool isSelf = player.PlayerId == _simulation.SelfPlayerId;
                GameObject sphere = GetOrCreateSphere(player.PlayerId, isSelf);
                sphere.SetActive(true);
            }

            _staleRenderTargetPlayerIds.Clear();
            foreach (long playerId in _renderTargetsByPlayerId.Keys)
            {
                if (!_activePlayers.Contains(playerId))
                {
                    _staleRenderTargetPlayerIds.Add(playerId);
                }
            }

            for (int i = 0; i < _staleRenderTargetPlayerIds.Count; i++)
            {
                _renderTargetsByPlayerId.Remove(_staleRenderTargetPlayerIds[i]);
            }

            foreach (KeyValuePair<long, GameObject> pair in _playerSpheres)
            {
                if (_activePlayers.Contains(pair.Key))
                {
                    continue;
                }

                if (pair.Value != null)
                {
                    pair.Value.SetActive(false);
                }
            }

            SyncAuthoritativeGhost();
        }

        private void UpdateRendering()
        {
            if (_simulation == null || !_simulation.IsJoined)
            {
                return;
            }

            _simulation.AdvancePredictionErrorSmoothing(Time.deltaTime);
            foreach (KeyValuePair<long, RenderTarget> pair in _renderTargetsByPlayerId)
            {
                if (!_playerSpheres.TryGetValue(pair.Key, out GameObject sphere) ||
                    sphere == null ||
                    !sphere.activeSelf)
                {
                    continue;
                }

                Vector3 position = ToWorldPosition(pair.Value.X, pair.Value.Y);
                if (pair.Key == _simulation.SelfPlayerId)
                {
                    position.x += _simulation.RenderErrorOffsetX;
                    position.z += _simulation.RenderErrorOffsetY;
                }

                sphere.transform.position = position;
                if (pair.Key == _simulation.SelfPlayerId)
                {
                    _simulation.RecordRenderedSelfPosition(position.x, position.z);
                }
            }

            if (_hasAuthoritativeGhostTarget && _authoritativeGhostSphere != null)
            {
                _authoritativeGhostSphere.transform.position = ToWorldPosition(
                    _authoritativeGhostTargetX,
                    _authoritativeGhostTargetY);
            }

            LatestPredictionError = new PredictionErrorSnapshot
            {
                LastCorrectionMagnitude = _simulation.LastPredictionCorrectionMagnitude,
                SmoothingRemainingSeconds = _simulation.PredictionSmoothingRemainingSeconds,
                RollbackCount = _simulation.RollbackCount,
                LastRollbackFrame = _simulation.LastRollbackFrame
            };
            HasPredictionError = true;
            GameEvent.Get<IBattleUI>().OnPredictionErrorUpdated();
        }

        private void SyncAuthoritativeGhost()
        {
            if (_simulation == null ||
                !_activePlayers.Contains(_simulation.SelfPlayerId) ||
                !_simulation.TryGetLatestAuthoritativeSelfPosition(
                    out _authoritativeGhostTargetX,
                    out _authoritativeGhostTargetY))
            {
                _hasAuthoritativeGhostTarget = false;
                if (_authoritativeGhostSphere != null)
                {
                    _authoritativeGhostSphere.SetActive(false);
                }

                return;
            }

            _hasAuthoritativeGhostTarget = true;
            GetOrCreateAuthoritativeGhostSphere().SetActive(true);
        }

        private BattleWorldSnapshot ConvertSnapshot(S2C_FrameSnapshot snapshot, out uint selfLatestAcceptedInputFrame)
        {
            selfLatestAcceptedInputFrame = 0;
            PlayerStateSnapshot[] players = new PlayerStateSnapshot[snapshot.Players.Count];
            PhysicsBodySnapshot[] bodies = new PhysicsBodySnapshot[snapshot.Players.Count];
            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerSnapshot player = snapshot.Players[i];
                _authoritativePlayersInSnapshot.Add(player.PlayerId);
                PlayerAttributeSnapshot baselineAttributes = _authoritativeAttributesByPlayerId.TryGetValue(
                    player.PlayerId,
                    out PlayerAttributeSnapshot cachedAttributes)
                    ? cachedAttributes
                    : PlayerAttributeSnapshot.Default;
                PlayerAttributeSnapshot mergedAttributes = PlayerAttributeSync.Merge(
                    baselineAttributes,
                    (PlayerAttributeDirtyFlags)player.AttributeDirtyMask,
                    player.Health,
                    player.MaxHealth,
                    player.Mana,
                    player.MaxMana,
                    player.Attack);
                ResolveAuthoritativeBuffSnapshot(snapshot.FrameIndex, player, out BuffState[] authoritativeBuffs, out long nextRuntimeBuffId);
                _authoritativeAttributesByPlayerId[player.PlayerId] = mergedAttributes;
                players[i] = new PlayerStateSnapshot(
                    player.PlayerId,
                    Fixed64.FromRaw(player.XRaw),
                    Fixed64.FromRaw(player.YRaw),
                    mergedAttributes,
                    authoritativeBuffs,
                    nextRuntimeBuffId,
                    BuildNumericSnapshot(player.Numeric, mergedAttributes));
                bodies[i] = new PhysicsBodySnapshot(
                    checked((int)player.PlayerId),
                    Fixed64.FromRaw(player.XRaw),
                    Fixed64.FromRaw(player.YRaw),
                    Fixed64.Zero,
                    Fixed64.FromRaw(player.LinearVelocityXRaw),
                    Fixed64.FromRaw(player.LinearVelocityYRaw),
                    Fixed64.Zero,
                    true,
                    true);
                if (_simulation != null && player.PlayerId == _simulation.SelfPlayerId)
                {
                    selfLatestAcceptedInputFrame = player.LatestAcceptedInputFrame;
                }
            }

            CleanupStaleAuthoritativeState();

            Array.Sort(players, PlayerSnapshotComparer.Instance);
            Array.Sort(bodies, PhysicsBodySnapshotComparer.Instance);

            PhysicsContactSnapshot[] contacts = new PhysicsContactSnapshot[snapshot.Contacts.Count];
            for (int i = 0; i < snapshot.Contacts.Count; i++)
            {
                FrameContactSnapshot contact = snapshot.Contacts[i];
                contacts[i] = new PhysicsContactSnapshot(contact.BodyAId, contact.BodyBId, contact.IsTouching);
            }

            PhysicsWorldSnapshot physicsSnapshot = new PhysicsWorldSnapshot(bodies, contacts);
            return new BattleWorldSnapshot(snapshot.FrameIndex, players, physicsSnapshot);
        }

        private static BuffState[] BuildBuffStates(IReadOnlyList<BuffSnapshot> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return Array.Empty<BuffState>();
            }

            BuffState[] states = new BuffState[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                BuffSnapshot buff = buffs[i];
                states[i] = new BuffState(
                    buff.RuntimeBuffId,
                    buff.BuffId,
                    buff.CasterId,
                    buff.TargetId,
                    buff.StackCount,
                    buff.RemainingFrames,
                    buff.AppliedFrame,
                    (BuffFlags)buff.Flags);
            }

            return states;
        }

        private static BuffSync.BuffChange[] BuildBuffChanges(IReadOnlyList<BuffSnapshot> buffs)
        {
            if (buffs == null || buffs.Count == 0)
            {
                return Array.Empty<BuffSync.BuffChange>();
            }

            BuffSync.BuffChange[] changes = new BuffSync.BuffChange[buffs.Count];
            for (int i = 0; i < buffs.Count; i++)
            {
                BuffSnapshot buff = buffs[i];
                BuffDirtyFlags dirtyFlags = (BuffDirtyFlags)buff.DirtyFlags;
                if (dirtyFlags == BuffDirtyFlags.None)
                {
                    dirtyFlags = BuffDirtyFlags.Updated;
                }

                changes[i] = new BuffSync.BuffChange(
                    new BuffState(
                        buff.RuntimeBuffId,
                        buff.BuffId,
                        buff.CasterId,
                        buff.TargetId,
                        buff.StackCount,
                        buff.RemainingFrames,
                        buff.AppliedFrame,
                        (BuffFlags)buff.Flags),
                    dirtyFlags);
            }

            return changes;
        }

        private static GameShared.FrameSync.Battle.NumericModifierSnapshot BuildNumericSnapshot(Fantasy.NumericSnapshot numeric, PlayerAttributeSnapshot fallbackAttributes)
        {
            if (numeric == null)
            {
                return GameShared.FrameSync.Battle.NumericModifierSnapshot.FromAttributes(fallbackAttributes);
            }

            NumericModifier[] modifiers = new NumericModifier[numeric.Modifiers.Count];
            for (int i = 0; i < numeric.Modifiers.Count; i++)
            {
                Fantasy.NumericModifierSnapshot modifier = numeric.Modifiers[i];
                modifiers[i] = new NumericModifier(
                    modifier.SourceBuffId,
                    (ModifierValueType)modifier.ValueType,
                    (AttributeKind)modifier.AttributeKind,
                    modifier.Value);
            }

            return new GameShared.FrameSync.Battle.NumericModifierSnapshot(
                new PlayerAttributeSnapshot(
                    numeric.BaseHealth,
                    numeric.BaseMaxHealth,
                    numeric.BaseMana,
                    numeric.BaseMaxMana,
                    numeric.BaseAttack),
                modifiers);
        }

        private void ResolveAuthoritativeBuffSnapshot(
            uint frameIndex,
            PlayerSnapshot player,
            out BuffState[] authoritativeBuffs,
            out long nextRuntimeBuffId)
        {
            if (player.IsBuffFullSync)
            {
                authoritativeBuffs = BuildBuffStates(player.ActiveBuffs);
                nextRuntimeBuffId = NormalizeNextRuntimeBuffId(player.NextRuntimeBuffId, 1L);
                _authoritativeBuffsByPlayerId[player.PlayerId] = new AuthoritativeBuffBaseline(
                    authoritativeBuffs,
                    nextRuntimeBuffId,
                    frameIndex);
                _playersAwaitingBuffFullSync.Remove(player.PlayerId);
                return;
            }

            bool hasBaseline = _authoritativeBuffsByPlayerId.TryGetValue(player.PlayerId, out AuthoritativeBuffBaseline baseline);
            if (player.BuffDirtyMask == 0)
            {
                authoritativeBuffs = hasBaseline
                    ? BuffSync.CopyBuffs(baseline.ActiveBuffs)
                    : Array.Empty<BuffState>();
                nextRuntimeBuffId = hasBaseline
                    ? baseline.NextRuntimeBuffId
                    : 1L;
                return;
            }

            if (!hasBaseline ||
                _playersAwaitingBuffFullSync.Contains(player.PlayerId) ||
                baseline.FrameIndex != player.BuffSnapshotFrameIndex)
            {
                uint localBaselineFrame = hasBaseline ? baseline.FrameIndex : 0u;
                Log.Warning(
                    $"[Battle] Buff delta dropped. player={player.PlayerId} frame={frameIndex} localBase={localBaselineFrame} packetBase={player.BuffSnapshotFrameIndex} hasBaseline={hasBaseline}");
                authoritativeBuffs = hasBaseline
                    ? BuffSync.CopyBuffs(baseline.ActiveBuffs)
                    : Array.Empty<BuffState>();
                nextRuntimeBuffId = hasBaseline
                    ? baseline.NextRuntimeBuffId
                    : 1L;
                _playersAwaitingBuffFullSync.Add(player.PlayerId);
                return;
            }

            BuffSync.BuffChange[] changes = BuildBuffChanges(player.ActiveBuffs);
            authoritativeBuffs = BuffSync.Merge(baseline.ActiveBuffs, changes);
            nextRuntimeBuffId = NormalizeNextRuntimeBuffId(player.NextRuntimeBuffId, baseline.NextRuntimeBuffId);
            _authoritativeBuffsByPlayerId[player.PlayerId] = new AuthoritativeBuffBaseline(
                authoritativeBuffs,
                nextRuntimeBuffId,
                frameIndex);
            _playersAwaitingBuffFullSync.Remove(player.PlayerId);
        }

        private void CleanupStaleAuthoritativeState()
        {
            _staleAuthoritativePlayers.Clear();
            foreach (long playerId in _authoritativeAttributesByPlayerId.Keys)
            {
                if (_authoritativePlayersInSnapshot.Contains(playerId))
                {
                    continue;
                }

                _staleAuthoritativePlayers.Add(playerId);
            }

            for (int i = 0; i < _staleAuthoritativePlayers.Count; i++)
            {
                long playerId = _staleAuthoritativePlayers[i];
                _authoritativeAttributesByPlayerId.Remove(playerId);
                _authoritativeBuffsByPlayerId.Remove(playerId);
                _playersAwaitingBuffFullSync.Remove(playerId);
            }

            _authoritativePlayersInSnapshot.Clear();
        }

        private static long NormalizeNextRuntimeBuffId(long nextRuntimeBuffId, long fallbackValue)
        {
            if (nextRuntimeBuffId > 0)
            {
                return nextRuntimeBuffId;
            }

            return fallbackValue > 0 ? fallbackValue : 1L;
        }

        private static void ReadKeyboardDirection(out float dx, out float dy)
        {
            dx = 0.0f;
            dy = 0.0f;

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                dx -= 1.0f;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                dx += 1.0f;
            }

            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                dy -= 1.0f;
            }

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                dy += 1.0f;
            }
        }

        private static void SendInputCommand(uint frameIndex, uint inputSeq, Fixed64 dx, Fixed64 dy, int skillId)
        {
            GameClient.Instance.Send(new C2B_PlayerInput
            {
                FrameIndex = frameIndex,
                InputSeq = inputSeq,
                DxRaw = dx.m_rawValue,
                DyRaw = dy.m_rawValue,
                SkillId = skillId
            });
        }

        private static void SendPingCommand(ulong sendTimestampMs)
        {
            GameClient.Instance.Send(new C2B_Ping
            {
                SendTimestampMs = sendTimestampMs
            });
        }

        private static void SendHashReportCommand(uint frameIndex, ulong stateHash)
        {
            GameClient.Instance.Send(new C2B_StateHashReport
            {
                FrameIndex = frameIndex,
                StateHash = stateHash
            });
        }

        private void LogP1FrameStatus(uint frameIndex)
        {
            if (_simulation == null || frameIndex < _nextStatusLogFrame)
            {
                return;
            }

            BattleWorldState worldState = _tickDriver?.WorldState;
            if (worldState == null)
            {
                return;
            }

            BattleWorldSnapshot snapshot = worldState.TakeSnapshot().WithFrameIndex(_simulation.LocalFrame);
            ulong stateHash = StateHasher.Hash(snapshot);
            Log.Info(
                $"[Battle][P1] frame={frameIndex} authoritativeFrame={_simulation.LastAppliedFrame} " +
                $"predictedFrame={_simulation.LastPredictedFrame} lead={_simulation.LeadFrames} stateHash=0x{stateHash:X16}");
            _nextStatusLogFrame = unchecked(frameIndex + 30u);
        }

        private GameObject GetOrCreateSphere(long playerId, bool isSelf)
        {
            if (_playerSpheres.TryGetValue(playerId, out GameObject exist) && exist != null)
            {
                return exist;
            }

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = $"GameplaySphere_{playerId}";
            sphere.transform.SetParent(transform, false);
            sphere.transform.position = Vector3.zero;
            float diameter = (float)(GameplayRoomSettings.PlayerRadius * Fixed64.Two);
            sphere.transform.localScale = Vector3.one * diameter;

            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = isSelf ? Color.green : Color.cyan;
            }

            _playerSpheres[playerId] = sphere;
            return sphere;
        }

        private GameObject GetOrCreateAuthoritativeGhostSphere()
        {
            if (_authoritativeGhostSphere != null)
            {
                return _authoritativeGhostSphere;
            }

            GameObject ghost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ghost.name = "GameplayAuthoritativeGhost";
            ghost.transform.SetParent(transform, false);
            ghost.transform.position = Vector3.zero;
            float diameter = (float)(GameplayRoomSettings.PlayerRadius * Fixed64.Two);
            ghost.transform.localScale = Vector3.one * diameter;

            Renderer renderer = ghost.GetComponent<Renderer>();
            if (renderer != null)
            {
                ConfigureGhostMaterial(renderer);
            }

            Collider collider = ghost.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            _authoritativeGhostSphere = ghost;
            return ghost;
        }

        private static void ConfigureGhostMaterial(Renderer renderer)
        {
            Material material = renderer.material;
            material.color = new Color(1.0f, 0.78f, 0.08f, 0.35f);
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3.0f);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1.0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetInt("_ZWrite", 0);
            }

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Vector3 ToWorldPosition(Fixed64 x, Fixed64 y)
        {
            return new Vector3((float)x, (float)GameplayRoomSettings.PlayerRadius, (float)y);
        }

        private static BattleAutomationBuffSnapshot[] BuildAutomationBuffSnapshots(IReadOnlyList<BuffState> activeBuffs)
        {
            if (activeBuffs == null || activeBuffs.Count == 0)
            {
                return Array.Empty<BattleAutomationBuffSnapshot>();
            }

            BattleAutomationBuffSnapshot[] snapshots = new BattleAutomationBuffSnapshot[activeBuffs.Count];
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                BuffState buff = activeBuffs[i];
                snapshots[i] = new BattleAutomationBuffSnapshot
                {
                    runtimeBuffId = buff.RuntimeBuffId,
                    buffId = buff.BuffId,
                    casterId = buff.CasterId,
                    targetId = buff.TargetId,
                    stackCount = buff.StackCount,
                    remainingFrames = buff.RemainingFrames,
                    appliedFrame = (int)buff.AppliedFrame,
                    flags = (uint)buff.Flags
                };
            }

            return snapshots;
        }

        private readonly struct RenderTarget
        {
            public RenderTarget(Fixed64 x, Fixed64 y)
            {
                X = x;
                Y = y;
            }

            public Fixed64 X { get; }
            public Fixed64 Y { get; }
        }

        private sealed class PlayerSnapshotComparer : IComparer<PlayerStateSnapshot>
        {
            public static readonly PlayerSnapshotComparer Instance = new PlayerSnapshotComparer();

            public int Compare(PlayerStateSnapshot x, PlayerStateSnapshot y)
            {
                return x.PlayerId.CompareTo(y.PlayerId);
            }
        }

        private sealed class PhysicsBodySnapshotComparer : IComparer<PhysicsBodySnapshot>
        {
            public static readonly PhysicsBodySnapshotComparer Instance = new PhysicsBodySnapshotComparer();

            public int Compare(PhysicsBodySnapshot x, PhysicsBodySnapshot y)
            {
                return x.BodyId.CompareTo(y.BodyId);
            }
        }

        private readonly struct AuthoritativeBuffBaseline
        {
            public AuthoritativeBuffBaseline(IReadOnlyList<BuffState> activeBuffs, long nextRuntimeBuffId, uint frameIndex)
            {
                ActiveBuffs = BuffSync.CopyBuffs(activeBuffs);
                NextRuntimeBuffId = nextRuntimeBuffId > 0 ? nextRuntimeBuffId : 1L;
                FrameIndex = frameIndex;
            }

            public IReadOnlyList<BuffState> ActiveBuffs { get; }
            public long NextRuntimeBuffId { get; }
            public uint FrameIndex { get; }
        }

        /// <summary>
        /// 服务端带宽统计上报的客户端侧快照（值拷贝自 <see cref="S2C_BandwidthStats"/>，
        /// 因消息对象在回调返回后会被对象池回收，必须拷出来缓存）。
        /// </summary>
        public struct BandwidthStatsSnapshot
        {
            public uint FrameIndex { get; set; }
            public bool MeasureFullSyncBaseline { get; set; }
            public bool HasSamples { get; set; }
            public long ActualPayloadBytes { get; set; }
            public long FullSyncPayloadBytes { get; set; }
            public double DirtySyncSavedRatio { get; set; }
            public long DirtySyncSavedBytes { get; set; }
        }

        public struct PredictionErrorSnapshot
        {
            public float LastCorrectionMagnitude { get; set; }
            public float SmoothingRemainingSeconds { get; set; }
            public int RollbackCount { get; set; }
            public uint LastRollbackFrame { get; set; }
        }
    }
}
