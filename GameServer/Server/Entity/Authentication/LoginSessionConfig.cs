using System;
using System.Globalization;
using GameShared.FrameSync.Determinism;

namespace Fantasy;

/// <summary>
/// Login token lifetime is deliberately independent from the battle reconnect grace period.
/// AUTH_LOGIN_TOKEN_TTL_MS defaults to 24 hours, which is much longer than the default grace.
/// </summary>
public sealed class LoginSessionConfig
{
    public const long DefaultTokenTtlMs = 24L * 60L * 60L * 1000L;

    public LoginSessionConfig(long tokenTtlMs = DefaultTokenTtlMs)
    {
        if (tokenTtlMs <= 0L)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenTtlMs), tokenTtlMs, "Token TTL must be positive.");
        }

        TokenTtlMs = tokenTtlMs;
    }

    public long TokenTtlMs { get; }

    public void ValidateReconnectGrace(BattleReconnectConfig reconnectConfig)
    {
        if (reconnectConfig == null)
        {
            throw new ArgumentNullException(nameof(reconnectConfig));
        }

        long graceDurationMs = checked((long)Math.Ceiling(
            reconnectConfig.GracePeriodFrames * DeterminismRules.FixedDeltaTime * 1000.0d));
        if (TokenTtlMs <= graceDurationMs)
        {
            throw new InvalidOperationException(
                $"Login token TTL ({TokenTtlMs}ms) must be longer than reconnect grace ({graceDurationMs}ms).");
        }
    }

    public static LoginSessionConfig FromEnvironment()
    {
        string? value = Environment.GetEnvironmentVariable("AUTH_LOGIN_TOKEN_TTL_MS");
        if (string.IsNullOrWhiteSpace(value))
        {
            LoginSessionConfig defaultConfig = new LoginSessionConfig();
            defaultConfig.ValidateReconnectGrace(BattleReconnectConfig.FromEnvironment());
            return defaultConfig;
        }

        if (!long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ||
            parsed <= 0L)
        {
            throw new InvalidOperationException(
                $"Environment variable AUTH_LOGIN_TOKEN_TTL_MS='{value}' is not a valid positive integer.");
        }

        LoginSessionConfig config = new LoginSessionConfig(parsed);
        config.ValidateReconnectGrace(BattleReconnectConfig.FromEnvironment());
        return config;
    }
}
