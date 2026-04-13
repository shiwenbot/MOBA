using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GameShared.FrameSync.Command;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Timer;

internal static class Program
{
    private static int Main(string[] args)
    {
        Options options = Options.Parse(args);
        ValidatorReport report = Validator.Run(options);

        string reportDirectory = Path.GetDirectoryName(report.ReportPath) ?? ".";
        Directory.CreateDirectory(reportDirectory);
        File.WriteAllText(report.ReportPath, report.Markdown, new UTF8Encoding(false));

        Console.WriteLine($"[FrameSyncMvpAValidator] Report: {report.ReportPath}");
        Console.WriteLine($"[FrameSyncMvpAValidator] Result: {(report.Passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"[FrameSyncMvpAValidator] TickP95={report.TickP95Ms:F3}ms Drift={report.DriftSeconds:F6}s Frames={report.TotalFrames}");
        Console.WriteLine($"[FrameSyncMvpAValidator] Issues={report.Failures.Count} Warnings={report.Warnings.Count}");

        return report.Passed ? 0 : 1;
    }
}

internal sealed class Options
{
    private const double DefaultDurationSeconds = 300.0d;
    private const double DefaultUpdateHz = 60.0d;

    private Options(string repoRoot, string reportPath, double durationSeconds, double updateHz)
    {
        RepoRoot = repoRoot;
        ReportPath = reportPath;
        DurationSeconds = durationSeconds;
        UpdateHz = updateHz;
    }

    public string RepoRoot { get; }
    public string ReportPath { get; }
    public double DurationSeconds { get; }
    public double UpdateHz { get; }

    public static Options Parse(string[] args)
    {
        string repoRoot = FindRepoRoot();
        double durationSeconds = DefaultDurationSeconds;
        double updateHz = DefaultUpdateHz;
        string? reportPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
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
            reportPath = Path.Combine(repoRoot, "Plan", "状态帧同步", "状态帧同步-MVP-a验收报告.md");
        }
        else if (!Path.IsPathRooted(reportPath))
        {
            reportPath = Path.GetFullPath(Path.Combine(repoRoot, reportPath));
        }

        return new Options(repoRoot, reportPath, durationSeconds, updateHz);
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

internal static class Validator
{
    public static ValidatorReport Run(Options options)
    {
        List<string> failures = new List<string>();
        List<string> warnings = new List<string>();
        List<string> details = new List<string>();

        string frameSyncRoot = Path.Combine(
            options.RepoRoot,
            "UnityProject",
            "Assets",
            "GameScripts",
            "HotFix",
            "GameShared",
            "FrameSync");

        StaticScanResult staticScan = RunDeterminismStaticScan(frameSyncRoot);
        failures.AddRange(staticScan.Failures);
        warnings.AddRange(staticScan.Warnings);
        details.AddRange(staticScan.Details);

        ValidationResult timerResult = ValidateFrameTimerService();
        failures.AddRange(timerResult.Failures);
        details.AddRange(timerResult.Details);

        ValidationResult poolResult = ValidateCommandPool();
        failures.AddRange(poolResult.Failures);
        details.AddRange(poolResult.Details);

        TickRunResult tickRun = RunTickLoop(options.DurationSeconds, options.UpdateHz);
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

        string markdown = BuildMarkdown(options, staticScan, timerResult, poolResult, tickRun, failures, warnings);
        return new ValidatorReport(
            options.ReportPath,
            markdown,
            failures.Count == 0,
            tickRun.TickP95Ms,
            tickRun.DriftSeconds,
            tickRun.TotalFrames,
            failures,
            warnings);
    }

