using System;
using System.Collections.Generic;
using Fantasy;
using Fantasy.Async;
using Fantasy.Network.Interface;
using GameLogic.FrameSync;
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

        private readonly Dictionary<long, GameObject> _playerCapsules = new Dictionary<long, GameObject>();
        private readonly HashSet<long> _activePlayers = new HashSet<long>();

        private ClientTickDriver _tickDriver;
        private Action<IMessage> _snapshotHandler;
        private bool _snapshotRegistered;
        private bool _isInitialized;
        private bool _isJoined;
        private long _selfPlayerId;
        private uint _inputSeq;
        private uint _lastAppliedFrame;

        public int Priority => 0;

        public void Initialize()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            EnsureTickDriver();
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

            _snapshotRegistered = false;
            _snapshotHandler = null;
            _isInitialized = false;
            _isJoined = false;
            _inputSeq = 0;
            _lastAppliedFrame = 0;
            _selfPlayerId = 0;

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

        public void Tick(uint frameIndex, float fixedDt)
        {
            if (!_isJoined)
            {
                return;
            }

            Vector2 direction = ReadKeyboardDirection();
            GameClient.Instance.Send(new C2B_PlayerInput
            {
                FrameIndex = frameIndex,
                InputSeq = ++_inputSeq,
                Dx = direction.x,
                Dy = direction.y
            });
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

            _selfPlayerId = response.PlayerId;
            _lastAppliedFrame = response.ServerFrameIndex;
            _isJoined = true;

            GameObject selfCapsule = GetOrCreateCapsule(_selfPlayerId, true);
            selfCapsule.transform.position = ToWorldPosition(response.X, response.Y);
            selfCapsule.SetActive(true);

            Log.Info($"[Battle] JoinBattle success: playerId={_selfPlayerId}, serverFrame={response.ServerFrameIndex}");
        }

        private void RegisterSnapshotHandler()
        {
            if (_snapshotRegistered)
            {
                return;
            }

            _snapshotHandler = OnSnapshotMessage;
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.S2C_FrameSnapshot, _snapshotHandler);
            _snapshotRegistered = true;
        }

        private void OnSnapshotMessage(IMessage message)
        {
            if (message is not S2C_FrameSnapshot snapshot)
            {
                return;
            }

            if (snapshot.FrameIndex <= _lastAppliedFrame)
            {
                return;
            }

            _lastAppliedFrame = snapshot.FrameIndex;
            _activePlayers.Clear();

            for (int i = 0; i < snapshot.Players.Count; i++)
            {
                PlayerSnapshot player = snapshot.Players[i];
                _activePlayers.Add(player.PlayerId);

                bool isSelf = player.PlayerId == _selfPlayerId;
                GameObject capsule = GetOrCreateCapsule(player.PlayerId, isSelf);
                capsule.transform.position = ToWorldPosition(player.X, player.Y);
                capsule.SetActive(true);
            }

            foreach (KeyValuePair<long, GameObject> pair in _playerCapsules)
            {
                if (!_activePlayers.Contains(pair.Key) && pair.Value != null)
                {
                    pair.Value.SetActive(false);
                }
            }

            Log.Info($"[Battle] ApplySnapshot Frame={snapshot.FrameIndex}");
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

        private static Vector2 ReadKeyboardDirection()
        {
            float dx = 0.0f;
            float dy = 0.0f;

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

            Vector2 direction = new Vector2(dx, dy);
            if (direction.sqrMagnitude > 1.0f)
            {
                direction.Normalize();
            }

            return direction;
        }
    }
}
