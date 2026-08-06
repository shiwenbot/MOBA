using System;
using System.Globalization;

namespace Fantasy;

/// <summary>
/// 服务端权威 RTT 测量与超前量控制开关。
/// 可变实例（非饿汉静态 Current），便于用例切换与 S10 运行时开关。
///
/// 未设置环境变量 → 默认值。
/// 已设置但非法 → 抛异常（不静默吞掉，避免部署配错却“看起来正常”）。
///
/// BATTLE_RTT_PROBE=1                         总开关（默认开）
/// BATTLE_RTT_WINDOW_MS=200                   告警容差窗口 ms
/// BATTLE_RTT_PROBE_FRAMES=10                 探测间隔（帧）
/// BATTLE_RTT_PROBE_TIMEOUT_MS=5000           探测条目超时
/// BATTLE_RTT_TIGHTEN_RATE_MS_PER_SEC=20      envelope 收紧速率
/// BATTLE_RTT_AUTHORITATIVE_LEAD=1            下发 targetLead（默认开）
/// BATTLE_RTT_ENVELOPE_SAMPLE_CAP=1           单样本 envelope 上限（默认开）
/// </summary>
public sealed class BattleRttConfig
{
    public const uint DefaultProbeIntervalFrames = 10u;
    public const uint DefaultWindowMs = 200u;
    public const uint DefaultProbeTimeoutMs = 5000u;
    public const uint DefaultTightenRateMsPerSec = 20u;

    public bool ProbeEnabled { get; set; }
    public uint WindowMs { get; set; }
    public uint ProbeIntervalFrames { get; set; }
    public uint ProbeTimeoutMs { get; set; }
    public float TightenRateMsPerSec { get; set; }
    public bool AuthoritativeLeadEnabled { get; set; }
    public bool EnvelopeSampleCapEnabled { get; set; }

    public BattleRttConfig(
        bool probeEnabled = true,
        uint windowMs = DefaultWindowMs,
        uint probeIntervalFrames = DefaultProbeIntervalFrames,
        uint probeTimeoutMs = DefaultProbeTimeoutMs,
        float tightenRateMsPerSec = DefaultTightenRateMsPerSec,
        bool authoritativeLeadEnabled = true,
        bool envelopeSampleCapEnabled = true)
    {
        if (windowMs == 0u)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMs), windowMs, "WindowMs must be > 0.");
        }

        if (probeIntervalFrames == 0u)
        {
            throw new ArgumentOutOfRangeException(nameof(probeIntervalFrames), probeIntervalFrames, "ProbeIntervalFrames must be > 0.");
        }

        if (probeTimeoutMs == 0u)
        {
            throw new ArgumentOutOfRangeException(nameof(probeTimeoutMs), probeTimeoutMs, "ProbeTimeoutMs must be > 0.");
        }

        if (tightenRateMsPerSec <= 0f || float.IsNaN(tightenRateMsPerSec) || float.IsInfinity(tightenRateMsPerSec))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tightenRateMsPerSec),
                tightenRateMsPerSec,
                "TightenRateMsPerSec must be finite and > 0.");
        }

        ProbeEnabled = probeEnabled;
        WindowMs = windowMs;
        ProbeIntervalFrames = probeIntervalFrames;
        ProbeTimeoutMs = probeTimeoutMs;
        TightenRateMsPerSec = tightenRateMsPerSec;
        AuthoritativeLeadEnabled = authoritativeLeadEnabled;
        EnvelopeSampleCapEnabled = envelopeSampleCapEnabled;
    }

    public static BattleRttConfig FromEnvironment()
    {
        bool probeEnabled = GetEnvBool("BATTLE_RTT_PROBE", defaultValue: true);
        uint windowMs = GetEnvUInt("BATTLE_RTT_WINDOW_MS", DefaultWindowMs);
        uint probeFrames = GetEnvUInt("BATTLE_RTT_PROBE_FRAMES", DefaultProbeIntervalFrames);
        uint probeTimeout = GetEnvUInt("BATTLE_RTT_PROBE_TIMEOUT_MS", DefaultProbeTimeoutMs);
        float tightenRate = GetEnvFloat("BATTLE_RTT_TIGHTEN_RATE_MS_PER_SEC", DefaultTightenRateMsPerSec);
        bool authoritativeLead = GetEnvBool("BATTLE_RTT_AUTHORITATIVE_LEAD", defaultValue: true);
        bool envelopeCap = GetEnvBool("BATTLE_RTT_ENVELOPE_SAMPLE_CAP", defaultValue: true);
        return new BattleRttConfig(
            probeEnabled,
            windowMs,
            probeFrames,
            probeTimeout,
            tightenRate,
            authoritativeLead,
            envelopeCap);
    }

    /// <summary>
    /// 未设置 → default。已设置但无法识别 → 抛异常。
    /// </summary>
    private static bool GetEnvBool(string name, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);
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

        throw new InvalidOperationException(
            $"Environment variable {name}='{value}' is not a valid bool. " +
            "Use 1/true/yes/on or 0/false/no/off, or unset for default.");
    }

    /// <summary>
    /// 未设置 → default。已设置但解析失败或 ==0 → 抛异常。
    /// </summary>
    private static uint GetEnvUInt(string name, uint defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!uint.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ||
            parsed == 0u)
        {
            throw new InvalidOperationException(
                $"Environment variable {name}='{value}' is not a valid positive integer.");
        }

        return parsed;
    }

    private static float GetEnvFloat(string name, float defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
            parsed <= 0f ||
            float.IsNaN(parsed) ||
            float.IsInfinity(parsed))
        {
            throw new InvalidOperationException(
                $"Environment variable {name}='{value}' is not a valid positive finite float.");
        }

        return parsed;
    }
}
