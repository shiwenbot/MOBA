using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GameShared.FrameSync.Command;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Timer;

namespace Fantasy;

public static class FrameSyncValidationRunner
{
    public static int Run(string[] args)
    {
        FrameSyncValidationOptions options = FrameSyncValidationOptions.Parse(args);
        FrameSyncValidatorReport report = FrameSyncValidator.Run(options);

        if (!string.IsNullOrWhiteSpace(report.ReportPath))
        {
            string reportDirectory = Path.GetDirectoryName(report.ReportPath) ?? ".";
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(report.ReportPath, report.ReportContent, new UTF8Encoding(false));
            Console.WriteLine($"[FrameSyncMvpAValidator] Report: {report.ReportPath}");
        }

        Console.WriteLine($"[FrameSyncMvpAValidator] Result: {(report.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine(
            $"[FrameSyncMvpAValidator] TickP95={report.TickP95Ms:F3}ms Drift={report.DriftSeconds:F6}s Frames={report.TotalFrames}");
        Console.WriteLine($"[FrameSyncMvpAValidator] Issues={report.Failures.Count} Warnings={report.Warnings.Count}");

        if (options.Output != FrameSyncValidationOutputFormat.Console &&
            string.IsNullOrWhiteSpace(report.ReportPath))
        {
            Console.WriteLine(report.ReportContent);
        }

        return report.Passed ? 0 : 1;
    }
}

public sealed class FrameSyncValidationOptions
{
    private const double DefaultDurationSeconds = 300.0d;
    private const double DefaultUpdateHz = 60.0d;

    private FrameSyncValidationOptions(
        string repoRoot,
        string reportPath,
        double durationSeconds,
        double updateHz,
        FrameSyncValidationOutputFormat output)
    {
        RepoRoot = repoRoot;
        ReportPath = reportPath;
        DurationSeconds = durationSeconds;
        UpdateHz = updateHz;
        Output = output;
    }

    public string RepoRoot { get; }
    public string ReportPath { get; }
    public double DurationSeconds { get; }
    public double UpdateHz { get; }
    public FrameSyncValidationOutputFormat Output { get; }

    public static FrameSyncValidationOptions Parse(string[] args)
    {
        string repoRoot = FindRepoRoot();
        double durationSeconds = DefaultDurationSeconds;
        double updateHz = DefaultUpdateHz;
        string? reportPath = null;
        FrameSyncValidationOutputFormat output = FrameSyncValidationOutputFormat.Markdown;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--validate", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--mode=validate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (arg.Equals("--repo-root", StringComparison.OrdinalIgnoreCase))
            {
                repoRoot = ReadRequiredValue(args, ref i, "--repo-root");
                continue;
            }

            if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase))
            {
                reportPath = ReadRequiredValue(args, ref i, "--report");
                continue;
            }

            if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                string value = ReadRequiredValue(args, ref i, "--output");
                output = value.ToLowerInvariant() switch
                {
                    "console" => FrameSyncValidationOutputFormat.Console,
                    "markdown" => FrameSyncValidationOutputFormat.Markdown,
                    "json" => FrameSyncValidationOutputFormat.Json,
                    _ => throw new ArgumentException($"Invalid --output value: {value}")
                };
                continue;
            }

            if (arg.Equals("--duration-seconds", StringComparison.OrdinalIgnoreCase))
            {
                string value = ReadRequiredValue(args, ref i, "--duration-seconds");
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out durationSeconds) ||
                    durationSeconds <= 0.0d)
                {
                    throw new ArgumentException($"Invalid --duration-seconds value: {value}");
                }

                continue;
            }

            if (arg.Equals("--update-hz", StringComparison.OrdinalIgnoreCase))
            {
                string value = ReadRequiredValue(args, ref i, "--update-hz");
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out updateHz) ||
                    updateHz <= 0.0d)
                {
                    throw new ArgumentException($"Invalid --update-hz value: {value}");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(reportPath))
        {
            reportPath = output switch
            {
                FrameSyncValidationOutputFormat.Console => string.Empty,
                FrameSyncValidationOutputFormat.Json => Path.Combine(repoRoot, "Plan", "状态帧同步", "状态帧同步-MVP-a验收报告.json"),
                _ => Path.Combine(repoRoot, "Plan", "状态帧同步", "状态帧同步-MVP-a验收报告.md")
            };
        }
        else if (!Path.IsPathRooted(reportPath))
        {
            reportPath = Path.GetFullPath(Path.Combine(repoRoot, reportPath));
        }

        return new FrameSyncValidationOptions(repoRoot, reportPath, durationSeconds, updateHz, output);
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        int valueIndex = index + 1;
        if (valueIndex >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index = valueIndex;
        return args[valueIndex];
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            string unityProject = Path.Combine(current.FullName, "UnityProject");
            string gameServer = Path.Combine(current.FullName, "GameServer");
            if (Directory.Exists(unityProject) && Directory.Exists(gameServer))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repo root (expected UnityProject and GameServer folders).");
    }
}

