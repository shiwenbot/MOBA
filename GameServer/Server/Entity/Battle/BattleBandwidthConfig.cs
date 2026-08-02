using System;

namespace Fantasy;

/// <summary>
/// 带宽统计开关。调试友好：默认开启，本机直接跑即可看到统计与对照基线。
/// 生产环境可通过环境变量显式关闭，避免多做一次序列化的开销。
///
/// BATTLE_BANDWIDTH_STATS=0              关闭统计（默认开）
/// BATTLE_BANDWIDTH_FULLSYNC_BASELINE=0  关闭对照基线测量（默认开；关闭后无法算出脏同步节省量）
/// BATTLE_BANDWIDTH_REPORT_FRAMES=300    多少逻辑帧输出一次，30Hz 下 300 帧 = 10 秒
/// </summary>
public sealed class BattleBandwidthConfig
{
    public static BattleBandwidthConfig Current { get; } = CreateCurrent();

    public bool Enabled { get; }

    /// <summary>是否同时测量全量同步对照值。仅在 <see cref="Enabled"/> 为真时生效。</summary>
    public bool MeasureFullSyncBaseline { get; }

    public uint ReportIntervalFrames { get; }

    private BattleBandwidthConfig(bool enabled, bool measureFullSyncBaseline, uint reportIntervalFrames)
    {
        Enabled = enabled;
        MeasureFullSyncBaseline = measureFullSyncBaseline;
        ReportIntervalFrames = reportIntervalFrames;
    }

    private static BattleBandwidthConfig CreateCurrent()
    {
        // 调试友好：未设环境变量时默认开启（本机 Rider 点一下即可看到统计）。
        // 生产环境可通过显式设 =0 / false / no / off 关闭。
        bool enabled = GetEnvBool("BATTLE_BANDWIDTH_STATS", defaultValue: true);
        bool measureFullSync = enabled && GetEnvBool("BATTLE_BANDWIDTH_FULLSYNC_BASELINE", defaultValue: true);
        uint reportFrames = GetEnvUInt("BATTLE_BANDWIDTH_REPORT_FRAMES", 300u);
        return new BattleBandwidthConfig(enabled, measureFullSync, reportFrames);
    }

    /// <summary>
    /// 读取布尔型环境变量。
    /// - 未设置（空）→ 返回 <paramref name="defaultValue"/>
    /// - 设为 1/true/yes/on → true
    /// - 设为 0/false/no/off → false
    /// - 其它无法识别的值 → 返回 <paramref name="defaultValue"/>
    /// </summary>
    private static bool GetEnvBool(string name, bool defaultValue)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        value = value.Trim();
        if (value == "1" ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value == "0" ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return defaultValue;
    }

    private static uint GetEnvUInt(string name, uint defaultValue)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return uint.TryParse(value, out uint parsed) && parsed > 0u ? parsed : defaultValue;
    }
}
