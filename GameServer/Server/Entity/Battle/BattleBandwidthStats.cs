using System.Text;

namespace Fantasy;

/// <summary>
/// 战斗快照广播的带宽统计。按逻辑帧驱动，不依赖墙上时钟，因此在无头测试里也能复现。
///
/// 统计的是「应用层字节」：ProtoBuf 消息体 + Fantasy 外网包头（20 字节）。
/// 不含 KCP/UDP/IP 头，所以真实链路字节会略高于此处数字。
/// </summary>
public sealed class BattleBandwidthStats
{
    /// <summary>Fantasy 外网包头长度：PacketLength(4) + ProtocolCode(4) + RpcId(4) + Address(8)。</summary>
    public const int OuterPacketHeadBytes = 20;

    private readonly uint _reportIntervalFrames;
    private readonly int _logicFramesPerSecond;

    private long _totalPayloadBytes;
    private long _totalHeaderBytes;
    private long _totalFullSyncPayloadBytes;
    private long _sendCount;
    private long _snapshotTickCount;
    private int _maxSendBytes;
    private long _totalPlayerEntries;
    private long _totalContactEntries;
    private long _totalDirtyAttributeEntries;
    private long _totalBuffFullSyncEntries;
    private bool _hasFullSyncBaseline;
    private uint _nextReportFrame;

    public BattleBandwidthStats(uint reportIntervalFrames = 300u, int logicFramesPerSecond = 30)
    {
        _reportIntervalFrames = reportIntervalFrames == 0u ? 300u : reportIntervalFrames;
        _logicFramesPerSecond = logicFramesPerSecond <= 0 ? 30 : logicFramesPerSecond;
        _nextReportFrame = _reportIntervalFrames;
    }

    /// <summary>本次统计窗口内是否已经采到样本。</summary>
    public bool HasSamples => _sendCount > 0;

    /// <summary>窗口内实际脏同步下发的 payload 字节累计。</summary>
    public long TotalPayloadBytes => _totalPayloadBytes;

    /// <summary>窗口内「假设全量同步」的 payload 字节累计（仅开对照测量时有值）。</summary>
    public long TotalFullSyncPayloadBytes => _totalFullSyncPayloadBytes;

    /// <summary>是否采到了对照基线（开对照测量且至少有一条样本）。</summary>
    public bool HasFullSyncBaseline => _hasFullSyncBaseline;

    /// <summary>
    /// 记录一次 per-session 发送。
    /// </summary>
    /// <param name="payloadBytes">ProtoBuf 序列化后的消息体字节数。</param>
    /// <param name="playerEntries">该条快照携带的玩家条目数。</param>
    /// <param name="contactEntries">该条快照携带的接触点条目数。</param>
    /// <param name="dirtyAttributeEntries">属性掩码非零（即真的带了属性增量）的玩家条目数。</param>
    /// <param name="buffFullSyncEntries">走 Buff 全量下发的玩家条目数。</param>
    public void RecordSend(
        int payloadBytes,
        int playerEntries,
        int contactEntries,
        int dirtyAttributeEntries,
        int buffFullSyncEntries)
    {
        int sendBytes = payloadBytes + OuterPacketHeadBytes;
        _totalPayloadBytes += payloadBytes;
        _totalHeaderBytes += OuterPacketHeadBytes;
        _sendCount++;
        _totalPlayerEntries += playerEntries;
        _totalContactEntries += contactEntries;
        _totalDirtyAttributeEntries += dirtyAttributeEntries;
        _totalBuffFullSyncEntries += buffFullSyncEntries;

        if (sendBytes > _maxSendBytes)
        {
            _maxSendBytes = sendBytes;
        }
    }

    /// <summary>
    /// 记录同一条快照在「假设不做脏同步、属性与 Buff 全量下发」时的消息体字节数，用于算脏同步收益。
    /// 只有开启对照测量时才会被调用。
    /// </summary>
    public void RecordFullSyncCounterfactual(int payloadBytes)
    {
        _totalFullSyncPayloadBytes += payloadBytes;
        _hasFullSyncBaseline = true;
    }