public static class FrameSyncValidator
{
    public static FrameSyncValidatorReport Run(FrameSyncValidationOptions options)
    {
        List<string> failures = new();
        List<string> warnings = new();
        List<string> details = new();

        string frameSyncRoot = Path.Combine(
            options.RepoRoot,
            "UnityProject",
            "Assets",
            "GameScripts",
            "HotFix",
            "GameShared",
            "FrameSync");

        FrameSyncStaticScanResult staticScan = RunDeterminismStaticScan(frameSyncRoot);
        failures.AddRange(staticScan.Failures);
        warnings.AddRange(staticScan.Warnings);
        details.AddRange(staticScan.Details);

        FrameSyncValidationResult timerResult = ValidateFrameTimerService();
        failures.AddRange(timerResult.Failures);
        details.AddRange(timerResult.Details);

        FrameSyncValidationResult poolResult = ValidateCommandPool();
        failures.AddRange(poolResult.Failures);
        details.AddRange(poolResult.Details);

        FrameSyncTickRunResult tickRun = RunTickLoop(options.DurationSeconds, options.UpdateHz);
        if (!tickRun.FrameSequenceContinuous)
        {
            failures.Add("Tick frame sequence is not continuous.");
        }

        if (tickRun.TickP95Ms >= 40.0d)
        {
            failures.Add($"Tick interval P95 {tickRun.TickP95Ms:F3}ms exceeds threshold 40ms.");
        }

        if (tickRun.DriftSeconds >= 1.0d)
        {
            failures.Add($"Simulation drift {tickRun.DriftSeconds:F6}s exceeds threshold 1s.");
        }

        details.Add($"Tick loop frames={tickRun.TotalFrames}, intervalP95={tickRun.TickP95Ms:F3}ms, drift={tickRun.DriftSeconds:F6}s.");

        string reportContent = options.Output switch
        {
            FrameSyncValidationOutputFormat.Json => BuildJson(options, staticScan, timerResult, poolResult, tickRun, failures, warnings),
            FrameSyncValidationOutputFormat.Console => BuildText(options, staticScan, timerResult, poolResult, tickRun, failures, warnings),
            _ => BuildMarkdown(options, staticScan, timerResult, poolResult, tickRun, failures, warnings)
        };

        return new FrameSyncValidatorReport(
            options.ReportPath,
            reportContent,
            failures.Count == 0,
            tickRun.TickP95Ms,
            tickRun.DriftSeconds,
            tickRun.TotalFrames,
            failures,
            warnings);
    }

