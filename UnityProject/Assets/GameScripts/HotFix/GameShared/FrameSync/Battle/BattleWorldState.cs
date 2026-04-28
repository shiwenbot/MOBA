using System;
using System.Collections.Generic;
using GameShared.FrameSync.Snapshot;

namespace GameShared.FrameSync.Battle
{
    public sealed class BattleWorldState : ISnapshotable<BattleWorldSnapshot>
    {
        private readonly Dictionary<long, PlayerState> _players = new Dictionary<long, PlayerState>();

        public int PlayerCount => _players.Count;
        public Dictionary<long, PlayerState>.ValueCollection Players => _players.Values;

        public void AddOrUpdatePlayer(long playerId, float x, float y)
        {
            if (_players.TryGetValue(playerId, out PlayerState playerState))
            {
                playerState.X = x;
                playerState.Y = y;
                return;
            }

            _players[playerId] = new PlayerState(playerId, x, y);
        }

        public bool RemovePlayer(long playerId)
        {
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
                snapshots[index++] = new PlayerStateSnapshot(playerState.PlayerId, playerState.X, playerState.Y);
            }

            Array.Sort(snapshots, PlayerStateSnapshotComparer.Instance);
            return new BattleWorldSnapshot(0, snapshots, null);
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
                _players[player.PlayerId] = new PlayerState(player.PlayerId, player.X, player.Y);
            }
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