    private static StaticScanResult RunDeterminismStaticScan(string frameSyncRoot)
    {
        List<string> failures = new List<string>();
        List<string> warnings = new List<string>();
        List<string> details = new List<string>();

        if (!Directory.Exists(frameSyncRoot))
        {
            failures.Add($"FrameSync directory not found: {frameSyncRoot}");
            return new StaticScanResult(failures, warnings, details);
        }

        string[] files = Directory.GetFiles(frameSyncRoot, "*.cs", SearchOption.AllDirectories);
        ScanRule[] forbiddenRules =
        {
            new ScanRule("using UnityEngine", new Regex(@"^\s*using\s+UnityEngine\s*;", RegexOptions.Multiline)),
            new ScanRule("using Fantasy", new Regex(@"^\s*using\s+Fantasy(?:\.[A-Za-z0-9_]+)?\s*;", RegexOptions.Multiline)),
            new ScanRule("DateTime usage", new Regex(@"\bDateTime\b", RegexOptions.None)),
            new ScanRule("Stopwatch usage", new Regex(@"\bStopwatch\b", RegexOptions.None)),
            new ScanRule("System.Random usage", new Regex(@"\bSystem\.Random\b|\bnew\s+Random\s*\(", RegexOptions.None)),
            new ScanRule("UnityEngine.Random usage", new Regex(@"\bUnityEngine\.Random\b", RegexOptions.None)),
            new ScanRule("async keyword", new Regex(@"\basync\b", RegexOptions.None)),
            new ScanRule("await keyword", new Regex(@"\bawait\b", RegexOptions.None)),
            new ScanRule("Dictionary<K,V> usage", new Regex(@"\bDictionary<", RegexOptions.None)),
            new ScanRule("HashSet<T> usage", new Regex(@"\bHashSet<", RegexOptions.None))
        };

        foreach (string file in files)
        {
            string content = File.ReadAllText(file, Encoding.UTF8);
            string relativePath = Path.GetRelativePath(frameSyncRoot, file).Replace('\\', '/');

            foreach (ScanRule rule in forbiddenRules)
            {
                Match match = rule.Regex.Match(content);
                if (match.Success)
                {
                    failures.Add($"Forbidden pattern [{rule.Name}] in FrameSync/{relativePath}");
                }
            }
        }

        if (failures.Count == 0)
        {
            details.Add($"Static scan passed for {files.Length} FrameSync files.");
        }

        return new StaticScanResult(failures, warnings, details);
    }

    private static ValidationResult ValidateFrameTimerService()
    {
        List<string> failures = new List<string>();
        List<string> details = new List<string>();

        FrameTimerService timerService = new FrameTimerService();
        List<string> events = new List<string>();
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

        FrameTimerService removeDuringCallback = new FrameTimerService();
        bool secondTriggered = false;
        uint secondId = 0;
        removeDuringCallback.AddTimer(1, _ => removeDuringCallback.RemoveTimer(secondId));
        secondId = removeDuringCallback.AddTimer(1, _ => secondTriggered = true);
        removeDuringCallback.Tick(1, TickAccumulator.DefaultFixedDeltaTime);
        if (secondTriggered)
        {
            failures.Add("FrameTimer remove-in-callback rule violated: removed timer still triggered in same frame.");
        }

        FrameTimerService addDuringCallback = new FrameTimerService();
        uint childTriggeredFrame = uint.MaxValue;
        addDuringCallback.AddTimer(1, _ => addDuringCallback.AddTimer(0, frame => childTriggeredFrame = frame));
        addDuringCallback.Tick(1, TickAccumulator.DefaultFixedDeltaTime);
        addDuringCallback.Tick(2, TickAccumulator.DefaultFixedDeltaTime);
        if (childTriggeredFrame != 2)
        {
            failures.Add($"FrameTimer add-in-callback rule violated: expected child trigger at frame 2, actual={childTriggeredFrame}.");
        }

        details.Add("FrameTimerService validation completed.");
        return new ValidationResult(failures, details);
    }