    private static FrameSyncStaticScanResult RunDeterminismStaticScan(string frameSyncRoot)
    {
        List<string> failures = new();
        List<string> warnings = new();
        List<string> details = new();

        if (!Directory.Exists(frameSyncRoot))
        {
            failures.Add($"FrameSync directory not found: {frameSyncRoot}");
            return new FrameSyncStaticScanResult(failures, warnings, details);
        }

        string[] files = Directory.GetFiles(frameSyncRoot, "*.cs", SearchOption.AllDirectories);
        FrameSyncScanRule[] forbiddenRules =
        {
            new("using UnityEngine", new Regex(@"^\s*using\s+UnityEngine\s*;", RegexOptions.Multiline)),
            new("using Fantasy", new Regex(@"^\s*using\s+Fantasy(?:\.[A-Za-z0-9_]+)?\s*;", RegexOptions.Multiline)),
            new("DateTime usage", new Regex(@"\bDateTime\b", RegexOptions.None)),
            new("Stopwatch usage", new Regex(@"\bStopwatch\b", RegexOptions.None)),
            new("System.Random usage", new Regex(@"\bSystem\.Random\b|\bnew\s+Random\s*\(", RegexOptions.None)),
            new("UnityEngine.Random usage", new Regex(@"\bUnityEngine\.Random\b", RegexOptions.None)),
            new("async keyword", new Regex(@"\basync\b", RegexOptions.None)),
            new("await keyword", new Regex(@"\bawait\b", RegexOptions.None))
        };

        FrameSyncScanRule[] advisoryRules =
        {
            new("Dictionary<K,V> usage", new Regex(@"\bDictionary<", RegexOptions.None)),
            new("HashSet<T> usage", new Regex(@"\bHashSet<", RegexOptions.None))
        };

        foreach (string file in files)
        {
            string content = File.ReadAllText(file, Encoding.UTF8);
            string relativePath = Path.GetRelativePath(frameSyncRoot, file).Replace('\\', '/');

            for (int i = 0; i < forbiddenRules.Length; i++)
            {
                FrameSyncScanRule rule = forbiddenRules[i];
                Match match = rule.Regex.Match(content);
                if (match.Success)
                {
                    failures.Add($"Forbidden pattern [{rule.Name}] in FrameSync/{relativePath}");
                }
            }

            for (int i = 0; i < advisoryRules.Length; i++)
            {
                FrameSyncScanRule rule = advisoryRules[i];
                Match match = rule.Regex.Match(content);
                if (match.Success)
                {
                    warnings.Add(
                        $"Advisory pattern [{rule.Name}] in FrameSync/{relativePath}. Ensure deterministic code paths do not depend on collection iteration order.");
                }
            }
        }

        if (failures.Count == 0)
        {
            details.Add($"Static scan passed for {files.Length} FrameSync files.");
        }

        return new FrameSyncStaticScanResult(failures, warnings, details);
    }

    private static FrameSyncValidationResult ValidateFrameTimerService()
    {
        List<string> failures = new();
        List<string> details = new();

        FrameTimerService timerService = new();
        List<string> events = new();
        timerService.AddTimer(3, frame => events.Add($"S:{frame}"));
        timerService.AddTimer(2, frame => events.Add($"R:{frame}"), repeat: true);

        for (uint frame = 0; frame < 10; frame++)
        {
            timerService.Tick(frame, TickAccumulator.DefaultFixedDeltaTime);
        }

        string[] expected = { "R:2", "S:3", "R:4", "R:6", "R:8" };
        if (!events.SequenceEqual(expected))
        {
            failures.Add($"FrameTimer single/repeat sequence mismatch. expected=[{string.Join(",", expected)}], actual=[{string.Join(",", events)}]");
        }

        FrameTimerService removeDuringCallback = new();
        bool secondTriggered = false;
        uint secondId = 0;
        removeDuringCallback.AddTimer(1, _ => removeDuringCallback.RemoveTimer(secondId));
        secondId = removeDuringCallback.AddTimer(1, _ => secondTriggered = true);
        removeDuringCallback.Tick(1, TickAccumulator.DefaultFixedDeltaTime);
        if (secondTriggered)
        {
            failures.Add("FrameTimer remove-in-callback rule violated: removed timer still triggered in same frame.");
        }

        FrameTimerService addDuringCallback = new();
        uint childTriggeredFrame = uint.MaxValue;
        addDuringCallback.AddTimer(1, _ => addDuringCallback.AddTimer(0, frame => childTriggeredFrame = frame));
        addDuringCallback.Tick(1, TickAccumulator.DefaultFixedDeltaTime);
        addDuringCallback.Tick(2, TickAccumulator.DefaultFixedDeltaTime);
        if (childTriggeredFrame != 2)
        {
            failures.Add($"FrameTimer add-in-callback rule violated: expected child trigger at frame 2, actual={childTriggeredFrame}.");
        }

        details.Add("FrameTimerService validation completed.");
        return new FrameSyncValidationResult(failures, details);
    }

