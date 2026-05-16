using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using GameLogic;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Snapshot;

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
        TestRunOptions options = TestRunOptions.Parse(args);
        TestExecutionReport report = Execute(options);

        PrintConsoleReport(report);
        EmitStructuredReport(report, options);
        return report.Passed ? 0 : 1;
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
                scenarios.Add(RunConvergenceScenario(options));
                scenarios.Add(RunPlayerLeaveScenario(options));
                break;

            case TestScenario.SnapshotSelf:
                checks.Add(RunSnapshotSelfTest());
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
                checks.Add(RunPredictionSelfCase(options.Scenario));
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

            case TestScenario.PlayerLeave:
                scenarios.Add(RunPlayerLeaveScenario(options));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Scenario, "Unknown test scenario.");
        }

        return new TestExecutionReport(options, checks, scenarios);
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
            builder.AppendLine($"- {check.SuiteName}: {(check.Passed ? "PASS" : "FAIL")} {check.Details}".TrimEnd());
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
                builder.AppendLine($"- `{check.SuiteName}`：`{(check.Passed ? "通过" : "失败")}` {check.Details}".TrimEnd());
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

    private static string BuildJsonReport(TestExecutionReport report)
    {
        var payload = new
        {
            generatedAt = report.GeneratedAt.ToString("O", CultureInfo.InvariantCulture),
            passed = report.Passed,
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
                details = scenario.Details
            }),
            summary = new
            {
                passedChecks = report.PassedChecks,
                totalChecks = report.Checks.Count,
                passedScenarios = report.PassedScenarios,
                totalScenarios = report.Scenarios.Count
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
        PlayerState state = battleLogic.JoinPlayer(1, 0.0f, 0.0f);

        battleLogic.SubmitInput(1, 5, 1, 1.0f, 0.0f);
        for (uint frame = 0; frame < 5; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);
            if (state.X != 0.0f)
            {
                return false;
            }
        }

        battleLogic.Tick(5, DeterminismRules.FixedDeltaTime);
        float expectedX = DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTime;
        return Math.Abs(state.X - expectedX) < 0.0001f;
    }

    private static bool ExpiredInputIsDropped()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, 0.0f, 0.0f);

        battleLogic.Tick(0, DeterminismRules.FixedDeltaTime);
        battleLogic.SubmitInput(1, 0, 1, 1.0f, 0.0f);
        battleLogic.Tick(1, DeterminismRules.FixedDeltaTime);

        return Math.Abs(state.X) < 0.0001f && battleLogic.LateInputDropCount == 1;
    }

    private static bool FutureInputIsBuffered()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, 0.0f, 0.0f);

        battleLogic.SubmitInput(1, 5, 1, 1.0f, 0.0f);
        for (uint frame = 0; frame < 5; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);
            if (Math.Abs(state.X) >= 0.0001f)
            {
                return false;
            }
        }

        battleLogic.Tick(5, DeterminismRules.FixedDeltaTime);
        float expectedX = DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTime;
        return Math.Abs(state.X - expectedX) < 0.0001f &&
               battleLogic.AcceptedInputCount == 1 &&
               battleLogic.FutureInputRejectCount == 0;
    }

    private static bool TooFarFutureInputIsRejected()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, 0.0f, 0.0f);

        battleLogic.SubmitInput(1, 17, 1, 1.0f, 0.0f);
        for (uint frame = 0; frame <= 17; frame++)
        {
            battleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);
        }

        return Math.Abs(state.X) < 0.0001f && battleLogic.FutureInputRejectCount == 1;
    }

    private static bool MissingInputReusesLast()
    {
        BattleLogic battleLogic = new BattleLogic();
        PlayerState state = battleLogic.JoinPlayer(1, 0.0f, 0.0f);

        battleLogic.Tick(0, DeterminismRules.FixedDeltaTime);
        if (Math.Abs(state.X) >= 0.0001f)
        {
            return false;
        }

        battleLogic.SubmitInput(1, 1, 1, 1.0f, 0.0f);
        battleLogic.Tick(1, DeterminismRules.FixedDeltaTime);
        float afterFirstTick = state.X;

        battleLogic.Tick(2, DeterminismRules.FixedDeltaTime);
        float expectedX = afterFirstTick + (DeterminismRules.MoveSpeed * DeterminismRules.FixedDeltaTime);
        return Math.Abs(state.X - expectedX) < 0.0001f &&
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

            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);
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

        for (uint frame = 0; frame < options.Frames; frame++)
        {
            (float dx, float dy) = SharedMovementScript(frame);
            client.SubmitInput(frame, dx, dy);
            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);

            if (frame % 30 != 0)
            {
                continue;
            }

            ulong serverHash = harness.BattleLogic.GetStateHash();
            ulong clientHash = client.GetStateHash();
            if (serverHash != clientHash)
            {
                return ScenarioResult.Fail(
                    $"Consistency (server-client, {options.Frames} frames)",
                    $"server=0x{serverHash:X16} client=0x{clientHash:X16} frame={frame}");
            }
        }

        return ScenarioResult.Pass($"Consistency (server-client, {options.Frames} frames)");
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

            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);

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

    private static ScenarioResult RunPlayerLeaveScenario(TestRunOptions options)
    {
        uint totalFrames = Math.Max(options.Frames, LeaveFrame + 51);
        Harness harness = CreateHarness(Math.Max(2, options.Clients));
        long playerA = harness.Clients[0].PlayerId;
        long playerB = harness.Clients[1].PlayerId;
        float beforeLeaveX = 0.0f;
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

            harness.BattleLogic.Tick(frame, DeterminismRules.FixedDeltaTime);

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
                $"player A stopped moving after leave beforeX={beforeLeaveX:F3} afterX={finalState.X:F3}");
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

    private static Harness CreateHarness(int clientCount)
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

            (float x, float y) = GetSpawnPosition(i);
            battleLogic.JoinPlayer(playerId, x, y);
        }

        return new Harness(battleLogic, clients);
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

    private static (float x, float y) GetSpawnPosition(int playerCount)
    {
        const float spacing = 3.0f;
        int row = playerCount / 2;
        float x = (playerCount & 1) == 0 ? -spacing : spacing;
        float y = row * spacing;
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
        private ScenarioResult(string name, bool passed, string details)
        {
            Name = name;
            Passed = passed;
            Details = details;
        }

        public string Name { get; }
        public bool Passed { get; }
        public string Details { get; }

        public static ScenarioResult Pass(string name, string details = "")
        {
            return new ScenarioResult(name, true, details);
        }

        public static ScenarioResult Fail(string name, string details)
        {
            return new ScenarioResult(name, false, details);
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
                if (arg.Equals("--test", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--mode=test", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (arg.Equals("--scenario", StringComparison.OrdinalIgnoreCase))
                {
                    scenario = NormalizeScenario(ReadRequiredValue(args, ref i, "--scenario"));
                    continue;
                }

                if (arg.Equals("--frames", StringComparison.OrdinalIgnoreCase))
                {
                    string value = ReadRequiredValue(args, ref i, "--frames");
                    if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out frames) || frames == 0)
                    {
                        throw new ArgumentException($"Invalid --frames value: {value}");
                    }

                    continue;
                }

                if (arg.Equals("--clients", StringComparison.OrdinalIgnoreCase))
                {
                    string value = ReadRequiredValue(args, ref i, "--clients");
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out clients) || clients <= 0)
                    {
                        throw new ArgumentException($"Invalid --clients value: {value}");
                    }

                    continue;
                }

                if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase))
                {
                    string value = ReadRequiredValue(args, ref i, "--output");
                    output = value.ToLowerInvariant() switch
                    {
                        "console" => TestOutputFormat.Console,
                        "markdown" => TestOutputFormat.Markdown,
                        "json" => TestOutputFormat.Json,
                        _ => throw new ArgumentException($"Invalid --output value: {value}")
                    };
                    continue;
                }

                if (arg.Equals("--report", StringComparison.OrdinalIgnoreCase))
                {
                    string value = ReadRequiredValue(args, ref i, "--report");
                    reportPath = Path.IsPathRooted(value)
                        ? value
                        : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), value));
                    continue;
                }
            }

            return new TestRunOptions(scenario, frames, clients, output, reportPath);
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

        private static string NormalizeScenario(string scenario)
        {
            string normalized = scenario?.Trim().ToLowerInvariant() ?? string.Empty;
            return normalized switch
            {
                "" => TestScenario.All,
                "all" => TestScenario.All,
                "snapshot-self" => TestScenario.SnapshotSelf,
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
                "determinism" => TestScenario.Determinism,
                "consistency" => TestScenario.Consistency,
                "convergence" => TestScenario.Convergence,
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
        public const string Determinism = "determinism";
        public const string Consistency = "consistency";
        public const string Convergence = "convergence";
        public const string PlayerLeave = "player-leave";
    }
}