    /// <summary>标记本帧产生了一次广播（无论发给几个 session）。</summary>
    public void RecordSnapshotTick()
    {
        _snapshotTickCount++;
    }

    /// <summary>是否到了输出统计的帧。到点后内部游标自动推进。</summary>
    public bool TryConsumeReportDue(uint frameIndex)
    {
        if (frameIndex < _nextReportFrame)
        {
            return false;
        }

        while (_nextReportFrame <= frameIndex)
        {
            _nextReportFrame += _reportIntervalFrames;
        }

        return true;
    }

    /// <summary>
    /// 生成一行统计文本。调用方负责打日志。
    /// </summary>
    public string BuildReport(uint frameIndex, int sessionCount)
    {
        if (_sendCount == 0)
        {
            return $"[Battle][Bandwidth] frame={frameIndex} 无发送样本";
        }

        long totalBytes = _totalPayloadBytes + _totalHeaderBytes;
        double avgSendBytes = (double)totalBytes / _sendCount;
        double seconds = (double)_snapshotTickCount / _logicFramesPerSecond;
        double bytesPerSecondTotal = seconds > 0.0 ? totalBytes / seconds : 0.0;
        double bytesPerSecondPerSession = sessionCount > 0 ? bytesPerSecondTotal / sessionCount : bytesPerSecondTotal;
        double headerRatio = totalBytes > 0 ? (double)_totalHeaderBytes / totalBytes * 100.0 : 0.0;
        double avgPlayerEntries = (double)_totalPlayerEntries / _sendCount;
        double avgContactEntries = (double)_totalContactEntries / _sendCount;

        StringBuilder builder = new StringBuilder(320);
        builder.Append("[Battle][Bandwidth] frame=").Append(frameIndex)
            .Append(" sessions=").Append(sessionCount)
            .Append(" sends=").Append(_sendCount)
            .Append(" ticks=").Append(_snapshotTickCount)
            .Append(" total=").Append(totalBytes).Append('B')
            .Append(" avgSend=").Append(avgSendBytes.ToString("F1")).Append('B')
            .Append(" maxSend=").Append(_maxSendBytes).Append('B')
            .Append(" down=").Append((bytesPerSecondPerSession / 1024.0).ToString("F2")).Append("KB/s/session")
            .Append(" downAll=").Append((bytesPerSecondTotal / 1024.0).ToString("F2")).Append("KB/s")
            .Append(" head=").Append(headerRatio.ToString("F1")).Append('%')
            .Append(" avgPlayers=").Append(avgPlayerEntries.ToString("F2"))
            .Append(" avgContacts=").Append(avgContactEntries.ToString("F2"))
            .Append(" dirtyAttrEntries=").Append(_totalDirtyAttributeEntries)
            .Append(" buffFullSyncEntries=").Append(_totalBuffFullSyncEntries);

        if (_hasFullSyncBaseline && _totalFullSyncPayloadBytes > 0)
        {
            double saved = _totalFullSyncPayloadBytes - _totalPayloadBytes;
            double savedRatio = saved / _totalFullSyncPayloadBytes * 100.0;
            builder.Append(" dirtySyncSaved=").Append(savedRatio.ToString("F1")).Append('%')
                .Append(" (payload ").Append(_totalPayloadBytes)
                .Append("B vs fullSync ").Append(_totalFullSyncPayloadBytes).Append("B)");
        }

        return builder.ToString();
    }

    /// <summary>清空累计量，开始下一个统计窗口。</summary>
    public void ResetWindow()
    {
        _totalPayloadBytes = 0;
        _totalHeaderBytes = 0;
        _totalFullSyncPayloadBytes = 0;
        _sendCount = 0;
        _snapshotTickCount = 0;
        _maxSendBytes = 0;
        _totalPlayerEntries = 0;
        _totalContactEntries = 0;
        _totalDirtyAttributeEntries = 0;
        _totalBuffFullSyncEntries = 0;
        _hasFullSyncBaseline = false;
    }
}
