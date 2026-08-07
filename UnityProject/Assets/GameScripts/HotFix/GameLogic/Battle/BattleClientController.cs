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
        private const float StaminaBarWidth = 1.5f;
        private const float StaminaBarHeight = 0.12f;
        private static readonly Color SelfColor = new Color(0.18f, 0.92f, 0.34f);
        private static readonly Color RemoteColor = new Color(0.12f, 0.82f, 0.94f);
        private static readonly Color DashColor = new Color(1.0f, 0.68f, 0.08f);
        private static readonly Color RecoverColor = new Color(0.42f, 0.62f, 0.72f);
        private static readonly Color KnockbackColor = new Color(1.0f, 0.22f, 0.16f);
#if BATTLE_PREDICTION_SELF_TEST
        private static bool s_predictionSelfTestExecuted;
#endif

        private readonly Dictionary<long, GameObject> _playerSpheres = new Dictionary<long, GameObject>();
        private readonly Dictionary<long, AuthoritativeAttributeBaseline> _authoritativeAttributesByPlayerId = new Dictionary<long, AuthoritativeAttributeBaseline>();
        private readonly Dictionary<long, AuthoritativeBuffBaseline> _authoritativeBuffsByPlayerId = new Dictionary<long, AuthoritativeBuffBaseline>();
        private readonly HashSet<long> _activePlayers = new HashSet<long>();
        private readonly Dictionary<long, RenderTarget> _renderTargetsByPlayerId = new Dictionary<long, RenderTarget>();
        private readonly HashSet<long> _authoritativePlayersInSnapshot = new HashSet<long>();
        private readonly HashSet<long> _playersAwaitingBuffFullSync = new HashSet<long>();
        private readonly List<long> _staleAuthoritativePlayers = new List<long>();
        private readonly List<long> _staleRenderTargetPlayerIds = new List<long>();
        private readonly InputBuffer<BufferedInputKind, int> _inputBuffer = new InputBuffer<BufferedInputKind, int>();
        private readonly HashSet<long> _observedSelfDashBuffIds = new HashSet<long>();
        private readonly HashSet<long> _observedSelfKnockbackBuffIds = new HashSet<long>();

        private ClientTickDriver _tickDriver;
        private BattleSimulation _simulation;
        private IBattleClock _battleClock;
        private BattleNetworkGate _networkGate;
        private Action<IMessage> _snapshotHandler;
        private Action<IMessage> _pongHandler;
        private Action<IMessage> _bandwidthStatsHandler;
        private Action<IMessage> _rttProbeHandler;
        private Action<IMessage> _rttStatsHandler;

        private bool _snapshotRegistered;
        private bool _pongRegistered;
        private bool _bandwidthStatsRegistered;
        private bool _rttProbeRegistered;
        private bool _rttStatsRegistered;

        private bool _isInitialized;
        private bool _joinSucceeded;
        private string _joinFailureReason = string.Empty;
        private int _snapshotMessageCount;
        private int _pongMessageCount;
        private int _rttProbeAcksSent;

        private float _cachedDx;
        private float _cachedDy;
        private IBattleAutomationInputSource _automationInputSource;
        private uint _nextStatusLogFrame;
        private GameObject _authoritativeGhostSphere;
        private bool _hasAuthoritativeGhostTarget;
        private Fixed64 _authoritativeGhostTargetX;
        private Fixed64 _authoritativeGhostTargetY;
        private Renderer _authoritativeGhostRenderer;
        private LineRenderer _authoritativeSeparationLine;
        private GameObject _staminaBarRoot;
        private Transform _staminaBarFill;
        private Renderer _staminaBarFillRenderer;
        private int _dashCount;
        private int _knockbackTriggerCount;

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
        public RttStatsSnapshot LatestRttStats { get; private set; }
        public bool HasRttStats { get; private set; }
        public GameplayStatusSnapshot LatestGameplayStatus { get; private set; }
        public bool HasGameplayStatus { get; private set; }
        public int RttProbeAcksSent => _rttProbeAcksSent;


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

            if (_rttProbeRegistered && _rttProbeHandler != null)
            {
                GameClient.Instance.UnRegisterMsgHandler(OuterOpcode.S2C_RttProbe, _rttProbeHandler);
            }

            if (_rttStatsRegistered && _rttStatsHandler != null)
            {
                GameClient.Instance.UnRegisterMsgHandler(OuterOpcode.S2C_RttStats, _rttStatsHandler);
            }

            _snapshotRegistered = false;
            _pongRegistered = false;
            _bandwidthStatsRegistered = false;
            _rttProbeRegistered = false;
            _rttStatsRegistered = false;
            _snapshotHandler = null;
            _pongHandler = null;
            _bandwidthStatsHandler = null;
            _rttProbeHandler = null;
            _rttStatsHandler = null;

            _networkGate?.ClearPending();
            _networkGate = null;
            _battleClock = null;
            _simulation = null;
            _isInitialized = false;
            _joinSucceeded = false;
            _joinFailureReason = string.Empty;
            _snapshotMessageCount = 0;
            _pongMessageCount = 0;
            _rttProbeAcksSent = 0;

            _cachedDx = 0.0f;
            _cachedDy = 0.0f;
            _nextStatusLogFrame = 0u;
            _inputBuffer.Clear();
            _automationInputSource = null;
            LatestBandwidthStats = default;
            HasBandwidthStats = false;
            LatestPredictionError = default;
            HasPredictionError = false;
            LatestRttStats = default;
            HasRttStats = false;
            LatestGameplayStatus = default;
            HasGameplayStatus = false;
            _dashCount = 0;
            _knockbackTriggerCount = 0;
            _observedSelfDashBuffIds.Clear();
            _observedSelfKnockbackBuffIds.Clear();

            _hasAuthoritativeGhostTarget = false;
            _authoritativeGhostTargetX = Fixed64.Zero;
            _authoritativeGhostTargetY = Fixed64.Zero;

            if (_authoritativeGhostSphere != null)
            {
                Destroy(_authoritativeGhostSphere);
                _authoritativeGhostSphere = null;
                _authoritativeGhostRenderer = null;
            }

            if (_authoritativeSeparationLine != null)
            {
                Destroy(_authoritativeSeparationLine.gameObject);
                _authoritativeSeparationLine = null;
            }

            if (_staminaBarRoot != null)
            {
                Destroy(_staminaBarRoot);
                _staminaBarRoot = null;
                _staminaBarFill = null;
                _staminaBarFillRenderer = null;
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
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                _inputBuffer.Record(
                    BufferedInputKind.Skill,
                    DashTuning.DashSkillId,
                    SkillInputBufferFrames);
            }
            else if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.J))
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
            HashSet<long> capturedPlayerIds = new HashSet<long>();
            int totalActiveBuffCount = 0;
            int staminaAtEnd = 0;
            if (worldState != null)
            {
                foreach (PlayerState player in worldState.Players)
                {
                    if (!capturedPlayerIds.Add(player.PlayerId))
                    {
                        continue;
                    }

                    bool isSelf = _simulation != null && player.PlayerId == _simulation.SelfPlayerId;
                    BattleAutomationBuffSnapshot[] activeBuffs = BuildAutomationBuffSnapshots(player.ActiveBuffs);
                    totalActiveBuffCount += activeBuffs.Length;
                    int recoverRemainingFrames = GetBuffRemainingFrames(player.ActiveBuffs, DashTuning.RecoverBuffId);
                    if (isSelf)
                    {
                        staminaAtEnd = player.Stamina;
                        ObserveSelfGameplayEpochs(player.DashRuntimeBuffId, player.KnockbackRuntimeBuffId);
                    }

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
                        stamina = player.Stamina,
                        maxStamina = player.MaxStamina,
                        staminaRegenCounterFrames = player.StaminaRegenCounterFrames,
                        dashRemainingFrames = player.DashRemainingFrames,
                        dashRuntimeBuffId = player.DashRuntimeBuffId,
                        recoverRemainingFrames = recoverRemainingFrames,
                        knockbackRemainingFrames = player.KnockbackRemainingFrames,
                        knockbackRuntimeBuffId = player.KnockbackRuntimeBuffId,
                        activeBuffCount = activeBuffs.Length,
                        activeBuffs = activeBuffs,
                        nextRuntimeBuffId = player.NextRuntimeBuffId,
                        numericModifierCount = player.Numeric.Count
                    });
                }
            }

            if (_simulation != null)
            {
                foreach (PlayerStateSnapshot remote in _simulation.RemotePlayers.Players)
                {
                    if (remote.PlayerId == _simulation.SelfPlayerId || !capturedPlayerIds.Add(remote.PlayerId))
                    {
                        continue;
                    }

                    BattleAutomationBuffSnapshot[] activeBuffs = BuildAutomationBuffSnapshots(remote.ActiveBuffs);
                    totalActiveBuffCount += activeBuffs.Length;
                    players.Add(new BattleAutomationPlayerSnapshot
                    {
                        playerId = remote.PlayerId,
                        isSelf = false,
                        x = (float)remote.X,
                        y = (float)remote.Y,
                        health = remote.Health,
                        maxHealth = remote.MaxHealth,
                        mana = remote.Mana,
                        maxMana = remote.MaxMana,
                        attack = remote.Attack,
                        stamina = remote.Stamina,
                        maxStamina = remote.MaxStamina,
                        staminaRegenCounterFrames = remote.StaminaRegenCounterFrames,
                        dashRemainingFrames = remote.DashRemainingFrames,
                        dashRuntimeBuffId = remote.DashRuntimeBuffId,
                        recoverRemainingFrames = GetBuffRemainingFrames(remote.ActiveBuffs, DashTuning.RecoverBuffId),
                        knockbackRemainingFrames = remote.KnockbackRemainingFrames,
                        knockbackRuntimeBuffId = remote.KnockbackRuntimeBuffId,
                        activeBuffCount = activeBuffs.Length,
                        activeBuffs = activeBuffs,
                        nextRuntimeBuffId = remote.NextRuntimeBuffId,
                        numericModifierCount = remote.Numeric.Count
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
                stateMismatchCount = _simulation?.StateMismatchCount ?? 0,
                positionMismatchCount = _simulation?.PositionMismatchCount ?? 0,
                staminaMismatchCount = _simulation?.StaminaMismatchCount ?? 0,
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
                rttProbeAcksSent = _rttProbeAcksSent,
                serverControlRttMs = HasRttStats ? (float)LatestRttStats.ControlRttMs : 0f,
                appliedTargetLeadFrames = (int)(_simulation?.AppliedTargetLeadFrames ?? 0u),

                dashCount = _dashCount,
                staminaAtEnd = staminaAtEnd,
                knockbackTriggerCount = _knockbackTriggerCount,
                contactMismatchFrames = _simulation?.ContactMismatchFrames ?? 0,
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
            _rttProbeHandler = OnRttProbeMessage;
            _rttStatsHandler = OnRttStatsMessage;
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_FrameSnapshot, _snapshotHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_Pong, _pongHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_BandwidthStats, _bandwidthStatsHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_RttProbe, _rttProbeHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_RttStats, _rttStatsHandler);
            _snapshotRegistered = true;
            _pongRegistered = true;
            _bandwidthStatsRegistered = true;
            _rttProbeRegistered = true;
            _rttStatsRegistered = true;
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
            uint targetLeadFrames = snapshot.TargetLeadFrames;
            BattleWorldSnapshot authoritativeSnapshot = ConvertSnapshot(snapshot, out uint selfLatestAcceptedInputFrame);
            if (_networkGate == null)
            {
                _simulation?.ApplyAuthoritativeTargetLead(targetLeadFrames);
                _simulation?.EnqueueServerSnapshot(authoritativeSnapshot, selfLatestAcceptedInputFrame);
                return;
            }

            _networkGate.EnqueueConvertedSnapshot(
                nowMs,
                authoritativeSnapshot,
                value =>
                {
                    _simulation?.ApplyAuthoritativeTargetLead(targetLeadFrames);
                    _simulation?.EnqueueServerSnapshot(value, selfLatestAcceptedInputFrame);
                });
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

        private void OnRttProbeMessage(IMessage message)
        {
            if (message is not S2C_RttProbe probe)
            {
                return;
            }

            // Copy scalar immediately — pooled message is disposed after callback.
            ulong probeNonce = probe.ProbeNonce;
            long nowMs = _battleClock?.NowMs ?? SystemBattleClock.Instance.NowMs;
            if (_networkGate == null)
            {
                SendRttProbeAck(probeNonce);
                return;
            }

            _networkGate.TryAcceptRttProbe(nowMs, probeNonce, DeliverRttProbeAck);
        }

        private void DeliverRttProbeAck(ulong probeNonce)
        {
            Action<ulong> send = _networkGate != null
                ? _networkGate.WrapSendRttProbeAck(SendRttProbeAck)
                : SendRttProbeAck;
            send(probeNonce);
        }

        private void OnRttStatsMessage(IMessage message)
        {
            if (message is not S2C_RttStats stats)
            {
                return;
            }

            LatestRttStats = new RttStatsSnapshot
            {
                FrameIndex = stats.FrameIndex,
                Enabled = stats.Enabled,
                HasSample = stats.HasSample,
                RttMinMs = stats.RttMinMs,
                RttEmaMs = stats.RttEmaMs,
                ControlRttMs = stats.ControlRttMs,
                RttSampleCount = stats.RttSampleCount,
                LeadOutOfBoundsCount = stats.LeadOutOfBoundsCount,
                AppliedTargetLeadFrames = _simulation?.AppliedTargetLeadFrames ?? 0u,
                LeadFrames = _simulation?.LeadFrames ?? 0u
            };
            HasRttStats = true;
            GameEvent.Get<IBattleUI>().OnRttStatsUpdated();
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

            // 自己：仍从 worldState 取，走 PredictionErrorSmoother。
            if (worldState.TryGetPlayer(_simulation.SelfPlayerId, out PlayerState selfPlayer))
            {
                _activePlayers.Add(selfPlayer.PlayerId);
                int recoverRemainingFrames = GetBuffRemainingFrames(
                    selfPlayer.ActiveBuffs,
                    DashTuning.RecoverBuffId);
                _renderTargetsByPlayerId[selfPlayer.PlayerId] = new RenderTarget(
                    selfPlayer.X,
                    selfPlayer.Y,
                    selfPlayer.Stamina,
                    selfPlayer.MaxStamina,
                    selfPlayer.DashRemainingFrames,
                    recoverRemainingFrames,
                    selfPlayer.KnockbackRemainingFrames);
                ObserveSelfGameplayEpochs(selfPlayer.DashRuntimeBuffId, selfPlayer.KnockbackRuntimeBuffId);
                LatestGameplayStatus = new GameplayStatusSnapshot
                {
                    Stamina = selfPlayer.Stamina,
                    MaxStamina = selfPlayer.MaxStamina,
                    DashRemainingFrames = selfPlayer.DashRemainingFrames,
                    RecoverRemainingFrames = recoverRemainingFrames,
                    KnockbackRemainingFrames = selfPlayer.KnockbackRemainingFrames,
                    Phase = ResolveGameplayPhase(
                        selfPlayer.DashRemainingFrames,
                        recoverRemainingFrames)
                };
                HasGameplayStatus = true;
                GameObject selfSphere = GetOrCreateSphere(selfPlayer.PlayerId, true);
                selfSphere.SetActive(true);
            }
            else
            {
                HasGameplayStatus = false;
                if (_staminaBarRoot != null)
                {
                    _staminaBarRoot.SetActive(false);
                }
            }

            // 别人：从 RemotePlayerBuffer 直取最新权威位置（S10 再做插值）。
            foreach (PlayerStateSnapshot remote in _simulation.RemotePlayers.Players)
            {
                _activePlayers.Add(remote.PlayerId);
                _renderTargetsByPlayerId[remote.PlayerId] = new RenderTarget(
                    remote.X,
                    remote.Y,
                    remote.Stamina,
                    remote.MaxStamina,
                    remote.DashRemainingFrames,
                    GetBuffRemainingFrames(remote.ActiveBuffs, DashTuning.RecoverBuffId),
                    remote.KnockbackRemainingFrames);
                GameObject sphere = GetOrCreateSphere(remote.PlayerId, false);
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
                    TrailRenderer trail = pair.Value.GetComponent<TrailRenderer>();
                    if (trail != null)
                    {
                        trail.Clear();
                    }

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
            bool hasSelfRenderState = false;
            Vector3 selfRenderPosition = Vector3.zero;
            RenderTarget selfRenderTarget = default;
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
                bool isSelf = pair.Key == _simulation.SelfPlayerId;
                UpdateSphereVisualState(sphere, isSelf, pair.Value);
                if (pair.Key == _simulation.SelfPlayerId)
                {
                    hasSelfRenderState = true;
                    selfRenderPosition = position;
                    selfRenderTarget = pair.Value;
                    _simulation.RecordRenderedSelfPosition(position.x, position.z);
                    UpdateStaminaBar(position, pair.Value.Stamina, pair.Value.MaxStamina);
                }
            }

            if (_hasAuthoritativeGhostTarget && _authoritativeGhostSphere != null)
            {
                _authoritativeGhostSphere.transform.position = ToWorldPosition(
                    _authoritativeGhostTargetX,
                    _authoritativeGhostTargetY);
            }

            UpdateAuthoritativeSeparationVisual(
                hasSelfRenderState,
                selfRenderPosition,
                selfRenderTarget);

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
                bool hasAttributeBaseline = _authoritativeAttributesByPlayerId.TryGetValue(
                    player.PlayerId,
                    out AuthoritativeAttributeBaseline attributeBaseline);
                AttributeMergeResult attributeMerge = BattleSnapshotProtocolMapper.MergeAttributes(
                    snapshot.FrameIndex,
                    player,
                    hasAttributeBaseline,
                    hasAttributeBaseline ? attributeBaseline.Attributes : PlayerAttributeSnapshot.Default,
                    hasAttributeBaseline ? attributeBaseline.FrameIndex : 0u);
                if (attributeMerge.Diverged)
                {
                    Log.Warning(
                        $"[Battle] Attribute baseline diverged. player={player.PlayerId} frame={snapshot.FrameIndex} " +
                        $"localBase={(hasAttributeBaseline ? attributeBaseline.FrameIndex : 0u)} " +
                        $"packetBase={player.AttributeBaselineFrameIndex} hasBaseline={hasAttributeBaseline}");
                }

                if (attributeMerge.HasBaseline)
                {
                    _authoritativeAttributesByPlayerId[player.PlayerId] = new AuthoritativeAttributeBaseline(
                        attributeMerge.Attributes,
                        attributeMerge.FrameIndex);
                }

                PlayerAttributeSnapshot mergedAttributes = attributeMerge.Attributes;
                ResolveAuthoritativeBuffSnapshot(snapshot.FrameIndex, player, out BuffState[] authoritativeBuffs, out long nextRuntimeBuffId);
                players[i] = new PlayerStateSnapshot(
                    player.PlayerId,
                    Fixed64.FromRaw(player.XRaw),
                    Fixed64.FromRaw(player.YRaw),
                    mergedAttributes,
                    authoritativeBuffs,
                    nextRuntimeBuffId,
                    BattleSnapshotProtocolMapper.ReadNumericSnapshot(player.Numeric, mergedAttributes),
                    player.StaminaRegenCounterFrames,
                    Fixed64.FromRaw(player.DashVelocityXRaw),
                    Fixed64.FromRaw(player.DashVelocityYRaw),
                    player.DashRemainingFrames,
                    player.DashRuntimeBuffId,
                    Fixed64.FromRaw(player.KnockbackVelocityXRaw),
                    Fixed64.FromRaw(player.KnockbackVelocityYRaw),
                    player.KnockbackRemainingFrames,
                    player.KnockbackRuntimeBuffId,
                    BattleSnapshotProtocolMapper.ReadSkillExecutions(player.SkillExecutions));
                bodies[i] = BattleSnapshotProtocolMapper.ReadPhysicsBody(player);
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
            return BattleSnapshotProtocolMapper.ReadBuffStates(buffs);
        }

        private static BuffSync.BuffChange[] BuildBuffChanges(IReadOnlyList<BuffSnapshot> buffs)
        {
            return BattleSnapshotProtocolMapper.ReadBuffChanges(buffs);
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
                CollectStaleAuthoritativePlayer(playerId);
            }

            foreach (long playerId in _authoritativeBuffsByPlayerId.Keys)
            {
                CollectStaleAuthoritativePlayer(playerId);
            }

            foreach (long playerId in _playersAwaitingBuffFullSync)
            {
                CollectStaleAuthoritativePlayer(playerId);
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

        private void CollectStaleAuthoritativePlayer(long playerId)
        {
            if (_authoritativePlayersInSnapshot.Contains(playerId) ||
                _staleAuthoritativePlayers.Contains(playerId))
            {
                return;
            }

            _staleAuthoritativePlayers.Add(playerId);
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

        private void SendRttProbeAck(ulong probeNonce)
        {
            GameClient.Instance.Send(new C2B_RttProbeAck
            {
                ProbeNonce = probeNonce
            });
            _rttProbeAcksSent++;
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
                renderer.material.color = isSelf ? SelfColor : RemoteColor;
            }

            ConfigureDashTrail(sphere);

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
                _authoritativeGhostRenderer = renderer;
            }

            Collider collider = ghost.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            _authoritativeGhostSphere = ghost;
            return ghost;
        }

        private static void ConfigureDashTrail(GameObject sphere)
        {
            TrailRenderer trail = sphere.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = sphere.AddComponent<TrailRenderer>();
            }

            float diameter = (float)(GameplayRoomSettings.PlayerRadius * Fixed64.Two);
            trail.time = 0.22f;
            trail.minVertexDistance = 0.04f;
            trail.startWidth = diameter * 0.72f;
            trail.endWidth = 0.0f;
            trail.alignment = LineAlignment.View;
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                trail.material = new Material(shader);
            }

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(DashColor, 0.0f),
                    new GradientColorKey(new Color(1.0f, 0.3f, 0.05f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(0.82f, 0.0f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            trail.colorGradient = gradient;
        }

        private static void UpdateSphereVisualState(GameObject sphere, bool isSelf, RenderTarget target)
        {
            Renderer renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                Color color = isSelf ? SelfColor : RemoteColor;
                if (target.KnockbackRemainingFrames > 0)
                {
                    color = KnockbackColor;
                }
                else if (target.DashRemainingFrames > 0)
                {
                    color = DashColor;
                }
                else if (target.RecoverRemainingFrames > 0)
                {
                    color = RecoverColor;
                }

                renderer.material.color = color;
            }

            TrailRenderer trail = sphere.GetComponent<TrailRenderer>();
            if (trail != null)
            {
                trail.emitting = target.DashRemainingFrames > 0;
            }
        }

        private void UpdateStaminaBar(Vector3 selfPosition, int stamina, int maxStamina)
        {
            EnsureStaminaBar();
            _staminaBarRoot.SetActive(true);
            _staminaBarRoot.transform.position = new Vector3(
                selfPosition.x,
                (float)GameplayRoomSettings.PlayerRadius + 0.5f,
                selfPosition.z + 0.82f);

            float ratio = maxStamina > 0
                ? Mathf.Clamp01(stamina / (float)maxStamina)
                : 0.0f;
            _staminaBarFill.localScale = new Vector3(
                StaminaBarWidth * ratio,
                0.04f,
                StaminaBarHeight * 0.72f);
            _staminaBarFill.localPosition = new Vector3(
                (-StaminaBarWidth * 0.5f) + (StaminaBarWidth * ratio * 0.5f),
                0.04f,
                0.0f);
            if (_staminaBarFillRenderer != null)
            {
                _staminaBarFillRenderer.material.color = Color.Lerp(
                    KnockbackColor,
                    SelfColor,
                    ratio);
            }
        }

        private void EnsureStaminaBar()
        {
            if (_staminaBarRoot != null)
            {
                return;
            }

            _staminaBarRoot = new GameObject("GameplayStaminaBar");
            _staminaBarRoot.transform.SetParent(transform, false);

            GameObject background = CreateBarPart(
                _staminaBarRoot.transform,
                "Background",
                new Vector3(StaminaBarWidth, 0.04f, StaminaBarHeight),
                new Color(0.03f, 0.04f, 0.05f));
            background.transform.localPosition = Vector3.zero;

            GameObject fill = CreateBarPart(
                _staminaBarRoot.transform,
                "Fill",
                new Vector3(StaminaBarWidth, 0.04f, StaminaBarHeight * 0.72f),
                SelfColor);
            _staminaBarFill = fill.transform;
            _staminaBarFillRenderer = fill.GetComponent<Renderer>();
        }

        private static GameObject CreateBarPart(
            Transform parent,
            string name,
            Vector3 scale,
            Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            return part;
        }

        private void UpdateAuthoritativeSeparationVisual(
            bool hasSelfRenderState,
            Vector3 selfPosition,
            RenderTarget selfTarget)
        {
            if (!hasSelfRenderState ||
                !_hasAuthoritativeGhostTarget ||
                _authoritativeGhostSphere == null)
            {
                if (_authoritativeSeparationLine != null)
                {
                    _authoritativeSeparationLine.enabled = false;
                }

                return;
            }

            Vector3 ghostPosition = _authoritativeGhostSphere.transform.position;
            float separation = Vector3.Distance(selfPosition, ghostPosition);
            bool knockbackActive = selfTarget.KnockbackRemainingFrames > 0;
            bool showSeparation = knockbackActive || separation > 0.025f;
            LineRenderer line = GetOrCreateAuthoritativeSeparationLine();
            line.enabled = showSeparation;
            if (showSeparation)
            {
                float lineHeight = (float)GameplayRoomSettings.PlayerRadius + 0.06f;
                line.SetPosition(0, new Vector3(selfPosition.x, lineHeight, selfPosition.z));
                line.SetPosition(1, new Vector3(ghostPosition.x, lineHeight, ghostPosition.z));
            }

            if (_authoritativeGhostRenderer != null)
            {
                _authoritativeGhostRenderer.material.color = knockbackActive
                    ? new Color(KnockbackColor.r, KnockbackColor.g, KnockbackColor.b, 0.52f)
                    : new Color(1.0f, 0.78f, 0.08f, 0.35f);
            }

            float diameter = (float)(GameplayRoomSettings.PlayerRadius * Fixed64.Two);
            _authoritativeGhostSphere.transform.localScale = Vector3.one * diameter *
                (knockbackActive ? 1.12f : 1.0f);
        }

        private LineRenderer GetOrCreateAuthoritativeSeparationLine()
        {
            if (_authoritativeSeparationLine != null)
            {
                return _authoritativeSeparationLine;
            }

            GameObject lineObject = new GameObject("GameplayAuthoritativeSeparation");
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.07f;
            line.endWidth = 0.025f;
            line.startColor = new Color(1.0f, 0.28f, 0.18f, 0.95f);
            line.endColor = new Color(1.0f, 0.78f, 0.08f, 0.55f);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader);
            }

            line.enabled = false;
            _authoritativeSeparationLine = line;
            return line;
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

        private void ObserveSelfGameplayEpochs(long dashRuntimeBuffId, long knockbackRuntimeBuffId)
        {
            if (dashRuntimeBuffId > 0 && _observedSelfDashBuffIds.Add(dashRuntimeBuffId))
            {
                _dashCount++;
            }

            if (knockbackRuntimeBuffId > 0 &&
                _observedSelfKnockbackBuffIds.Add(knockbackRuntimeBuffId))
            {
                _knockbackTriggerCount++;
            }
        }

        private static int GetBuffRemainingFrames(IReadOnlyList<BuffState> activeBuffs, int buffId)
        {
            if (activeBuffs == null)
            {
                return 0;
            }

            for (int i = 0; i < activeBuffs.Count; i++)
            {
                BuffState buff = activeBuffs[i];
                if (buff.BuffId == buffId)
                {
                    return Math.Max(0, buff.RemainingFrames);
                }
            }

            return 0;
        }

        private static string ResolveGameplayPhase(int dashRemainingFrames, int recoverRemainingFrames)
        {
            if (dashRemainingFrames > 0)
            {
                return "DASH";
            }

            return recoverRemainingFrames > 0 ? "RECOVER" : "READY";
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
            public RenderTarget(
                Fixed64 x,
                Fixed64 y,
                int stamina,
                int maxStamina,
                int dashRemainingFrames,
                int recoverRemainingFrames,
                int knockbackRemainingFrames)
            {
                X = x;
                Y = y;
                Stamina = stamina;
                MaxStamina = maxStamina;
                DashRemainingFrames = dashRemainingFrames;
                RecoverRemainingFrames = recoverRemainingFrames;
                KnockbackRemainingFrames = knockbackRemainingFrames;
            }

            public Fixed64 X { get; }
            public Fixed64 Y { get; }
            public int Stamina { get; }
            public int MaxStamina { get; }
            public int DashRemainingFrames { get; }
            public int RecoverRemainingFrames { get; }
            public int KnockbackRemainingFrames { get; }
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

        private readonly struct AuthoritativeAttributeBaseline
        {
            public AuthoritativeAttributeBaseline(PlayerAttributeSnapshot attributes, uint frameIndex)
            {
                Attributes = attributes;
                FrameIndex = frameIndex;
            }

            public PlayerAttributeSnapshot Attributes { get; }
            public uint FrameIndex { get; }
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

        public struct GameplayStatusSnapshot
        {
            public int Stamina { get; set; }
            public int MaxStamina { get; set; }
            public int DashRemainingFrames { get; set; }
            public int RecoverRemainingFrames { get; set; }
            public int KnockbackRemainingFrames { get; set; }
            public string Phase { get; set; }
        }

        /// <summary>
        /// 服务端 RTT 观测值客户端快照（值拷贝自 S2C_RttStats）。
        /// </summary>
        public struct RttStatsSnapshot
        {
            public uint FrameIndex { get; set; }
            public bool Enabled { get; set; }
            public bool HasSample { get; set; }
            public double RttMinMs { get; set; }
            public double RttEmaMs { get; set; }
            public double ControlRttMs { get; set; }
            public int RttSampleCount { get; set; }
            public int LeadOutOfBoundsCount { get; set; }
            public uint AppliedTargetLeadFrames { get; set; }
            public uint LeadFrames { get; set; }
        }

    }
}
