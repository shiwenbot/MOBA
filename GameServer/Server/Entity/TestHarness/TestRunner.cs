using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FixedMathSharp;
using GameLogic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;
using GameShared.SkillGraph;

namespace Fantasy;

public static class TestRunner
{
    private const uint DefaultTotalFrames = 300;
    private const uint StopInputFrame = 50;
    private const uint LeaveFrame = 100;

    public static int RunAll()
    {
        return Run(Array.Empty<string>());
    }

    public static int Run(string[] args)
    {
        try
        {
            TestRunOptions options = TestRunOptions.Parse(args);
            TestExecutionReport report = Execute(options);

            if (ShouldPrintConsoleReport(options))
            {
                PrintConsoleReport(report);
            }

            EmitStructuredReport(report, options);
            return report.Passed ? 0 : 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"[TestRun] ERROR: {exception.Message}");
            return 1;
        }
    }

    private static TestExecutionReport Execute(TestRunOptions options)
    {
        List<CheckResult> checks = new();
        List<ScenarioResult> scenarios = new();

        switch (options.Scenario)
        {
            case TestScenario.All:
                checks.Add(RunSnapshotSelfTest());
                checks.Add(RunFrameScheduleSelfTest());
                checks.Add(RunServerInputModelSelfTest());
                checks.Add(RunPredictionSelfTest());
                scenarios.Add(RunDeterminismScenario(options));
                scenarios.Add(RunServerClientConsistencyScenario(options));
                scenarios.Add(RunPlayerCollisionScenario(options));
                scenarios.Add(RunConvergenceScenario(options));
                scenarios.Add(RunPlayerLeaveScenario(options));
                break;

            case TestScenario.SnapshotSelf:
                checks.Add(RunSnapshotSelfTest());
                break;

            case TestScenario.BasicRoundTrip:
            case TestScenario.CapacityEviction:
            case TestScenario.SameFrameOverride:
            case TestScenario.HashNormalization:
            case TestScenario.PhysicsRoundTrip:
            case TestScenario.PhysicsAffectsHash:
            case TestScenario.SnapshotAttributeRoundTrip:
            case TestScenario.AttributesAffectHash:
            case TestScenario.AttributeDirtyMerge:
            case TestScenario.BuffRoundTrip:
            case TestScenario.BuffsAffectHash:
            case TestScenario.RuntimeBuffIdRoundTrip:
            case TestScenario.SkillBuffRoundTrip:
            case TestScenario.StackOverlayRoundTrip:
            case TestScenario.RefreshOverlayRoundTrip:
            case TestScenario.MutexReplaceRoundTrip:
            case TestScenario.MutexRejectRoundTrip:
            case TestScenario.MutexSamePriority:
            case TestScenario.MutexSameFrameTwoCommands:
            case TestScenario.StackableBridgeUpgrade:
                checks.Add(RunSnapshotSelfCase(options.Scenario));
                break;

            case TestScenario.FrameSchedule:
                checks.Add(RunFrameScheduleSelfTest());
                break;

            case TestScenario.FutureFrameInputAppliesOnTargetFrame:
                checks.Add(RunSingleCheck("FrameScheduleTest", TestScenario.FutureFrameInputAppliesOnTargetFrame, FutureFrameInputAppliesOnTargetFrame));
                break;

            case TestScenario.ServerInput:
                checks.Add(RunServerInputModelSelfTest());
                break;

            case TestScenario.ExpiredInputIsDropped:
                checks.Add(RunSingleCheck("ServerInputTest", TestScenario.ExpiredInputIsDropped, ExpiredInputIsDropped));
                break;

            case TestScenario.FutureInputIsBuffered:
                checks.Add(RunSingleCheck("ServerInputTest", TestScenario.FutureInputIsBuffered, FutureInputIsBuffered));
                break;

            case TestScenario.TooFarFutureInputIsRejected:
                checks.Add(RunSingleCheck("ServerInputTest", TestScenario.TooFarFutureInputIsRejected, TooFarFutureInputIsRejected));
                break;

            case TestScenario.MissingInputReusesLast:
                checks.Add(RunSingleCheck("ServerInputTest", TestScenario.MissingInputReusesLast, MissingInputReusesLast));
                break;

            case TestScenario.PredictionSelf:
                checks.Add(RunPredictionSelfTest());
                break;

            case TestScenario.JoinAlignsGlobalFrame:
            case TestScenario.FrameIndexIsGlobal:
            case TestScenario.InputNormalization:
            case TestScenario.PredictionMovesSelfPlayer:
            case TestScenario.CatchUpTargetIsAuthPlusLead:
            case TestScenario.QueuedSnapshotsApplyInOrder:
            case TestScenario.ConsistencyHit:
            case TestScenario.ConsistencyMiss:
            case TestScenario.PredictionBufferUsesGlobalFrame:
            case TestScenario.SkippedNoRecord:
            case TestScenario.EvictionDoesNotCrash:
            case TestScenario.AcceptedInputFeedbackRaisesLead:
            case TestScenario.AuthoritativeSnapshotRestoresPhysicsWorld:
            case TestScenario.AuthoritativeSnapshotRestoresPlayerAttributes:
            case TestScenario.AuthoritativeSnapshotRestoresPlayerBuffs:
            case TestScenario.PredictedBuffConsistencyHit:
            case TestScenario.RollbackReplaysBeforeNextConsistencyCheck:
            case TestScenario.ManualRollbackReplaysAuthoritativeHistory:
                checks.Add(RunPredictionSelfCase(options.Scenario));
                break;

            case TestScenario.SkillTriggerBuff:
                checks.Add(RunSingleCheck("BattleSkillTest", TestScenario.SkillTriggerBuff, SkillTriggerBuff));
                break;

            case TestScenario.Determinism:
                scenarios.Add(RunDeterminismScenario(options));
                break;

            case TestScenario.Consistency:
                scenarios.Add(RunServerClientConsistencyScenario(options));
                break;

            case TestScenario.Convergence:
                scenarios.Add(RunConvergenceScenario(options));
                break;

            case TestScenario.PlayerCollision:
                scenarios.Add(RunPlayerCollisionScenario(options));
                break;

            case TestScenario.PlayerLeave:
                scenarios.Add(RunPlayerLeaveScenario(options));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Scenario, "Unknown test scenario.");
        }

        return new TestExecutionReport(options, checks, scenarios);
    }

    private static bool ShouldPrintConsoleReport(TestRunOptions options)
    {
        return options.Output == TestOutputFormat.Console ||
               !string.IsNullOrWhiteSpace(options.ReportPath);
    }

    private static void PrintConsoleReport(TestExecutionReport report)
    {
        for (int i = 0; i < report.Checks.Count; i++)
        {
            CheckResult check = report.Checks[i];
            if (IsAggregateCheck(check))
            {
                Console.WriteLine(check.Passed
                    ? $"[{check.SuiteName}] ALL PASS"
                    : $"[{check.SuiteName}] FAIL: {check.Details}");
                continue;
            }

            Console.WriteLine($"[Check] {check.Name} ... {(check.Passed ? "PASS" : "FAIL")} {check.Details}".TrimEnd());
        }

        if (report.Scenarios.Count > 0)
        {
            Console.WriteLine($"[TestSuite] Running {report.Scenarios.Count} scenarios...");

            int passedCount = 0;
            for (int i = 0; i < report.Scenarios.Count; i++)
            {
                ScenarioResult result = report.Scenarios[i];
                Console.WriteLine($"[Scenario {i + 1}] {result.Name} ... {(result.Passed ? "PASS" : "FAIL")} {result.Details}".TrimEnd());
                if (result.Passed)
                {
                    passedCount++;
                }
            }

            Console.WriteLine($"[TestSuite] {passedCount}/{report.Scenarios.Count} PASS");
        }

        Console.WriteLine(
            $"[TestRun] Result={(report.Passed ? "PASS" : "FAIL")} Checks={report.PassedChecks}/{report.Checks.Count} Scenarios={report.PassedScenarios}/{report.Scenarios.Count}");
    }

    private static bool IsAggregateCheck(CheckResult check)
    {
        return check.Name == TestScenario.SnapshotSelf ||
               check.Name == TestScenario.FrameSchedule ||
               check.Name == TestScenario.ServerInput ||
               check.Name == TestScenario.PredictionSelf;
    }

    private static void EmitStructuredReport(TestExecutionReport report, TestRunOptions options)
    {
        if (options.Output == TestOutputFormat.Console && string.IsNullOrWhiteSpace(options.ReportPath))
        {
            return;
        }

        string content = options.Output switch
        {
            TestOutputFormat.Markdown => BuildMarkdownReport(report),
            TestOutputFormat.Json => BuildJsonReport(report),
            _ => BuildTextReport(report)
        };

        if (!string.IsNullOrWhiteSpace(options.ReportPath))
        {
            string reportDirectory = Path.GetDirectoryName(options.ReportPath) ?? ".";
            Directory.CreateDirectory(reportDirectory);
            File.WriteAllText(options.ReportPath, content, new UTF8Encoding(false));
            Console.WriteLine($"[TestReport] Wrote {options.Output.ToString().ToLowerInvariant()} report to {options.ReportPath}");
            return;
        }

        Console.WriteLine(content);
    }

    private static string BuildTextReport(TestExecutionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Test Run: {(report.Passed ? "PASS" : "FAIL")}");
        builder.AppendLine($"Scenario: {report.Options.Scenario}");
        builder.AppendLine($"Frames: {report.Options.Frames}");
        builder.AppendLine($"Clients: {report.Options.Clients}");
        builder.AppendLine();
        builder.AppendLine("Checks:");
        for (int i = 0; i < report.Checks.Count; i++)
        {
            CheckResult check = report.Checks[i];
            builder.AppendLine($"- {FormatCheckLabel(check)}: {(check.Passed ? "PASS" : "FAIL")} {check.Details}".TrimEnd());
        }

        if (report.Scenarios.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Scenarios:");
            for (int i = 0; i < report.Scenarios.Count; i++)
            {
                ScenarioResult scenario = report.Scenarios[i];
                builder.AppendLine($"- {scenario.Name}: {(scenario.Passed ? "PASS" : "FAIL")} {scenario.Details}".TrimEnd());
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMarkdownReport(TestExecutionReport report)
    {
        StringBuilder builder = new();
        builder.AppendLine("# 无头逻辑测试报告");
        builder.AppendLine();
        builder.AppendLine($"- 生成时间：`{report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- 结果：`{(report.Passed ? "通过" : "未通过")}`");
        builder.AppendLine($"- 场景：`{report.Options.Scenario}`");
        builder.AppendLine($"- 帧数：`{report.Options.Frames}`");
        builder.AppendLine($"- 客户端数：`{report.Options.Clients}`");
        builder.AppendLine();

        if (report.Checks.Count > 0)
        {
            builder.AppendLine("## 检查项");
            builder.AppendLine();
            for (int i = 0; i < report.Checks.Count; i++)
            {
                CheckResult check = report.Checks[i];
                builder.AppendLine($"- `{FormatCheckLabel(check)}`：`{(check.Passed ? "通过" : "失败")}` {check.Details}".TrimEnd());
            }

            builder.AppendLine();
        }

        if (report.Scenarios.Count > 0)
        {
            builder.AppendLine("## 场景结果");
            builder.AppendLine();
            for (int i = 0; i < report.Scenarios.Count; i++)
            {
                ScenarioResult scenario = report.Scenarios[i];
                builder.AppendLine($"- `{scenario.Name}`：`{(scenario.Passed ? "通过" : "失败")}` {scenario.Details}".TrimEnd());
            }

            builder.AppendLine();
        }

        builder.AppendLine("## 汇总");
        builder.AppendLine();
        builder.AppendLine($"- 检查项：`{report.PassedChecks}/{report.Checks.Count}` 通过");
        builder.AppendLine($"- 场景：`{report.PassedScenarios}/{report.Scenarios.Count}` 通过");
        return builder.ToString().TrimEnd();
    }

    private static string FormatCheckLabel(CheckResult check)
    {
        return IsAggregateCheck(check)
            ? check.SuiteName
            : $"{check.SuiteName}/{check.Name}";
    }

    private static string BuildJsonReport(TestExecutionReport report)
    {
        var payload = new
        {
            generatedAt = report.GeneratedAt.ToString("O", CultureInfo.InvariantCulture),
            passed = report.Passed,
            hashReportsSent = report.HashReportsSent,
            hashReportsMatched = report.HashReportsMatched,
            hashMismatchCount = report.HashMismatchCount,
            hashNoRecordCount = report.HashNoRecordCount,
            options = new
            {
                scenario = report.Options.Scenario,
                frames = report.Options.Frames,
                clients = report.Options.Clients,
                output = report.Options.Output.ToString().ToLowerInvariant()
            },
            checks = report.Checks.ConvertAll(static check => new
            {
                suite = check.SuiteName,
                name = check.Name,
                passed = check.Passed,
                details = check.Details
            }),
            scenarios = report.Scenarios.ConvertAll(static scenario => new
            {
                name = scenario.Name,
                passed = scenario.Passed,
                details = scenario.Details,
                hashReportsSent = scenario.HashReportsSent,
                hashReportsMatched = scenario.HashReportsMatched,
                hashMismatchCount = scenario.HashMismatchCount,
                hashNoRecordCount = scenario.HashNoRecordCount
            }),
            summary = new
            {
                passedChecks = report.PassedChecks,
                totalChecks = report.Checks.Count,
                passedScenarios = report.PassedScenarios,
                totalScenarios = report.Scenarios.Count,
                hashReportsSent = report.HashReportsSent,
                hashReportsMatched = report.HashReportsMatched,
                hashMismatchCount = report.HashMismatchCount,
                hashNoRecordCount = report.HashNoRecordCount
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static CheckResult RunSnapshotSelfTest()
    {
        bool passed = SnapshotSelfTestSuite.Run(out string failedCase);
        return passed
            ? CheckResult.Pass("SnapshotTest", TestScenario.SnapshotSelf)
            : CheckResult.Fail("SnapshotTest", TestScenario.SnapshotSelf, failedCase);
    }

    private static CheckResult RunSnapshotSelfCase(string caseName)
    {
        bool passed = SnapshotSelfTestSuite.RunCase(caseName, out string failedCase);
        return passed
            ? CheckResult.Pass("SnapshotTest", caseName)
            : CheckResult.Fail("SnapshotTest", caseName, failedCase);
    }

    private static CheckResult RunFrameScheduleSelfTest()
    {
        return RunSingleCheck("FrameScheduleTest", TestScenario.FrameSchedule, FutureFrameInputAppliesOnTargetFrame);
    }

    private static CheckResult RunServerInputModelSelfTest()
    {
        List<string> failures = new();
        if (!ExpiredInputIsDropped())
        {
            failures.Add(TestScenario.ExpiredInputIsDropped);
        }

        if (!FutureInputIsBuffered())
        {
            failures.Add(TestScenario.FutureInputIsBuffered);
        }

        if (!TooFarFutureInputIsRejected())
        {
            failures.Add(TestScenario.TooFarFutureInputIsRejected);
        }

        if (!MissingInputReusesLast())
        {
            failures.Add(TestScenario.MissingInputReusesLast);
        }

        return failures.Count == 0
            ? CheckResult.Pass("ServerInputTest", TestScenario.ServerInput)
            : CheckResult.Fail("ServerInputTest", TestScenario.ServerInput, string.Join(", ", failures));
    }

    private static CheckResult RunPredictionSelfTest()
    {
        bool passed = BattleSimulation.RunSelfTest(out string failedCase);
        return passed
            ? CheckResult.Pass("PredictSelfTest", TestScenario.PredictionSelf)
            : CheckResult.Fail("PredictSelfTest", TestScenario.PredictionSelf, failedCase);
    }

    private static CheckResult RunPredictionSelfCase(string caseName)
    {
        bool passed = BattlePredictionSelfTestSuite.RunCase(caseName, out string failedCase);
        return passed
            ? CheckResult.Pass("PredictSelfTest", caseName)
            : CheckResult.Fail("PredictSelfTest", caseName, failedCase);
    }

    private static CheckResult RunSingleCheck(string suiteName, string name, Func<bool> test)
    {
        bool passed = test();
        return passed
            ? CheckResult.Pass(suiteName, name)
            : CheckResult.Fail(suiteName, name, name);
    }

    private static bool FutureFrameInputAppliesOnTargetFrame()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);

        battleLogic.SubmitInput(1, 5, 1, 1.0f, 0.0f);
        for (uint frame = 0; frame < 5; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
            if (state.X.m_rawValue != Fixed64.Zero.m_rawValue)
            {
                return false;
            }
        }

        battleLogic.Tick(5, DeterminismRules.FixedDeltaTimeFixed64);
        Fixed64 expectedX = DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTimeFixed64;
        return Near(state.X, expectedX);
    }

    private static bool ExpiredInputIsDropped()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);

        battleLogic.Tick(0, DeterminismRules.FixedDeltaTimeFixed64);
        battleLogic.SubmitInput(1, 0, 1, 1.0f, 0.0f);
        battleLogic.Tick(1, DeterminismRules.FixedDeltaTimeFixed64);

        return NearZero(state.X) && battleLogic.LateInputDropCount == 1;
    }

    private static bool FutureInputIsBuffered()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);

        battleLogic.SubmitInput(1, 5, 1, 1.0f, 0.0f);
        for (uint frame = 0; frame < 5; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
            if (!NearZero(state.X))
            {
                return false;
            }
        }

        battleLogic.Tick(5, DeterminismRules.FixedDeltaTimeFixed64);
        Fixed64 expectedX = DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTimeFixed64;
        return Near(state.X, expectedX) &&
               battleLogic.AcceptedInputCount == 1 &&
               battleLogic.FutureInputRejectCount == 0;
    }

    private static bool TooFarFutureInputIsRejected()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);

        uint rejectedFrame = (uint)(InputBufferTuning.MaxFutureInputFrames + 1);
        battleLogic.SubmitInput(1, rejectedFrame, 1, 1.0f, 0.0f);
        for (uint frame = 0; frame <= rejectedFrame; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
        }

        return NearZero(state.X) && battleLogic.FutureInputRejectCount == 1;
    }

    private static bool MissingInputReusesLast()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);

        battleLogic.Tick(0, DeterminismRules.FixedDeltaTimeFixed64);
        if (!NearZero(state.X))
        {
            return false;
        }

        battleLogic.SubmitInput(1, 1, 1, 1.0f, 0.0f);
        battleLogic.Tick(1, DeterminismRules.FixedDeltaTimeFixed64);
        Fixed64 afterFirstTick = state.X;

        battleLogic.Tick(2, DeterminismRules.FixedDeltaTimeFixed64);
        Fixed64 expectedX = afterFirstTick + (DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTimeFixed64);
        return Near(state.X, expectedX) &&
               battleLogic.ReusedInputCount == 1 &&
               battleLogic.ZeroInputFallbackCount == 1;
    }

    private static ScenarioResult RunDeterminismScenario(TestRunOptions options)
    {
        if (options.Clients < 2)
        {
            return ScenarioResult.Fail("Determinism", "--clients must be >= 2");
        }

        Harness harness = CreateHarness(options.Clients);
        for (uint frame = 0; frame < options.Frames; frame++)
        {
            for (int i = 0; i < harness.Clients.Count; i++)
            {
                (float dx, float dy) = SharedMovementScript(frame);
                harness.Clients[i].SubmitInput(frame, dx, dy);
            }

            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
        }

        ulong expectedHash = harness.Clients[0].GetStateHash();
        for (int i = 1; i < harness.Clients.Count; i++)
        {
            ulong actualHash = harness.Clients[i].GetStateHash();
            if (actualHash != expectedHash)
            {
                return ScenarioResult.Fail(
                    $"Determinism ({harness.Clients.Count} clients, {options.Frames} frames)",
                    $"Client[0].hash=0x{expectedHash:X16} Client[{i}].hash=0x{actualHash:X16} frame={options.Frames - 1}");
            }
        }

        return ScenarioResult.Pass(
            $"Determinism ({harness.Clients.Count} clients, {options.Frames} frames)",
            $"hash=0x{expectedHash:X16}");
    }

    private static ScenarioResult RunServerClientConsistencyScenario(TestRunOptions options)
    {
        Harness harness = CreateHarness(1);
        SimulatedClient client = harness.Clients[0];
        int framesSinceHashReport = 0;
        int hashReportsSent = 0;

        for (uint frame = 0; frame < options.Frames; frame++)
        {
            (float dx, float dy) = SharedMovementScript(frame);
            client.SubmitInput(frame, dx, dy);
            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);

            framesSinceHashReport++;
            bool shouldSendHashReport = framesSinceHashReport >= 30;
            bool shouldCheckConsistency = frame % 30 == 0;
            if (!shouldSendHashReport && !shouldCheckConsistency)
            {
                continue;
            }

            ulong serverHash = harness.BattleLogic.GetStateHash();
            ulong clientHash = client.GetStateHash();
            if (shouldCheckConsistency && serverHash != clientHash)
            {
                return ScenarioResult.Fail(
                    $"Consistency (server-client, {options.Frames} frames)",
                    $"server=0x{serverHash:X16} client=0x{clientHash:X16} frame={frame}",
                    hashReportsSent,
                    harness.BattleLogic.HashReportsMatched,
                    harness.BattleLogic.HashMismatchCount,
                    harness.BattleLogic.HashNoRecordCount);
            }

            if (!shouldSendHashReport)
            {
                continue;
            }

            framesSinceHashReport = 0;
            hashReportsSent++;
            HashReportResult reportResult = harness.BattleLogic.TryCompareReportedHash(client.PlayerId, frame, clientHash);
            if (reportResult != HashReportResult.Matched)
            {
                return ScenarioResult.Fail(
                    $"Consistency (server-client, {options.Frames} frames)",
                    $"hash report result={reportResult} frame={frame} hash=0x{clientHash:X16}",
                    hashReportsSent,
                    harness.BattleLogic.HashReportsMatched,
                    harness.BattleLogic.HashMismatchCount,
                    harness.BattleLogic.HashNoRecordCount);
            }
        }

        return ScenarioResult.Pass(
            $"Consistency (server-client, {options.Frames} frames)",
            hashReportsSent: hashReportsSent,
            hashReportsMatched: harness.BattleLogic.HashReportsMatched,
            hashMismatchCount: harness.BattleLogic.HashMismatchCount,
            hashNoRecordCount: harness.BattleLogic.HashNoRecordCount);
    }

    private static ScenarioResult RunConvergenceScenario(TestRunOptions options)
    {
        if (options.Clients < 2)
        {
            return ScenarioResult.Fail("Convergence", "--clients must be >= 2");
        }

        uint totalFrames = Math.Max(options.Frames, StopInputFrame + 2);
        Harness harness = CreateHarness(options.Clients);
        ulong? stableHash = null;

        for (uint frame = 0; frame < totalFrames; frame++)
        {
            for (int i = 0; i < harness.Clients.Count; i++)
            {
                (float dx, float dy) = frame < StopInputFrame ? DivergentMovementScript(i, frame) : (0.0f, 0.0f);
                harness.Clients[i].SubmitInput(frame, dx, dy);
            }

            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);

            if (frame <= StopInputFrame)
            {
                continue;
            }

            ulong currentHash = harness.Clients[0].GetStateHash();
            for (int i = 1; i < harness.Clients.Count; i++)
            {
                ulong peerHash = harness.Clients[i].GetStateHash();
                if (currentHash != peerHash)
                {
                    return ScenarioResult.Fail(
                        $"Convergence (no input after frame {StopInputFrame})",
                        $"client hash mismatch frame={frame} client0=0x{currentHash:X16} client{i}=0x{peerHash:X16}");
                }
            }

            if (!stableHash.HasValue)
            {
                stableHash = currentHash;
                continue;
            }

            if (currentHash != stableHash.Value)
            {
                return ScenarioResult.Fail(
                    $"Convergence (no input after frame {StopInputFrame})",
                    $"hash drift detected frame={frame} baseline=0x{stableHash.Value:X16} current=0x{currentHash:X16}");
            }
        }

        if (!stableHash.HasValue)
        {
            return ScenarioResult.Fail(
                $"Convergence (no input after frame {StopInputFrame})",
                "stable hash was not captured");
        }

        return ScenarioResult.Pass($"Convergence (no input after frame {StopInputFrame})");
    }

    private static ScenarioResult RunPlayerCollisionScenario(TestRunOptions options)
    {
        const float stationaryPlayerX = 1.0f;
        const float minimumExpectedSeparation = 0.85f;
        uint totalFrames = Math.Max(options.Frames, 30);
        if (!RunStationaryBodyBlockScenario(
                totalFrames,
                stationaryPlayerX,
                minimumExpectedSeparation,
                out string stationaryFailure,
                out string stationaryDetail))
        {
            return ScenarioResult.Fail("PlayerCollision (MOBA body block)", stationaryFailure);
        }

        if (!RunHeadOnBodyBlockScenario(
                totalFrames,
                minimumExpectedSeparation,
                out string headOnFailure,
                out string headOnDetail))
        {
            return ScenarioResult.Fail("PlayerCollision (MOBA body block)", headOnFailure);
        }

        return ScenarioResult.Pass(
            "PlayerCollision (MOBA body block)",
            $"{stationaryDetail}; {headOnDetail}");
    }

    private static bool RunStationaryBodyBlockScenario(
        uint totalFrames,
        float stationaryPlayerX,
        float minimumExpectedSeparation,
        out string failure,
        out string successDetail)
    {
        const float moverStartX = -1.0f;
        const float stationaryMaxDisplacement = 0.01f;
        Harness harness = CreateHarness(
            2,
            playerIndex => playerIndex switch
            {
                0 => ((Fixed64)moverStartX, Fixed64.Zero),
                1 => ((Fixed64)stationaryPlayerX, Fixed64.Zero),
                _ => GetSpawnPosition(playerIndex)
            });

        bool everTouched = false;
        float minimumSeparation = float.MaxValue;
        float maximumStationaryDisplacement = 0.0f;

        for (uint frame = 0; frame < totalFrames; frame++)
        {
            harness.Clients[0].SubmitInput(frame, 1.0f, 0.0f);
            harness.Clients[1].SubmitInput(frame, 0.0f, 0.0f);
            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);

            if (!TryGetOrderedPlayers(harness.Clients[0].WorldState, 1, 2, out PlayerState mover, out PlayerState blocker))
            {
                failure = $"stationary-block players missing frame={frame}";
                successDetail = string.Empty;
                return false;
            }

            float separation = (float)(blocker.X - mover.X);
            minimumSeparation = Math.Min(minimumSeparation, separation);
            maximumStationaryDisplacement = Math.Max(maximumStationaryDisplacement, Math.Abs((float)blocker.X - stationaryPlayerX));

            PhysicsWorldSnapshot physicsSnapshot = harness.Clients[0].WorldState.TakeSnapshot().PhysicsSnapshot;
            if (physicsSnapshot != null && physicsSnapshot.Contacts.Count > 0)
            {
                everTouched = true;
            }
        }

        ulong serverHash = harness.BattleLogic.GetStateHash();
        ulong clientHash = harness.Clients[0].GetStateHash();
        if (serverHash != clientHash)
        {
            failure = $"stationary-block hash mismatch server=0x{serverHash:X16} client=0x{clientHash:X16}";
            successDetail = string.Empty;
            return false;
        }

        if (!everTouched)
        {
            failure = "stationary-block no contact recorded";
            successDetail = string.Empty;
            return false;
        }

        if (minimumSeparation < minimumExpectedSeparation)
        {
            failure = $"stationary-block penetration minSeparation={minimumSeparation:F4}";
            successDetail = string.Empty;
            return false;
        }

        if (maximumStationaryDisplacement > stationaryMaxDisplacement)
        {
            failure = $"stationary-block pushed blocker displacement={maximumStationaryDisplacement:F4}";
            successDetail = string.Empty;
            return false;
        }

        successDetail = $"stationaryDisp={maximumStationaryDisplacement:F4} minSeparation={minimumSeparation:F4}";
        failure = string.Empty;
        return true;
    }

    private static bool SkillTriggerBuff()
    {
        int skillId = BattleSkillGraphLibrary.ResolveConfiguredSkillId();
        int expectedBuffId = BattleSkillGraphLibrary.ResolveConfiguredBuffId();
        BattleLogic battleLogic = new BattleLogic(skillGraphs: BattleSkillGraphLibrary.CreateBuiltInGraphs());
        PlayerState player = battleLogic.JoinPlayer(1, Fixed64.Zero, Fixed64.Zero);

        battleLogic.SubmitInput(1, 5, 1, 0.0f, 0.0f, skillId);
        for (uint frame = 0; frame <= 5; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);
        }

        return player.ActiveBuffs.Count == 1 &&
               player.ActiveBuffs[0].BuffId == expectedBuffId &&
               player.ActiveBuffs[0].CasterId == player.PlayerId &&
               player.ActiveBuffs[0].TargetId == player.PlayerId;
    }

    private static bool RunHeadOnBodyBlockScenario(
        uint totalFrames,
        float minimumExpectedSeparation,
        out string failure,
        out string successDetail)
    {
        const float initialOffset = 1.0f;
        Harness harness = CreateHarness(
            2,
            playerIndex => playerIndex switch
            {
                0 => ((Fixed64)(-initialOffset), Fixed64.Zero),
                1 => ((Fixed64)initialOffset, Fixed64.Zero),
                _ => GetSpawnPosition(playerIndex)
            });

        bool everTouched = false;
        float minimumSeparation = float.MaxValue;

        for (uint frame = 0; frame < totalFrames; frame++)
        {
            harness.Clients[0].SubmitInput(frame, 1.0f, 0.0f);
            harness.Clients[1].SubmitInput(frame, -1.0f, 0.0f);
            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);

            if (!TryGetOrderedPlayers(harness.Clients[0].WorldState, 1, 2, out PlayerState leftPlayer, out PlayerState rightPlayer))
            {
                failure = $"head-on players missing frame={frame}";
                successDetail = string.Empty;
                return false;
            }

            float separation = (float)(rightPlayer.X - leftPlayer.X);
            minimumSeparation = Math.Min(minimumSeparation, separation);

            PhysicsWorldSnapshot physicsSnapshot = harness.Clients[0].WorldState.TakeSnapshot().PhysicsSnapshot;
            if (physicsSnapshot != null && physicsSnapshot.Contacts.Count > 0)
            {
                everTouched = true;
            }
        }

        if (!TryGetOrderedPlayers(harness.Clients[0].WorldState, 1, 2, out PlayerState finalLeftPlayer, out PlayerState finalRightPlayer))
        {
            failure = "head-on players missing at final frame";
            successDetail = string.Empty;
            return false;
        }

        float finalSeparation = (float)(finalRightPlayer.X - finalLeftPlayer.X);
        ulong serverHash = harness.BattleLogic.GetStateHash();
        ulong clientHash = harness.Clients[0].GetStateHash();
        if (serverHash != clientHash)
        {
            failure = $"head-on hash mismatch server=0x{serverHash:X16} client=0x{clientHash:X16}";
            successDetail = string.Empty;
            return false;
        }

        if (!everTouched)
        {
            failure = "head-on no contact recorded";
            successDetail = string.Empty;
            return false;
        }

        if (minimumSeparation < minimumExpectedSeparation)
        {
            failure = $"head-on penetration minSeparation={minimumSeparation:F4}";
            successDetail = string.Empty;
            return false;
        }

        if (finalSeparation < minimumExpectedSeparation)
        {
            failure = $"head-on final separation too small finalSeparation={finalSeparation:F4}";
            successDetail = string.Empty;
            return false;
        }

        if (finalLeftPlayer.X >= finalRightPlayer.X)
        {
            failure = $"head-on player order inverted left={finalLeftPlayer.X:F4} right={finalRightPlayer.X:F4}";
            successDetail = string.Empty;
            return false;
        }

        successDetail = $"headOnMin={minimumSeparation:F4} headOnFinal={finalSeparation:F4}";
        failure = string.Empty;
        return true;
    }

    private static ScenarioResult RunPlayerLeaveScenario(TestRunOptions options)
    {
        uint totalFrames = Math.Max(options.Frames, LeaveFrame + 51);
        Harness harness = CreateHarness(Math.Max(2, options.Clients));
        long playerA = harness.Clients[0].PlayerId;
        long playerB = harness.Clients[1].PlayerId;
        Fixed64 beforeLeaveX = Fixed64.Zero;
        bool capturedBeforeLeave = false;

        for (uint frame = 0; frame < totalFrames; frame++)
        {
            if (frame == LeaveFrame)
            {
                harness.BattleLogic.RemovePlayer(playerB);
            }

            harness.Clients[0].SubmitInput(frame, 1.0f, 0.0f);
            if (frame < LeaveFrame)
            {
                harness.Clients[1].SubmitInput(frame, -1.0f, 0.0f);
            }

            for (int i = 2; i < harness.Clients.Count; i++)
            {
                (float dx, float dy) = SharedMovementScript(frame);
                harness.Clients[i].SubmitInput(frame, dx, dy);
            }

            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTimeFixed64);

            if (frame != LeaveFrame - 1)
            {
                continue;
            }

            if (!harness.Clients[0].WorldState.TryGetPlayer(playerA, out PlayerState beforeLeaveState))
            {
                return ScenarioResult.Fail("PlayerLeave (player B leaves at frame 100)", "player A missing before leave");
            }

            beforeLeaveX = beforeLeaveState.X;
            capturedBeforeLeave = true;
        }

        if (!capturedBeforeLeave)
        {
            return ScenarioResult.Fail("PlayerLeave (player B leaves at frame 100)", "failed to capture player A state before leave");
        }

        if (!harness.Clients[0].WorldState.TryGetPlayer(playerA, out PlayerState finalState))
        {
            return ScenarioResult.Fail("PlayerLeave (player B leaves at frame 100)", "player A missing after leave");
        }

        if (harness.Clients[0].WorldState.TryGetPlayer(playerB, out _))
        {
            return ScenarioResult.Fail("PlayerLeave (player B leaves at frame 100)", "player B still exists on client");
        }

        if (harness.BattleLogic.HasPlayer(playerB))
        {
            return ScenarioResult.Fail("PlayerLeave (player B leaves at frame 100)", "player B still exists on server");
        }

        if (finalState.X <= beforeLeaveX)
        {
            return ScenarioResult.Fail(
                "PlayerLeave (player B leaves at frame 100)",
                $"player A stopped moving after leave beforeX={(float)beforeLeaveX:F3} afterX={(float)finalState.X:F3}");
        }

        ulong serverHash = harness.BattleLogic.GetStateHash();
        ulong clientHash = harness.Clients[0].GetStateHash();
        if (serverHash != clientHash)
        {
            return ScenarioResult.Fail(
                "PlayerLeave (player B leaves at frame 100)",
                $"server=0x{serverHash:X16} client=0x{clientHash:X16}");
        }

        return ScenarioResult.Pass("PlayerLeave (player B leaves at frame 100)");
    }

    private static bool TryGetOrderedPlayers(
        BattleWorldState worldState,
        long leftPlayerId,
        long rightPlayerId,
        out PlayerState leftPlayer,
        out PlayerState rightPlayer)
    {
        leftPlayer = null;
        rightPlayer = null;
        return worldState.TryGetPlayer(leftPlayerId, out leftPlayer) &&
               worldState.TryGetPlayer(rightPlayerId, out rightPlayer);
    }

    private static Harness CreateHarness(int clientCount, Func<int, (Fixed64 x, Fixed64 y)> spawnProvider = null)
    {
        BattleLogic battleLogic = new BattleLogic();
        List<SimulatedClient> clients = new(clientCount);

        battleLogic.OnBroadcast = snapshot =>
        {
            for (int i = 0; i < clients.Count; i++)
            {
                clients[i].ApplySnapshot(snapshot);
            }
        };

        for (int i = 0; i < clientCount; i++)
        {
            long playerId = i + 1L;
            SimulatedClient client = new(
                playerId,
                (id, frameIndex, inputSeq, dx, dy) => battleLogic.SubmitInput(id, frameIndex, inputSeq, dx, dy));
            clients.Add(client);

            (Fixed64 x, Fixed64 y) = spawnProvider != null ? spawnProvider(i) : GetSpawnPosition(i);
            battleLogic.JoinPlayer(playerId, x, y);
        }

        return new Harness(battleLogic, clients);
    }

    private static bool NearZero(Fixed64 value)
    {
        return FixedMath.Abs(value) < (Fixed64)0.0001f;
    }

    private static bool Near(Fixed64 actual, Fixed64 expected)
    {
        return FixedMath.Abs(actual - expected) < (Fixed64)0.0001f;
    }

    private static (float dx, float dy) SharedMovementScript(uint frame)
    {
        return ((frame / 30) % 4) switch
        {
            0 => (1.0f, 0.0f),
            1 => (0.0f, 1.0f),
            2 => (-1.0f, 0.0f),
            _ => (0.0f, -1.0f)
        };
    }

    private static (float dx, float dy) DivergentMovementScript(int clientIndex, uint frame)
    {
        if ((frame & 1) == 0)
        {
            return clientIndex == 0 ? (1.0f, 0.0f) : (-1.0f, 0.0f);
        }

        return clientIndex == 0 ? (0.0f, 1.0f) : (0.0f, -1.0f);
    }

    private static (Fixed64 x, Fixed64 y) GetSpawnPosition(int playerCount)
    {
        Fixed64 spacing = (Fixed64)3;
        int row = playerCount / 2;
        Fixed64 x = (playerCount & 1) == 0 ? -spacing : spacing;
        Fixed64 y = (Fixed64)row * spacing;
        return (x, y);
    }

    private readonly struct Harness
    {
        public Harness(BattleLogic battleLogic, List<SimulatedClient> clients)
        {
            BattleLogic = battleLogic;
            Clients = clients;
        }

        public BattleLogic BattleLogic { get; }
        public List<SimulatedClient> Clients { get; }
    }

    private readonly struct CheckResult
    {
        private CheckResult(string suiteName, string name, bool passed, string details)
        {
            SuiteName = suiteName;
            Name = name;
            Passed = passed;
            Details = details;
        }

        public string SuiteName { get; }
        public string Name { get; }
        public bool Passed { get; }
        public string Details { get; }

        public static CheckResult Pass(string suiteName, string name, string details = "")
        {
            return new CheckResult(suiteName, name, true, details);
        }

        public static CheckResult Fail(string suiteName, string name, string details)
        {
            return new CheckResult(suiteName, name, false, details);
        }
    }

    private readonly struct ScenarioResult
    {
        private ScenarioResult(
            string name,
            bool passed,
            string details,
            int hashReportsSent,
            int hashReportsMatched,
            int hashMismatchCount,
            int hashNoRecordCount)
        {
            Name = name;
            Passed = passed;
            Details = details;
            HashReportsSent = hashReportsSent;
            HashReportsMatched = hashReportsMatched;
            HashMismatchCount = hashMismatchCount;
            HashNoRecordCount = hashNoRecordCount;
        }

        public string Name { get; }
        public bool Passed { get; }
        public string Details { get; }
        public int HashReportsSent { get; }
        public int HashReportsMatched { get; }
        public int HashMismatchCount { get; }
        public int HashNoRecordCount { get; }

        public static ScenarioResult Pass(
            string name,
            string details = "",
            int hashReportsSent = 0,
            int hashReportsMatched = 0,
            int hashMismatchCount = 0,
            int hashNoRecordCount = 0)
        {
            return new ScenarioResult(
                name,
                true,
                details,
                hashReportsSent,
                hashReportsMatched,
                hashMismatchCount,
                hashNoRecordCount);
        }

        public static ScenarioResult Fail(
            string name,
            string details,
            int hashReportsSent = 0,
            int hashReportsMatched = 0,
            int hashMismatchCount = 0,
            int hashNoRecordCount = 0)
        {
            return new ScenarioResult(
                name,
                false,
                details,
                hashReportsSent,
                hashReportsMatched,
                hashMismatchCount,
                hashNoRecordCount);
        }
    }

    private sealed class TestExecutionReport
    {
        public TestExecutionReport(TestRunOptions options, List<CheckResult> checks, List<ScenarioResult> scenarios)
        {
            Options = options;
            Checks = checks;
            Scenarios = scenarios;
            GeneratedAt = DateTimeOffset.Now;
        }

        public TestRunOptions Options { get; }
        public List<CheckResult> Checks { get; }
        public List<ScenarioResult> Scenarios { get; }
        public DateTimeOffset GeneratedAt { get; }
        public int PassedChecks => CountPassedChecks();
        public int PassedScenarios => CountPassedScenarios();
        public int HashReportsSent => SumScenarioMetric(static scenario => scenario.HashReportsSent);
        public int HashReportsMatched => SumScenarioMetric(static scenario => scenario.HashReportsMatched);
        public int HashMismatchCount => SumScenarioMetric(static scenario => scenario.HashMismatchCount);
        public int HashNoRecordCount => SumScenarioMetric(static scenario => scenario.HashNoRecordCount);
        public bool Passed => PassedChecks == Checks.Count && PassedScenarios == Scenarios.Count;

        private int CountPassedChecks()
        {
            int passed = 0;
            for (int i = 0; i < Checks.Count; i++)
            {
                if (Checks[i].Passed)
                {
                    passed++;
                }
            }

            return passed;
        }

        private int CountPassedScenarios()
        {
            int passed = 0;
            for (int i = 0; i < Scenarios.Count; i++)
            {
                if (Scenarios[i].Passed)
                {
                    passed++;
                }
            }

            return passed;
        }

        private int SumScenarioMetric(Func<ScenarioResult, int> selector)
        {
            int total = 0;
            for (int i = 0; i < Scenarios.Count; i++)
            {
                total += selector(Scenarios[i]);
            }

            return total;
        }
    }

    private sealed class TestRunOptions
    {
        private TestRunOptions(string scenario, uint frames, int clients, TestOutputFormat output, string reportPath)
        {
            Scenario = scenario;
            Frames = frames;
            Clients = clients;
            Output = output;
            ReportPath = reportPath;
        }

        public string Scenario { get; }
        public uint Frames { get; }
        public int Clients { get; }
        public TestOutputFormat Output { get; }
        public string ReportPath { get; }

        public static TestRunOptions Parse(string[] args)
        {
            string scenario = TestScenario.All;
            uint frames = DefaultTotalFrames;
            int clients = 2;
            TestOutputFormat output = TestOutputFormat.Console;
            string reportPath = string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--test", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (CommandLineOptionReader.TryReadOption(args, ref i, "--mode", out string modeValue))
                {
                    if (modeValue.Equals("test", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    continue;
                }

                if (CommandLineOptionReader.TryReadOption(args, ref i, "--scenario", out string scenarioValue))
                {
                    scenario = NormalizeScenario(scenarioValue);
                    continue;
                }

                if (CommandLineOptionReader.TryReadOption(args, ref i, "--frames", out string framesValue))
                {
                    if (!uint.TryParse(framesValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) || frames == 0)
                    {
                        throw new ArgumentException($"Invalid --frames value: {framesValue}");
                    }

                    continue;
                }

                if (CommandLineOptionReader.TryReadOption(args, ref i, "--clients", out string clientsValue))
                {
                    if (!int.TryParse(clientsValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out clients) || clients <= 0)
                    {
                        throw new ArgumentException($"Invalid --clients value: {clientsValue}");
                    }

                    continue;
                }

                if (CommandLineOptionReader.TryReadOption(args, ref i, "--output", out string outputValue))
                {
                    output = outputValue.ToLowerInvariant() switch
                    {
                        "console" => TestOutputFormat.Console,
                        "markdown" => TestOutputFormat.Markdown,
                        "json" => TestOutputFormat.Json,
                        _ => throw new ArgumentException($"Invalid --output value: {outputValue}")
                    };
                    continue;
                }

                if (CommandLineOptionReader.TryReadOption(args, ref i, "--report", out string reportValue))
                {
                    reportPath = Path.IsPathRooted(reportValue)
                        ? reportValue
                        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), reportValue));
                    continue;
                }
            }

            return new TestRunOptions(scenario, frames, clients, output, reportPath);
        }

        private static string NormalizeScenario(string scenario)
        {
            string normalized = scenario?.Trim().ToLowerInvariant() ?? string.Empty;
            return normalized switch
            {
                "" => TestScenario.All,
                "all" => TestScenario.All,
                "snapshot-self" => TestScenario.SnapshotSelf,
                "basic-roundtrip" => TestScenario.BasicRoundTrip,
                "capacity-eviction" => TestScenario.CapacityEviction,
                "same-frame-override" => TestScenario.SameFrameOverride,
                "hash-normalization" => TestScenario.HashNormalization,
                "physics-roundtrip" => TestScenario.PhysicsRoundTrip,
                "physics-affects-hash" => TestScenario.PhysicsAffectsHash,
                "snapshot-attribute-roundtrip" => TestScenario.SnapshotAttributeRoundTrip,
                "attribute-roundtrip" => TestScenario.SnapshotAttributeRoundTrip,
                "attributes-affect-hash" => TestScenario.AttributesAffectHash,
                "attribute-dirty-merge" => TestScenario.AttributeDirtyMerge,
                "buff-roundtrip" => TestScenario.BuffRoundTrip,
                "buffs-affect-hash" => TestScenario.BuffsAffectHash,
                "runtime-buff-id-roundtrip" => TestScenario.RuntimeBuffIdRoundTrip,
                "skill-buff-roundtrip" => TestScenario.SkillBuffRoundTrip,
                "stack-overlay-roundtrip" => TestScenario.StackOverlayRoundTrip,
                "refresh-overlay-roundtrip" => TestScenario.RefreshOverlayRoundTrip,
                "mutex-replace-roundtrip" => TestScenario.MutexReplaceRoundTrip,
                "mutex-reject-roundtrip" => TestScenario.MutexRejectRoundTrip,
                "mutex-same-priority" => TestScenario.MutexSamePriority,
                "mutex-same-frame-two-commands" => TestScenario.MutexSameFrameTwoCommands,
                "stackable-bridge-upgrade" => TestScenario.StackableBridgeUpgrade,
                "frame-schedule" => TestScenario.FrameSchedule,
                "future-frame-input-applies-on-target-frame" => TestScenario.FutureFrameInputAppliesOnTargetFrame,
                "server-input" => TestScenario.ServerInput,
                "expired-input-is-dropped" => TestScenario.ExpiredInputIsDropped,
                "future-input-is-buffered" => TestScenario.FutureInputIsBuffered,
                "too-far-future-input-is-rejected" => TestScenario.TooFarFutureInputIsRejected,
                "missing-input-reuses-last" => TestScenario.MissingInputReusesLast,
                "prediction-self" => TestScenario.PredictionSelf,
                "join-aligns-global-frame" => TestScenario.JoinAlignsGlobalFrame,
                "frame-index-is-global" => TestScenario.FrameIndexIsGlobal,
                "input-normalization" => TestScenario.InputNormalization,
                "prediction-moves-self-player" => TestScenario.PredictionMovesSelfPlayer,
                "catchup-target-is-auth-plus-lead" => TestScenario.CatchUpTargetIsAuthPlusLead,
                "queued-snapshots-apply-in-order" => TestScenario.QueuedSnapshotsApplyInOrder,
                "consistency-hit" => TestScenario.ConsistencyHit,
                "consistency-miss" => TestScenario.ConsistencyMiss,
                "prediction-buffer-uses-global-frame" => TestScenario.PredictionBufferUsesGlobalFrame,
                "skipped-no-record" => TestScenario.SkippedNoRecord,
                "eviction-does-not-crash" => TestScenario.EvictionDoesNotCrash,
                "accepted-input-feedback-raises-lead" => TestScenario.AcceptedInputFeedbackRaisesLead,
                "authoritative-snapshot-restores-physics-world" => TestScenario.AuthoritativeSnapshotRestoresPhysicsWorld,
                "authoritative-snapshot-restores-player-attributes" => TestScenario.AuthoritativeSnapshotRestoresPlayerAttributes,
                "authoritative-snapshot-restores-attributes" => TestScenario.AuthoritativeSnapshotRestoresPlayerAttributes,
                "authoritative-snapshot-restores-player-buffs" => TestScenario.AuthoritativeSnapshotRestoresPlayerBuffs,
                "predicted-buff-consistency-hit" => TestScenario.PredictedBuffConsistencyHit,
                "rollback-replays-before-next-consistency-check" => TestScenario.RollbackReplaysBeforeNextConsistencyCheck,
                "manual-rollback-replays-authoritative-history" => TestScenario.ManualRollbackReplaysAuthoritativeHistory,
                "skill-trigger-buff" => TestScenario.SkillTriggerBuff,
                "determinism" => TestScenario.Determinism,
                "consistency" => TestScenario.Consistency,
                "convergence" => TestScenario.Convergence,
                "player-collision" => TestScenario.PlayerCollision,
                "player-leave" => TestScenario.PlayerLeave,
                _ => throw new ArgumentException($"Unknown --scenario value: {scenario}")
            };
        }
    }

    private enum TestOutputFormat
    {
        Console,
        Markdown,
        Json
    }

    private static class TestScenario
    {
        public const string All = "all";
        public const string SnapshotSelf = "snapshot-self";
        public const string BasicRoundTrip = "basic-roundtrip";
        public const string CapacityEviction = "capacity-eviction";
        public const string SameFrameOverride = "same-frame-override";
        public const string HashNormalization = "hash-normalization";
        public const string PhysicsRoundTrip = "physics-roundtrip";
        public const string PhysicsAffectsHash = "physics-affects-hash";
        public const string SnapshotAttributeRoundTrip = "snapshot-attribute-roundtrip";
        public const string AttributesAffectHash = "attributes-affect-hash";
        public const string AttributeDirtyMerge = "attribute-dirty-merge";
        public const string BuffRoundTrip = "buff-roundtrip";
        public const string BuffsAffectHash = "buffs-affect-hash";
        public const string RuntimeBuffIdRoundTrip = "runtime-buff-id-roundtrip";
        public const string SkillBuffRoundTrip = "skill-buff-roundtrip";
        public const string StackOverlayRoundTrip = "stack-overlay-roundtrip";
        public const string RefreshOverlayRoundTrip = "refresh-overlay-roundtrip";
        public const string MutexReplaceRoundTrip = "mutex-replace-roundtrip";
        public const string MutexRejectRoundTrip = "mutex-reject-roundtrip";
        public const string MutexSamePriority = "mutex-same-priority";
        public const string MutexSameFrameTwoCommands = "mutex-same-frame-two-commands";
        public const string StackableBridgeUpgrade = "stackable-bridge-upgrade";
        public const string FrameSchedule = "frame-schedule";
        public const string FutureFrameInputAppliesOnTargetFrame = "future-frame-input-applies-on-target-frame";
        public const string ServerInput = "server-input";
        public const string ExpiredInputIsDropped = "expired-input-is-dropped";
        public const string FutureInputIsBuffered = "future-input-is-buffered";
        public const string TooFarFutureInputIsRejected = "too-far-future-input-is-rejected";
        public const string MissingInputReusesLast = "missing-input-reuses-last";
        public const string PredictionSelf = "prediction-self";
        public const string JoinAlignsGlobalFrame = "join-aligns-global-frame";
        public const string FrameIndexIsGlobal = "frame-index-is-global";
        public const string InputNormalization = "input-normalization";
        public const string PredictionMovesSelfPlayer = "prediction-moves-self-player";
        public const string CatchUpTargetIsAuthPlusLead = "catchup-target-is-auth-plus-lead";
        public const string QueuedSnapshotsApplyInOrder = "queued-snapshots-apply-in-order";
        public const string ConsistencyHit = "consistency-hit";
        public const string ConsistencyMiss = "consistency-miss";
        public const string PredictionBufferUsesGlobalFrame = "prediction-buffer-uses-global-frame";
        public const string SkippedNoRecord = "skipped-no-record";
        public const string EvictionDoesNotCrash = "eviction-does-not-crash";
        public const string AcceptedInputFeedbackRaisesLead = "accepted-input-feedback-raises-lead";
        public const string AuthoritativeSnapshotRestoresPhysicsWorld = "authoritative-snapshot-restores-physics-world";
        public const string AuthoritativeSnapshotRestoresPlayerAttributes = "authoritative-snapshot-restores-player-attributes";
        public const string AuthoritativeSnapshotRestoresPlayerBuffs = "authoritative-snapshot-restores-player-buffs";
        public const string PredictedBuffConsistencyHit = "predicted-buff-consistency-hit";
        public const string RollbackReplaysBeforeNextConsistencyCheck = "rollback-replays-before-next-consistency-check";
        public const string ManualRollbackReplaysAuthoritativeHistory = "manual-rollback-replays-authoritative-history";
        public const string SkillTriggerBuff = "skill-trigger-buff";
        public const string Determinism = "determinism";
        public const string Consistency = "consistency";
        public const string Convergence = "convergence";
        public const string PlayerCollision = "player-collision";
        public const string PlayerLeave = "player-leave";
    }
}