    private static ValidationResult ValidateCommandPool()
    {
        List<string> failures = new List<string>();
        List<string> details = new List<string>();

        const int count = 1000;
        CommandPool<MockCommand> pool = new CommandPool<MockCommand>();
        MockCommand[] firstBatch = new MockCommand[count];
        for (int i = 0; i < count; i++)
        {
            MockCommand command = pool.Rent();
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

        HashSet<MockCommand> firstSet = new HashSet<MockCommand>(firstBatch);
        MockCommand[] secondBatch = new MockCommand[count];
        for (int i = 0; i < count; i++)
        {
            MockCommand command = pool.Rent();
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

        HashSet<MockCommand> secondSet = new HashSet<MockCommand>(secondBatch);
        if (!firstSet.SetEquals(secondSet))
        {
            failures.Add("CommandPool reuse validation failed: second rent batch is not equal to first batch references.");
        }

        details.Add("CommandPool validation completed.");
        return new ValidationResult(failures, details);
    }

    private static TickRunResult RunTickLoop(double durationSeconds, double updateHz)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        RecordingLogger logger = new RecordingLogger();
        TickDispatcher dispatcher = new TickDispatcher(
            TickAccumulator.DefaultFixedDeltaTime,
            TickAccumulator.DefaultMaxDeltaTime,
            logger);

        TickMetricsCollector collector = new TickMetricsCollector(stopwatch);
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

        return new TickRunResult(
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

        List<double> sorted = new List<double>(values);
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
        Options options,
        StaticScanResult staticScan,
        ValidationResult timerResult,
        ValidationResult poolResult,
        TickRunResult tickRun,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        StringBuilder builder = new StringBuilder();
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
            foreach (string warning in warnings)
            {
                builder.AppendLine($"- {warning}");
            }

            builder.AppendLine();
        }

        if (failures.Count > 0)
        {
            builder.AppendLine("## 失败项");
            builder.AppendLine();
            foreach (string failure in failures)
            {
                builder.AppendLine($"- {failure}");
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

    private sealed record ScanRule(string Name, Regex Regex);

    private sealed class MockCommand : IResettable
    {
        public int Value { get; set; }
        public int ResetCount { get; private set; }

        public void Reset()
        {
            Value = 0;
            ResetCount++;
        }
    }

    private sealed class TickMetricsCollector : ITickable
    {
        private readonly Stopwatch _stopwatch;
        private readonly List<double> _timestamps = new List<double>();
        private uint _lastFrame = uint.MaxValue;
        private bool _hasLastFrame;

        public TickMetricsCollector(Stopwatch stopwatch)
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

            List<double> intervals = new List<double>(_timestamps.Count - 1);
            for (int i = 1; i < _timestamps.Count; i++)
            {
                intervals.Add(_timestamps[i] - _timestamps[i - 1]);
            }

            return intervals;
        }
    }

    private sealed class RecordingLogger : IFrameSyncLogger
    {
        public List<string> Errors { get; } = new List<string>();

        public void LogError(Exception exception, string context)
        {
            Errors.Add($"{context} | {exception.GetType().Name}");
        }
    }
}

internal sealed class ValidatorReport
{
    public ValidatorReport(
        string reportPath,
        string markdown,
        bool passed,
        double tickP95Ms,
        double driftSeconds,
        uint totalFrames,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> warnings)
    {
        ReportPath = reportPath;
        Markdown = markdown;
        Passed = passed;
        TickP95Ms = tickP95Ms;
        DriftSeconds = driftSeconds;
        TotalFrames = totalFrames;
        Failures = failures;
        Warnings = warnings;
    }

    public string ReportPath { get; }
    public string Markdown { get; }
    public bool Passed { get; }
    public double TickP95Ms { get; }
    public double DriftSeconds { get; }
    public uint TotalFrames { get; }
    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Warnings { get; }
}

internal sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<string> failures, IReadOnlyList<string> details)
    {
        Failures = failures;
        Details = details;
    }

    public IReadOnlyList<string> Failures { get; }
    public IReadOnlyList<string> Details { get; }
}

internal sealed class StaticScanResult
{
    public StaticScanResult(
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

internal sealed class TickRunResult
{
    public TickRunResult(
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
