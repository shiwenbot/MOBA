using System;
using System.Collections.Generic;
using Fantasy;
using Fantasy.Async;
using Fantasy.Network.Interface;
using GameLogic.FrameSync;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using TEngine;
using UnityEngine;
using Log = TEngine.Log;

namespace GameLogic
{
    public sealed class BattleClientController : MonoBehaviour, ITickable
    {
        private const string BattleServerAddress = "127.0.0.1";
        private const int BattleServerPort = 20101;
#if BATTLE_PREDICTION_SELF_TEST
        private static bool s_predictionSelfTestExecuted;
#endif

        private readonly Dictionary<long, GameObject> _playerCapsules = new Dictionary<long, GameObject>();
        private readonly HashSet<long> _activePlayers = new HashSet<long>();

        private ClientTickDriver _tickDriver;
        private BattleSimulation _simulation;
        private Action<IMessage> _snapshotHandler;
        private Action<IMessage> _pongHandler;
        private bool _snapshotRegistered;
        private bool _pongRegistered;
        private bool _isInitialized;
        private float _cachedDx;
        private float _cachedDy;

        public int Priority => 0;

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

            _snapshotRegistered = false;
            _pongRegistered = false;
            _snapshotHandler = null;
            _pongHandler = null;
            _simulation = null;
            _isInitialized = false;
            _cachedDx = 0.0f;
            _cachedDy = 0.0f;

            foreach (KeyValuePair<long, GameObject> pair in _playerCapsules)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            _playerCapsules.Clear();
            _activePlayers.Clear();
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                return;
            }

            ReadKeyboardDirection(out _cachedDx, out _cachedDy);
        }

        public void Tick(uint frameIndex, float fixedDt)
        {
            if (_simulation == null || !_simulation.IsJoined)
            {
                return;
            }

            TickResult tickResult = _simulation.Tick(frameIndex, fixedDt, _cachedDx, _cachedDy);
            SyncRendering();

            if (tickResult.TargetFrameExclusive > 0 && _tickDriver != null)
            {
                _tickDriver.SetTargetFrame(tickResult.TargetFrameExclusive);
            }
        }

        public void RollBack(uint targetFrame)
        {
            _simulation?.RollBack(targetFrame);
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

            _simulation = new BattleSimulation(
                _tickDriver.WorldState,
                SendInputCommand,
                SendPingCommand,
                _tickDriver.Logger);

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
            bool connected = await DataCenterSys.Instance.ConnectBattle(BattleServerAddress, BattleServerPort);
            if (!connected)
            {
                return;
            }

            C2B_JoinBattleResponse response = (C2B_JoinBattleResponse)await GameClient.Instance.Call(new C2B_JoinBattle());
            if (response == null)
            {
                Log.Warning("[Battle] JoinBattle response is null.");
                return;
            }

            if (response.ErrorCode != 0)
            {
                Log.Warning($"[Battle] JoinBattle failed, ErrorCode={response.ErrorCode}");
                return;
            }

            _simulation?.SetJoined(response.PlayerId, response.ServerFrameIndex, response.X, response.Y);
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
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_FrameSnapshot, _snapshotHandler);
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_Pong, _pongHandler);
            _snapshotRegistered = true;
            _pongRegistered = true;
        }

        private void OnSnapshotMessage(IMessage message)
        {
            if (message is not S2C_FrameSnapshot snapshot)
            {
                return;
            }

            BattleWorldSnapshot authoritativeSnapshot = ConvertSnapshot(snapshot, out uint selfLatestAcceptedInputFrame);
            _simulation?.EnqueueServerSnapshot(authoritativeSnapshot, selfLatestAcceptedInputFrame);
        }

        private void OnPongMessage(IMessage message)
        {
            if (message is not S2C_Pong pong)
            {
                return;
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long rttMs = nowMs - (long)pong.SendTimestampMs;
            if (rttMs < 0)
            {
                return;
            }

            _simulation?.ProcessPong(rttMs);
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
                bool isSelf = player.PlayerId == _simulation.SelfPlayerId;
                GameObject capsule = GetOrCreateCapsule(player.PlayerId, isSelf);
                capsule.transform.position = ToWorldPosition(player.X, player.Y);
                capsule.SetActive(true);
            }

            foreach (KeyValuePair<long, GameObject> pair in _playerCapsules)
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
        }

        private BattleWorldSnapshot ConvertSnapshot(S2C_FrameSnapshot snapshot, out uint selfLatestAcceptedInputFrame)
        {
            selfLatestAcceptedInputFrame = 0;
            PlayerStateSnapshot[] players = new PlayerStateSnapshot[snapshot.Players.Count];
            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerSnapshot player = snapshot.Players[i];
                players[i] = new PlayerStateSnapshot(player.PlayerId, player.X, player.Y);
                if (_simulation != null && player.PlayerId == _simulation.SelfPlayerId)
                {
                    selfLatestAcceptedInputFrame = player.LatestAcceptedInputFrame;
                }
            }

            Array.Sort(players, PlayerSnapshotComparer.Instance);
            return new BattleWorldSnapshot(snapshot.FrameIndex, players, null);
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

        private static void SendInputCommand(uint frameIndex, uint inputSeq, float dx, float dy)
        {
            GameClient.Instance.Send(new C2B_PlayerInput
            {
                FrameIndex = frameIndex,
                InputSeq = inputSeq,
                Dx = dx,
                Dy = dy
            });
        }

        private static void SendPingCommand(ulong sendTimestampMs)
        {
            GameClient.Instance.Send(new C2B_Ping
            {
                SendTimestampMs = sendTimestampMs
            });
        }

        private GameObject GetOrCreateCapsule(long playerId, bool isSelf)
        {
            if (_playerCapsules.TryGetValue(playerId, out GameObject exist) && exist != null)
            {
                return exist;
            }

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = $"BattleCapsule_{playerId}";
            capsule.transform.position = Vector3.zero;

            Renderer renderer = capsule.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = isSelf ? Color.green : Color.cyan;
            }

            _playerCapsules[playerId] = capsule;
            return capsule;
        }

        private static Vector3 ToWorldPosition(float x, float y)
        {
            return new Vector3(x, 0.5f, y);
        }

        private sealed class PlayerSnapshotComparer : IComparer<PlayerStateSnapshot>
        {
            public static readonly PlayerSnapshotComparer Instance = new PlayerSnapshotComparer();

            public int Compare(PlayerStateSnapshot x, PlayerStateSnapshot y)
            {
                return x.PlayerId.CompareTo(y.PlayerId);
            }
        }
    }
}
