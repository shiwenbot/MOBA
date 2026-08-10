using System;
using System.Globalization;

namespace Fantasy;

/// <summary>
/// Connection-lifetime settings for an in-memory battle slot.
///
/// BATTLE_RECONNECT_GRACE_FRAMES=150   Frames kept after disconnect detection.
/// </summary>
public sealed class BattleReconnectConfig
{
    public const uint DefaultGracePeriodFrames = 150u;

    public BattleReconnectConfig(uint gracePeriodFrames = DefaultGracePeriodFrames)
    {
        if (gracePeriodFrames == 0u)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gracePeriodFrames),
                gracePeriodFrames,
                "GracePeriodFrames must be > 0.");
        }

        GracePeriodFrames = gracePeriodFrames;
    }

    public uint GracePeriodFrames { get; }

    public static BattleReconnectConfig FromEnvironment()
    {
        return new BattleReconnectConfig(
            GetEnvUInt("BATTLE_RECONNECT_GRACE_FRAMES", DefaultGracePeriodFrames));
    }

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
}
