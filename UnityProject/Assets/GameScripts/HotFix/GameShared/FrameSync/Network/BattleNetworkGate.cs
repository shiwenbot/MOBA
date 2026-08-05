using System;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;

namespace GameShared.FrameSync.Network
{
    public delegate void BattleInputSender(uint frameIndex, uint inputSeq, Fixed64 dx, Fixed64 dy, int skillId);

    public sealed class BattleNetworkGate
    {
        private readonly NetworkConditionSimulator _simulator;
        private readonly IBattleClock _clock;

        public BattleNetworkGate(
            NetworkConditionConfig config,
            IBattleClock clock,
            int queueCapacity = NetworkConditionSimulator.DefaultQueueCapacity)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _simulator = new NetworkConditionSimulator(config, queueCapacity);
        }

        public NetworkConditionConfig Config => _simulator.Config;
        public IBattleClock Clock => _clock;
        public long UplinkSent => _simulator.UplinkSent;
        public long UplinkDropped => _simulator.UplinkDropped;
        public long DownlinkDelivered => _simulator.DownlinkDelivered;
        public long DownlinkDropped => _simulator.DownlinkDropped;
        public int MaxQueueDepth => _simulator.MaxQueueDepth;
        public long OverflowDropped => _simulator.OverflowDropped;

        public void Configure(NetworkConditionConfig config)
        {
            _simulator.Configure(config);
        }

        public BattleInputSender WrapSendInput(BattleInputSender send)
        {
            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }

            return (frameIndex, inputSeq, dx, dy, skillId) =>
                _simulator.TryEnqueueUplink(
                    _clock.NowMs,
                    () => send(frameIndex, inputSeq, dx, dy, skillId));
        }

        public Action<ulong> WrapSendPing(Action<ulong> send)
        {
            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }

            return sendTimestampMs =>
                _simulator.TryEnqueueUplink(_clock.NowMs, () => send(sendTimestampMs));
        }

        public Action<uint, ulong> WrapSendHashReport(Action<uint, ulong> send)
        {
            if (send == null)
            {
                throw new ArgumentNullException(nameof(send));
            }

            return (frameIndex, stateHash) =>
                _simulator.TryEnqueueUplink(_clock.NowMs, () => send(frameIndex, stateHash));
        }

        public bool TryAcceptSnapshotMessage(long nowMs)
        {
            return !_simulator.ShouldDropDownlink(nowMs);
        }

        public void EnqueueConvertedSnapshot(
            long nowMs,
            BattleWorldSnapshot snapshot,
            Action<BattleWorldSnapshot> deliver)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (deliver == null)
            {
                throw new ArgumentNullException(nameof(deliver));
            }

            _simulator.EnqueueAcceptedDownlink(nowMs, () => deliver(snapshot));
        }

        public bool TryAcceptPong(long nowMs, ulong sendTimestampMs, Action<ulong> deliver)
        {
            if (deliver == null)
            {
                throw new ArgumentNullException(nameof(deliver));
            }

            return _simulator.TryEnqueueDownlink(nowMs, () => deliver(sendTimestampMs));
        }

        public void PumpDownlink(long nowMs)
        {
            _simulator.PumpDownlink(nowMs);
        }

        public void PumpUplink(long nowMs)
        {
            _simulator.PumpUplink(nowMs);
        }

        public void ClearPending()
        {
            _simulator.ClearPending();
        }
    }
}
