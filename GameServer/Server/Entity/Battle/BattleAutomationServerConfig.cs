using System;
using GameShared.FrameSync.Battle;

namespace Fantasy;

internal sealed class BattleAutomationServerConfig
{
    private BattleAutomationServerConfig(
        string scenario,
        int minimumPlayerCount,
        int expectedBuffId,
        uint buffApplyDelayFrames,
        int buffDurationFrames,
        int buffStackCount)
    {
        Scenario = scenario ?? string.Empty;
        MinimumPlayerCount = Math.Max(1, minimumPlayerCount);
        ExpectedBuffId = expectedBuffId;
        BuffApplyDelayFrames = buffApplyDelayFrames;
        BuffDurationFrames = Math.Max(1, buffDurationFrames);
        BuffStackCount = Math.Max(1, buffStackCount);
    }

    public static BattleAutomationServerConfig Current { get; } = CreateCurrent();

    public string Scenario { get; }
    public int MinimumPlayerCount { get; }
    public int ExpectedBuffId { get; }
    public uint BuffApplyDelayFrames { get; }
    public int BuffDurationFrames { get; }
    public int BuffStackCount { get; }
    public BuffFlags BuffFlags => BuffFlags.Duration | BuffFlags.Dispellable;
    public bool Enabled => !string.IsNullOrWhiteSpace(Scenario);
    public bool IsBuffLifecycleScenario => Scenario.Equals("two-client-buff-lifecycle", StringComparison.OrdinalIgnoreCase);

    private static BattleAutomationServerConfig CreateCurrent()
    {
        // 从环境变量读取，避免与 Fantasy 框架的 CommandLine.Parser 冲突
        string scenario = Environment.GetEnvironmentVariable("BATTLE_AUTOMATION_SCENARIO") ?? string.Empty;
        int minimumPlayerCount = GetEnvInt("BATTLE_AUTOMATION_MINIMUM_PLAYER_COUNT", 2);
        int expectedBuffId = GetEnvInt("BATTLE_AUTOMATION_BUFF_ID", 9001);
        uint buffApplyDelayFrames = GetEnvUInt("BATTLE_AUTOMATION_BUFF_APPLY_DELAY_FRAMES", 30);
        int buffDurationFrames = GetEnvInt("BATTLE_AUTOMATION_BUFF_DURATION_FRAMES", 45);
        int buffStackCount = GetEnvInt("BATTLE_AUTOMATION_BUFF_STACK_COUNT", 1);

        return new BattleAutomationServerConfig(
            scenario,
            minimumPlayerCount,
            expectedBuffId,
            buffApplyDelayFrames,
            buffDurationFrames,
            buffStackCount);
    }

    private static int GetEnvInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    private static uint GetEnvUInt(string name, uint defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return uint.TryParse(value, out uint result) ? result : defaultValue;
    }
}
