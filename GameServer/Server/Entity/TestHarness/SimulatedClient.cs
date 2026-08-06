using System;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Snapshot;

namespace Fantasy;

public sealed class SimulatedClient
{
    private readonly Action<long, uint, uint, float, float, int> _onSendInput;
    private uint _inputSeq;

    public SimulatedClient(long playerId, Action<long, uint, uint, float, float> onSendInput)
        : this(playerId, (id, frameIndex, inputSeq, dx, dy, skillId) => onSendInput(id, frameIndex, inputSeq, dx, dy))
    {
    }

    public SimulatedClient(long playerId, Action<long, uint, uint, float, float, int> onSendInput)
    {
        PlayerId = playerId;
        WorldState = new BattleWorldState();
        _onSendInput = onSendInput ?? throw new ArgumentNullException(nameof(onSendInput));
    }

    public long PlayerId { get; }
    public BattleWorldState WorldState { get; }

    public void ApplySnapshot(TestSnapshot snapshot)
    {
        ApplySnapshot(snapshot.ToBattleWorldSnapshot());
    }

    public void ApplySnapshot(BattleWorldSnapshot snapshot)
    {
        WorldState.RestoreSnapshot(snapshot);
    }

    public ulong GetStateHash()
    {
        return StateHasher.Hash(WorldState.TakeSnapshot());
    }

    public void SubmitInput(uint frameIndex, float dx, float dy, int skillId = 0)
    {
        _onSendInput(PlayerId, frameIndex, ++_inputSeq, dx, dy, skillId);
    }
}
