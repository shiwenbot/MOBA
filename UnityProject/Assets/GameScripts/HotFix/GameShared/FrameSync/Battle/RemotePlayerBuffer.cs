using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Battle
{
    /// <summary>
    /// 远端玩家的最新权威态容器。客户端只预测自己，别人不进 BattleWorldState，
    /// 也不参与回滚重放；表现层从这里取最新权威位置（S10 再做时间轴插值）。
    /// </summary>
    public sealed class RemotePlayerBuffer
    {
        private readonly Dictionary<long, PlayerStateSnapshot> _players =
            new Dictionary<long, PlayerStateSnapshot>();
        private readonly List<long> _stalePlayerIds = new List<long>();

        public int Count => _players.Count;
        public uint LastAppliedFrame { get; private set; }
        public Dictionary<long, PlayerStateSnapshot>.ValueCollection Players => _players.Values;

        public void ApplyAuthoritative(BattleWorldSnapshot snapshot, long selfPlayerId)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            HashSet<long> presentPlayerIds = new HashSet<long>();
            IReadOnlyList<PlayerStateSnapshot> players = snapshot.Players;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStateSnapshot player = players[i];
                if (player.PlayerId == selfPlayerId)
                {
                    continue;
                }

                _players[player.PlayerId] = player;
                presentPlayerIds.Add(player.PlayerId);
            }

            _stalePlayerIds.Clear();
            foreach (long playerId in _players.Keys)
            {
                if (!presentPlayerIds.Contains(playerId))
                {
                    _stalePlayerIds.Add(playerId);
                }
            }

            for (int i = 0; i < _stalePlayerIds.Count; i++)
            {
                _players.Remove(_stalePlayerIds[i]);
            }

            _stalePlayerIds.Clear();
            LastAppliedFrame = snapshot.FrameIndex;
        }

        public bool TryGet(long playerId, out PlayerStateSnapshot state)
        {
            return _players.TryGetValue(playerId, out state);
        }

        public void Clear()
        {
            _players.Clear();
            _stalePlayerIds.Clear();
            LastAppliedFrame = 0u;
        }
    }
}
