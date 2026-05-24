using System;
using GameShared.FrameSync.Battle;
using GameShared.SkillGraph;

namespace Fantasy;

internal sealed class BattleAutomationServerConfig
{
    private BattleAutomationServerConfig(
        string scenario,
        int minimumPlayerCount,
        int expectedSkillId,
        int expectedBuffId,
        uint buffApplyDelayFrames,
        int buffDurationFrames,
        int buffStackCount)
    {
        Scenario = scenario ?? string.Empty;
        MinimumPlayerCount = Math.Max(1, minimumPlayerCount);
        ExpectedSkillId = Math.Max(0, expectedSkillId);
        ExpectedBuffId = expectedBuffId;
        BuffApplyDelayFrames = buffApplyDelayFrames;
        BuffDurationFrames = Math.Max(1, buffDurationFrames);
        BuffStackCount = Math.Max(1, buffStackCount);
    }

    public static BattleAutomationServerConfig Current { get; } = CreateCurrent();

    public string Scenario { get; }
    public int MinimumPlayerCount { get; }
    public int ExpectedSkillId { get; }
    public int ExpectedBuffId { get; }
    public uint BuffApplyDelayFrames { get; }
    public int BuffDurationFrames { get; }
    public int BuffStackCount { get; }
    public BuffFlags BuffFlags => BuffFlags.Duration | BuffFlags.Dispellable;
    public bool Enabled => !string.IsNullOrWhiteSpace(Scenario);
    public bool IsBuffLifecycleScenario => IsScenario("two-client-buff-lifecycle");
    public bool IsSkillBuffScenario => IsScenario("two-client-skill-buff");
    public bool IsBuffStackScenario => IsScenario("buff-stack", "buffstack", "two-client-buff-stack");
    public bool IsBuffRefreshScenario => IsScenario("buff-refresh", "buffrefresh", "two-client-buff-refresh");
    public bool IsBuffMutexScenario => IsScenario("buff-mutex", "buffmutex", "two-client-buff-mutex");
    public bool IsTimedBuffScenario =>
        IsBuffLifecycleScenario ||
        IsSkillBuffScenario ||
        IsBuffStackScenario ||
        IsBuffRefreshScenario ||
        IsBuffMutexScenario;

    private static BattleAutomationServerConfig CreateCurrent()
    {
        // 从环境变量读取，避免与 Fantasy 框架的 CommandLine.Parser 冲突
        string scenario = Environment.GetEnvironmentVariable("BATTLE_AUTOMATION_SCENARIO") ?? string.Empty;
        int minimumPlayerCount = GetEnvInt("BATTLE_AUTOMATION_MINIMUM_PLAYER_COUNT", 2);
        int expectedSkillId = GetEnvInt("BATTLE_AUTOMATION_SKILL_ID", BattleSkillGraphLibrary.ResolveConfiguredSkillId());
        int expectedBuffId = GetEnvInt("BATTLE_AUTOMATION_BUFF_ID", 9001);
        uint buffApplyDelayFrames = GetEnvUInt("BATTLE_AUTOMATION_BUFF_APPLY_DELAY_FRAMES", 30);
        int buffDurationFrames = GetEnvInt("BATTLE_AUTOMATION_BUFF_DURATION_FRAMES", 45);
        int buffStackCount = GetEnvInt("BATTLE_AUTOMATION_BUFF_STACK_COUNT", 1);

        return new BattleAutomationServerConfig(
            scenario,
            minimumPlayerCount,
            expectedSkillId,
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

    private bool IsScenario(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (Scenario.Equals(names[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
