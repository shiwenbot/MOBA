using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Snapshot;

namespace GameShared.FrameSync.Battle
{
    public sealed class BattleWorldState : ISnapshotable<BattleWorldSnapshot>
    {
        private readonly Dictionary<long, PlayerState> _players = new Dictionary<long, PlayerState>();
        private readonly IPhysicsMovementWorld _physicsWorld;

        public BattleWorldState(IPhysicsMovementWorld physicsWorld = null)
        {
            _physicsWorld = physicsWorld ?? new FrameSyncPhysicsWorld();
        }

        public int PlayerCount => _players.Count;
        public Dictionary<long, PlayerState>.ValueCollection Players => _players.Values;
        public IPhysicsMovementWorld PhysicsWorld => _physicsWorld;

        public void AddOrUpdatePlayer(long playerId, float x, float y)
        {
            AddOrUpdatePlayer(playerId, (Fixed64)x, (Fixed64)y);
        }

        public void AddOrUpdatePlayer(long playerId, Fixed64 x, Fixed64 y)
        {
            if (_players.TryGetValue(playerId, out PlayerState playerState))
            {
                playerState.X = x;
                playerState.Y = y;
                _physicsWorld.SetBodyTransform(ToBodyId(playerId), x, y, false);
                return;
            }

            _players[playerId] = new PlayerState(playerId, x, y);
            _physicsWorld.EnsureBody(ToBodyId(playerId), x, y);
        }

        public bool RemovePlayer(long playerId)
        {
            _physicsWorld.RemoveBody(ToBodyId(playerId));
            return _players.Remove(playerId);
        }

        public bool TryGetPlayer(long playerId, out PlayerState playerState)
        {
            return _players.TryGetValue(playerId, out playerState);
        }

        public BattleWorldSnapshot TakeSnapshot()
        {
            PlayerStateSnapshot[] snapshots = new PlayerStateSnapshot[_players.Count];
            int index = 0;
            foreach (KeyValuePair<long, PlayerState> pair in _players)
            {
                PlayerState playerState = pair.Value;
                snapshots[index++] = new PlayerStateSnapshot(
                    playerState.PlayerId,
                    playerState.X,
                    playerState.Y,
                    playerState.CaptureAttributeSnapshot(),
                    playerState.ActiveBuffs,
                    playerState.NextRuntimeBuffId,
                    playerState.Numeric.CaptureSnapshot());
            }

            Array.Sort(snapshots, PlayerStateSnapshotComparer.Instance);
            PhysicsWorldSnapshot physicsSnapshot = _physicsWorld.TakeSnapshot();
            return new BattleWorldSnapshot(0, snapshots, physicsSnapshot);
        }

        public void RestoreSnapshot(BattleWorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            _players.Clear();
            IReadOnlyList<PlayerStateSnapshot> players = snapshot.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                PlayerState restoredPlayer = new PlayerState(player.PlayerId, player.X, player.Y, player.Attributes);
                restoredPlayer.RestoreRuntimeState(player.ActiveBuffs, player.NextRuntimeBuffId, player.Numeric);
                _players[player.PlayerId] = restoredPlayer;
            }

            if (snapshot.PhysicsSnapshot != null)
            {
                _physicsWorld.RestoreSnapshot(snapshot.PhysicsSnapshot);
                return;
            }

            _physicsWorld.ClearBodies();
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                int bodyId = ToBodyId(player.PlayerId);
                _physicsWorld.EnsureBody(bodyId, player.X, player.Y);
                _physicsWorld.SetBodyTransform(bodyId, player.X, player.Y, true);
            }
        }

        /// <summary>
        /// 只恢复指定玩家，其余玩家与其物理体一并清除。
        /// 用于客户端预测回滚——客户端只预测自己，别人由 RemotePlayerBuffer 承载。
        /// </summary>
        public void RestoreSelfOnly(BattleWorldSnapshot snapshot, long selfPlayerId)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            PlayerStateSnapshot? selfSnapshot = null;
            IReadOnlyList<PlayerStateSnapshot> players = snapshot.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                if (player.PlayerId == selfPlayerId)
                {
                    selfSnapshot = player;
                    break;
                }
            }

            // 快照里找不到自己时什么都不做，避免静默清空世界。
            if (selfSnapshot == null)
            {
                return;
            }

            PlayerStateSnapshot self = selfSnapshot.Value;
            _players.Clear();
            PlayerState restoredPlayer = new PlayerState(self.PlayerId, self.X, self.Y, self.Attributes);
            restoredPlayer.RestoreRuntimeState(self.ActiveBuffs, self.NextRuntimeBuffId, self.Numeric);
            _players[self.PlayerId] = restoredPlayer;

            // 忽略 PhysicsSnapshot 全量恢复：IPhysicsMovementWorld 没有按 bodyId 过滤 API，
            // 整份恢复会把别人的 body 带回来。位置足够，速度下帧由输入重算。
            _physicsWorld.ClearBodies();
            int bodyId = ToBodyId(self.PlayerId);
            _physicsWorld.EnsureBody(bodyId, self.X, self.Y);
            _physicsWorld.SetBodyTransform(bodyId, self.X, self.Y, true);
        }

        private static int ToBodyId(long playerId)
        {
            return checked((int)playerId);
        }

        private sealed class PlayerStateSnapshotComparer : IComparer<PlayerStateSnapshot>
        {
            public static readonly PlayerStateSnapshotComparer Instance = new PlayerStateSnapshotComparer();

            public int Compare(PlayerStateSnapshot x, PlayerStateSnapshot y)
            {
                return x.PlayerId.CompareTo(y.PlayerId);
            }
        }
    }
}
