using System;
using System.Collections.Generic;
using Fantasy;
using Fantasy.Async;
using Fantasy.Helper;
using Fantasy.Network.Interface;
using Log = TEngine.Log;

namespace GameLogic
{
internal sealed class BattleNetTestRunner
{
    private bool m_isRunning;
    private bool m_handlerRegistered;
    private uint m_nextSeq;
    private long m_startTimeMs;
    private long m_endTimeMs;
    private int m_sendIntervalMs;
    private long m_nextSendTimeMs;

    private long m_sendCount;
    private long m_recvCount;
    private long m_outOfOrderCount;
    private long m_gapLostCount;
    private long m_serverBroadcastDelayOver1MsCount;
    private uint m_lastRecvSeq;
    private bool m_hasLastRecvSeq;

    private readonly List<long> m_rttSamples = new();
    private readonly List<long> m_serverBroadcastDelaySamples = new();

    public bool IsRunning => m_isRunning;

    public void Start(int durationSeconds, int hz)
    {
        if (durationSeconds <= 0)
        {
            durationSeconds = 300;
        }

        if (hz <= 0)
        {
            hz = 30;
        }

        StopInternal("restart", false);
        EnsureHandlerRegistered();
        ResetStats();

        m_isRunning = true;
        m_nextSeq = 1;
        m_sendIntervalMs = Math.Max(1, 1000 / hz);
        m_startTimeMs = TimeHelper.Now;
        m_endTimeMs = m_startTimeMs + durationSeconds * 1000L;
        m_nextSendTimeMs = m_startTimeMs;

        Log.Info($"[BattleTest] start. duration={durationSeconds}s, hz={hz}, intervalMs={m_sendIntervalMs}");
        RunSendLoop().Coroutine();
    }

    public void StopManual()
    {
        StopInternal("manual stop", true);
    }

    private void EnsureHandlerRegistered()
    {
        if (m_handlerRegistered)
        {
            GameClient.Instance.UnRegisterMsgHandler(OuterOpcode.B2C_TestState, OnBattleState);
            m_handlerRegistered = false;
        }

        GameClient.Instance.RegisterMsgHandler(OuterOpcode.B2C_TestState, OnBattleState);
        m_handlerRegistered = true;
    }

    private async FTask RunSendLoop()
    {
        while (m_isRunning)
        {
            var client = GameClient.Instance;
            var scene = client.Scene;
            if (scene == null || scene.IsDisposed || scene.Session == null || scene.Session.IsDisposed)
            {
                StopInternal("session disconnected", true);
                return;
            }

            var nowMs = TimeHelper.Now;
            if (nowMs >= m_endTimeMs)
            {
                StopInternal("duration reached", true);
                return;
            }

            while (m_isRunning && nowMs >= m_nextSendTimeMs)
            {
                var seq = m_nextSeq++;
                m_sendCount++;
                client.Send(new C2B_TestInput
                {
                    Seq = seq,
                    ClientSendTimeMs = nowMs
                }, OuterOpcode.C2B_TestInput);

                m_nextSendTimeMs += m_sendIntervalMs;
                if (m_nextSendTimeMs < nowMs - m_sendIntervalMs * 3L)
                {
                    // If the loop lags too much, resync to avoid burst sending.
                    m_nextSendTimeMs = nowMs + m_sendIntervalMs;
                }
            }

            var waitMs = Math.Max(1, m_nextSendTimeMs - TimeHelper.Now);
            await FTask.Wait(scene, waitMs);
        }
    }

    private void OnBattleState(IMessage message)
    {
        if (!m_isRunning)
        {
            return;
        }

        if (message is not B2C_TestState state)
        {
            return;
        }

        m_recvCount++;

        var nowMs = TimeHelper.Now;
        var rtt = nowMs - state.ClientSendTimeMs;
        if (rtt >= 0)
        {
            m_rttSamples.Add(rtt);
        }

        var serverBroadcastDelay = state.ServerSendTimeMs - state.ServerRecvTimeMs;
        if (serverBroadcastDelay >= 0)
        {
            m_serverBroadcastDelaySamples.Add(serverBroadcastDelay);
            if (serverBroadcastDelay > 1)
            {
                m_serverBroadcastDelayOver1MsCount++;
            }
        }

        if (!m_hasLastRecvSeq)
        {
            m_lastRecvSeq = state.Seq;
            m_hasLastRecvSeq = true;
            return;
        }

        if (state.Seq == m_lastRecvSeq + 1)
        {
            m_lastRecvSeq = state.Seq;
            return;
        }

        if (state.Seq > m_lastRecvSeq + 1)
        {
            m_gapLostCount += state.Seq - m_lastRecvSeq - 1;
            m_lastRecvSeq = state.Seq;
            return;
        }

        m_outOfOrderCount++;
    }

    private void StopInternal(string reason, bool report)
    {
        if (!m_isRunning)
        {
            return;
        }

        m_isRunning = false;

        if (report)
        {
            PrintReport(reason);
        }
    }

    private void PrintReport(string reason)
    {
        var nowMs = TimeHelper.Now;
        var durationMs = Math.Max(1, nowMs - m_startTimeMs);
        var sendHz = m_sendCount * 1000.0 / durationMs;
        var recvHz = m_recvCount * 1000.0 / durationMs;
        var arrivalRate = m_sendCount <= 0 ? 0 : m_recvCount * 100.0 / m_sendCount;

        var (rttP50, rttP95, rttP99) = CalcPercentiles(m_rttSamples);
        var (delayP50, delayP95, delayP99) = CalcPercentiles(m_serverBroadcastDelaySamples);

        Log.Info(
            $"[BattleTest] stop: {reason}\n" +
            $"[BattleTest] durationMs={durationMs}, send={m_sendCount}, recv={m_recvCount}, arrivalRate={arrivalRate:F2}%\n" +
            $"[BattleTest] sendHz={sendHz:F2}, recvHz={recvHz:F2}, outOfOrder={m_outOfOrderCount}, seqGap={m_gapLostCount}\n" +
            $"[BattleTest] RTT(ms): p50={rttP50}, p95={rttP95}, p99={rttP99}\n" +
            $"[BattleTest] ServerBroadcastDelay(ms): p50={delayP50}, p95={delayP95}, p99={delayP99}, >1ms={m_serverBroadcastDelayOver1MsCount}");
    }

    private static (long p50, long p95, long p99) CalcPercentiles(List<long> samples)
    {
        if (samples.Count == 0)
        {
            return (0, 0, 0);
        }

        samples.Sort();
        return (Pick(samples, 0.50), Pick(samples, 0.95), Pick(samples, 0.99));
    }

    private static long Pick(List<long> sortedSamples, double percentile)
    {
        if (sortedSamples.Count == 0)
        {
            return 0;
        }

        var rawIndex = (int)Math.Ceiling(sortedSamples.Count * percentile) - 1;
        var index = rawIndex;
        if (index < 0)
        {
            index = 0;
        }
        else if (index >= sortedSamples.Count)
        {
            index = sortedSamples.Count - 1;
        }
        return sortedSamples[index];
    }

    private void ResetStats()
    {
        m_sendCount = 0;
        m_recvCount = 0;
        m_outOfOrderCount = 0;
        m_gapLostCount = 0;
        m_serverBroadcastDelayOver1MsCount = 0;
        m_lastRecvSeq = 0;
        m_hasLastRecvSeq = false;
        m_rttSamples.Clear();
        m_serverBroadcastDelaySamples.Clear();
    }
}
}