    private static FrameSyncValidationResult ValidateCommandPool()
    {
        List<string> failures = new();
        List<string> details = new();

        const int count = 1000;
        CommandPool<FrameSyncMockCommand> pool = new();
        FrameSyncMockCommand[] firstBatch = new FrameSyncMockCommand[count];
        for (int i = 0; i < count; i++)
        {
            FrameSyncMockCommand command = pool.Rent();
            command.Value = i + 1;
            firstBatch[i] = command;
        }

        for (int i = 0; i < count; i++)
        {
            pool.Return(firstBatch[i]);
        }

        if (pool.AvailableCount != count)
        {
            failures.Add($"CommandPool available count mismatch after return. expected={count}, actual={pool.AvailableCount}.");
        }

        HashSet<FrameSyncMockCommand> firstSet = new(firstBatch);
        FrameSyncMockCommand[] secondBatch = new FrameSyncMockCommand[count];
        for (int i = 0; i < count; i++)
        {
            FrameSyncMockCommand command = pool.Rent();
            secondBatch[i] = command;
            if (command.Value != 0)
            {
                failures.Add($"CommandPool reset rule violated. command.Value expected 0, actual={command.Value}.");
                break;
            }

            if (command.ResetCount <= 0)
            {
                failures.Add("CommandPool reset count rule violated. Reset() was not called before reuse.");
                break;
            }
        }

        HashSet<FrameSyncMockCommand> secondSet = new(secondBatch);
        if (!firstSet.SetEquals(secondSet))
        {
            failures.Add("CommandPool reuse validation failed: second rent batch is not equal to first batch references.");
        }

        details.Add("CommandPool validation completed.");
        return new FrameSyncValidationResult(failures, details);
    }

    private static FrameSyncTickRunResult RunTickLoop(double durationSeconds, double updateHz)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        FrameSyncRecordingLogger logger = new();
        TickDispatcher dispatcher = new(
            TickAccumulator.DefaultFixedDeltaTime,
            TickAccumulator.DefaultMaxDeltaTime,
            logger);

        FrameSyncTickMetricsCollector collector = new(stopwatch);
        dispatcher.Register(collector);

        double updateIntervalSeconds = 1.0d / updateHz;
        double nextUpdate = stopwatch.Elapsed.TotalSeconds;
        double lastUpdate = nextUpdate;

        while (stopwatch.Elapsed.TotalSeconds < durationSeconds)
        {
            double now = stopwatch.Elapsed.TotalSeconds;
            double deltaTime = now - lastUpdate;
            lastUpdate = now;

            dispatcher.Update((float)deltaTime);

            nextUpdate += updateIntervalSeconds;
            SleepUntil(stopwatch, nextUpdate);
        }

        double end = stopwatch.Elapsed.TotalSeconds;
        double finalDelta = end - lastUpdate;
        if (finalDelta > 0.0d)
        {
            dispatcher.Update((float)finalDelta);
        }

        IReadOnlyList<double> intervalsMs = collector.GetIntervalsMilliseconds();
        double p95Ms = CalculatePercentile(intervalsMs, 95.0d);

        double simulatedSeconds = dispatcher.CurrentFrame * dispatcher.FixedDeltaTime;
        double driftSeconds = Math.Abs(simulatedSeconds - stopwatch.Elapsed.TotalSeconds);

        bool continuous = collector.FrameSequenceContinuous;
        if (logger.Errors.Count > 0)
        {
            continuous = false;
        }

        return new FrameSyncTickRunResult(
            dispatcher.CurrentFrame,
            p95Ms,
            driftSeconds,
            continuous,
            logger.Errors.Count);
    }

    private static void SleepUntil(Stopwatch stopwatch, double targetTimeSeconds)
    {
        while (true)
        {
            double remaining = targetTimeSeconds - stopwatch.Elapsed.TotalSeconds;
            if (remaining <= 0.0d)
            {
                return;
            }

            if (remaining > 0.0015d)
            {
                Thread.Yield();
                continue;
            }

            Thread.SpinWait(256);
        }
    }

    private static double CalculatePercentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0d;
        }

        List<double> sorted = new(values);
        sorted.Sort();
        double position = (percentile / 100.0d) * (sorted.Count - 1);
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        double weight = position - lowerIndex;
        return sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * weight);
    }

    private static string BuildMarkdown(
        FrameSyncValidationOptions options,
        FrameSyncStaticScanResult staticScan,
        FrameSyncValidationResult timerResult,
        FrameSyncValidationResult poolResult,
        FrameSyncTickRunResult tickRun,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        StringBuilder builder = new();
        DateTimeOffset now = DateTimeOffset.Now;
        string nowString = now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

        builder.AppendLine("# 状态帧同步-MVP-a 验收报告");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{nowString}`");
        builder.AppendLine($"- 验收工具：`GameServer/Tools/FrameSyncMvpAValidator`");
        builder.AppendLine($"- 运行参数：`duration={options.DurationSeconds:F0}s, updateHz={options.UpdateHz:F0}`");
        builder.AppendLine($"- 结果：`{(failures.Count == 0 ? "通过" : "未通过")}`");
        builder.AppendLine();
        builder.AppendLine("## 核心指标");
        builder.AppendLine();
        builder.AppendLine($"- Tick 调用间隔 p95：`{tickRun.TickP95Ms:F3}ms`（阈值 `< 40ms`）");
        builder.AppendLine($"- 仿真时间漂移：`{tickRun.DriftSeconds:F6}s`（阈值 `< 1s`）");
        builder.AppendLine($"- 帧号连续性：`{(tickRun.FrameSequenceContinuous ? "连续" : "不连续")}`");
        builder.AppendLine($"- 总帧数：`{tickRun.TotalFrames}`");
        builder.AppendLine($"- Tick 回调异常数：`{tickRun.TickExceptionCount}`");
        builder.AppendLine();
        builder.AppendLine("## 子项验证");
        builder.AppendLine();
        builder.AppendLine($"- 确定性静态约束扫描：`{(staticScan.Failures.Count == 0 ? "通过" : "失败")}`");
        builder.AppendLine($"- FrameTimerService 行为：`{(timerResult.Failures.Count == 0 ? "通过" : "失败")}`");
        builder.AppendLine($"- CommandPool< T > 行为：`{(poolResult.Failures.Count == 0 ? "通过" : "失败")}`");
        builder.AppendLine();

        if (warnings.Count > 0)
        {
            builder.AppendLine("## 警告");
            builder.AppendLine();
            for (int i = 0; i < warnings.Count; i++)
            {
                builder.AppendLine($"- {warnings[i]}");
            }

            builder.AppendLine();
        }

        if (failures.Count > 0)
        {
            builder.AppendLine("## 失败项");
            builder.AppendLine();
            for (int i = 0; i < failures.Count; i++)
            {
                builder.AppendLine($"- {failures[i]}");
            }
        }
        else
        {
            builder.AppendLine("## 结论");
            builder.AppendLine();
            builder.AppendLine("- 本次自动化验收覆盖项全部通过。");
            builder.AppendLine("- 客户端 Unity 场景挂载运行属于手工联调项，需在 Unity Editor PlayMode 进行最终确认。");
        }

        return builder.ToString();
    }

    private static string BuildText(
        FrameSyncValidationOptions options,
        FrameSyncStaticScanResult staticScan,
        FrameSyncValidationResult timerResult,
        FrameSyncValidationResult poolResult,
        FrameSyncTickRunResult tickRun,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        StringBuilder builder = new();
        builder.AppendLine($"FrameSync Validation: {(failures.Count == 0 ? "PASS" : "FAIL")}");
        builder.AppendLine($"Duration: {options.DurationSeconds:F0}s");
        builder.AppendLine($"UpdateHz: {options.UpdateHz:F0}");
        builder.AppendLine($"TickP95: {tickRun.TickP95Ms:F3}ms");
        builder.AppendLine($"Drift: {tickRun.DriftSeconds:F6}s");
        builder.AppendLine($"Frames: {tickRun.TotalFrames}");
        builder.AppendLine($"StaticScan: {(staticScan.Failures.Count == 0 ? "PASS" : "FAIL")}");
        builder.AppendLine($"FrameTimer: {(timerResult.Failures.Count == 0 ? "PASS" : "FAIL")}");
        builder.AppendLine($"CommandPool: {(poolResult.Failures.Count == 0 ? "PASS" : "FAIL")}");

        if (warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            for (int i = 0; i < warnings.Count; i++)
            {
                builder.AppendLine($"- {warnings[i]}");
            }
        }

        if (failures.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Failures:");
            for (int i = 0; i < failures.Count; i++)
            {
                builder.AppendLine($"- {failures[i]}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildJson(
        FrameSyncValidationOptions options,
        FrameSyncStaticScanResult staticScan,
        FrameSyncValidationResult timerResult,
        FrameSyncValidationResult poolResult,
        FrameSyncTickRunResult tickRun,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        var payload = new
        {
            passed = failures.Count == 0,
            options = new
            {
                durationSeconds = options.DurationSeconds,
                updateHz = options.UpdateHz,
                output = options.Output.ToString().ToLowerInvariant()
            },
            metrics = new
            {
                tickP95Ms = tickRun.TickP95Ms,
                driftSeconds = tickRun.DriftSeconds,
                totalFrames = tickRun.TotalFrames,
                frameSequenceContinuous = tickRun.FrameSequenceContinuous,
                tickExceptionCount = tickRun.TickExceptionCount
            },
            checks = new
            {
                staticScanPassed = staticScan.Failures.Count == 0,
                frameTimerPassed = timerResult.Failures.Count == 0,
                commandPoolPassed = poolResult.Failures.Count == 0
            },
            failures,
            warnings
        };

        return System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

public sealed class FrameSyncValidatorReport
{
    public FrameSyncValidatorReport(
        string reportPath,
        string reportContent,
        bool passed,
        double tickP95Ms,
        double driftSeconds,
        uint totalFrames,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        ReportPath = reportPath;
        ReportContent = reportContent;
        Passed = passed;
        TickP95Ms = tickP95Ms;
        DriftSeconds = driftSeconds;
        TotalFrames = totalFrames;
        Failures = failures;
        Warnings = warnings;
    }

    public string ReportPath { get; }
    public string ReportContent { get; }
    public bool Passed { get; }
    public double TickP95Ms { get; }
    public double DriftSeconds { get; }
    public uint TotalFrames { get; }
    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed class FrameSyncValidationResult
{
    public FrameSyncValidationResult(IReadOnlyList<string> failures, IReadOnlyList<string> details)
    {
        Failures = failures;
        Details = details;
    }

    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Details { get; }
}

public sealed class FrameSyncStaticScanResult
{
    public FrameSyncStaticScanResult(
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> details)
    {
        Failures = failures;
        Warnings = warnings;
        Details = details;
    }

    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<string> Details { get; }
}

public sealed class FrameSyncTickRunResult
{
    public FrameSyncTickRunResult(
        uint totalFrames,
        double tickP95Ms,
        double driftSeconds,
        bool frameSequenceContinuous,
        int tickExceptionCount)
    {
        TotalFrames = totalFrames;
        TickP95Ms = tickP95Ms;
        DriftSeconds = driftSeconds;
        FrameSequenceContinuous = frameSequenceContinuous;
        TickExceptionCount = tickExceptionCount;
    }

    public uint TotalFrames { get; }
    public double TickP95Ms { get; }
    public double DriftSeconds { get; }
    public bool FrameSequenceContinuous { get; }
    public int TickExceptionCount { get; }
}

public sealed record FrameSyncScanRule(string Name, Regex Regex);

public sealed class FrameSyncMockCommand : IResettable
{
    public int Value { get; set; }
    public int ResetCount { get; private set; }

    public void Reset()
    {
        Value = 0;
        ResetCount++;
    }
}

public sealed class FrameSyncTickMetricsCollector : ITickable
{
    private readonly Stopwatch _stopwatch;
    private readonly List<double> _timestamps = new();
    private uint _lastFrame = uint.MaxValue;
    private bool _hasLastFrame;

    public FrameSyncTickMetricsCollector(Stopwatch stopwatch)
    {
        _stopwatch = stopwatch;
    }

    public int Priority => 0;
    public bool FrameSequenceContinuous { get; private set; } = true;

    public void Tick(uint frameIndex, float fixedDt)
    {
        DeterminismRules.AssertFixedDt(fixedDt);
        DeterminismRules.AssertFinite(fixedDt, nameof(fixedDt));

        if (_hasLastFrame && frameIndex != _lastFrame + 1u)
        {
            FrameSequenceContinuous = false;
        }

        _lastFrame = frameIndex;
        _hasLastFrame = true;
        _timestamps.Add(_stopwatch.Elapsed.TotalMilliseconds);
    }

    public IReadOnlyList<double> GetIntervalsMilliseconds()
    {
        if (_timestamps.Count <= 1)
        {
            return Array.Empty<double>();
        }

        List<double> intervals = new(_timestamps.Count - 1);
        for (int i = 1; i < _timestamps.Count; i++)
        {
            intervals.Add(_timestamps[i] - _timestamps[i - 1]);
        }

        return intervals;
    }
}

public sealed class FrameSyncRecordingLogger : IFrameSyncLogger
{
    public List<string> Errors { get; } = new();

    public void LogError(Exception exception, string context)
    {
        Errors.Add($"{context} | {exception.GetType().Name}");
    }
}

public enum FrameSyncValidationOutputFormat
{
    Console,
    Markdown,
    Json
}
