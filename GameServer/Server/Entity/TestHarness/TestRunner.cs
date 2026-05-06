using System;
using System.Collections.Generic;
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
        bool snapshotPassed = RunSnapshotSelfTest();
        bool frameSchedulePassed = RunFrameScheduleSelfTest();
        bool serverInputModelPassed = RunServerInputModelSelfTest();

        Func<ScenarioResult>[] scenarios =
        {
            RunDeterminismScenario,
            RunServerClientConsistencyScenario,
            RunConvergenceScenario,
            RunPlayerLeaveScenario
        };

        Console.WriteLine($"[TestSuite] Running {scenarios.Length} scenarios...");

        int passedCount = 0;
        for (int i = 0; i < scenarios.Length; i++)
        {
            ScenarioResult result = scenarios[i]();
            Console.WriteLine($"[Scenario {i + 1}] {result.Name} ... {(result.Passed ? "PASS" : "FAIL")} {result.Details}".TrimEnd());
            if (result.Passed)
            {
                passedCount++;
            }
        }

        Console.WriteLine($"[TestSuite] {passedCount}/{scenarios.Length} PASS");
        return snapshotPassed && frameSchedulePassed && serverInputModelPassed && passedCount == scenarios.Length ? 0 : 1;
    }

    private static bool RunSnapshotSelfTest()
    {
        bool passed = SnapshotSelfTestSuite.Run(out string failedCase);
        Console.WriteLine(passed ? "[SnapshotTest] ALL PASS" : $"[SnapshotTest] FAIL: {failedCase}");
        return passed;
    }

    private static bool RunFrameScheduleSelfTest()
    {
        bool passed = FutureFrameInputAppliesOnTargetFrame();
        Console.WriteLine(passed ? "[FrameScheduleTest] ALL PASS" : "[FrameScheduleTest] FAIL: future-frame-input-applies-on-target-frame");
        return passed;
    }

    private static bool RunServerInputModelSelfTest()
    {
        bool expiredPassed = ExpiredInputIsDropped();
        bool futureBufferedPassed = FutureInputIsBuffered();
        bool tooFarRejectedPassed = TooFarFutureInputIsRejected();
        bool reusePassed = MissingInputReusesLast();

        if (expiredPassed && futureBufferedPassed && tooFarRejectedPassed && reusePassed)
        {
            Console.WriteLine("[ServerInputTest] ALL PASS");
            return true;
        }

        if (!expiredPassed)
        {
            Console.WriteLine("[ServerInputTest] FAIL: expired-input-is-dropped");
        }

        if (!futureBufferedPassed)
        {
            Console.WriteLine("[ServerInputTest] FAIL: future-input-is-buffered");
        }

        if (!tooFarRejectedPassed)
        {
            Console.WriteLine("[ServerInputTest] FAIL: too-far-future-input-is-rejected");
        }

        if (!reusePassed)
        {
            Console.WriteLine("[ServerInputTest] FAIL: missing-input-reuses-last");
        }

        return false;
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

        return Math.Abs(state.X) < 0.0001f;
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
        return Math.Abs(state.X - expectedX) < 0.0001f;
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

        return Math.Abs(state.X) < 0.0001f;
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
        return Math.Abs(state.X - expectedX) < 0.0001f;
    }

    private static ScenarioResult RunDeterminismScenario()
    {
        Harness harness = CreateHarness(clientCount: 2);

        for (uint frame = 0; frame < DefaultTotalFrames; frame++)
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
                    "Determinism (2 clients, 300 frames)",
                    $"Client[0].hash=0x{expectedHash:X16} Client[{i}].hash=0x{actualHash:X16} frame={DefaultTotalFrames - 1}");
            }
        }

        return ScenarioResult.Pass("Determinism (2 clients, 300 frames)", $"hash=0x{expectedHash:X16}");
    }

    private static ScenarioResult RunServerClientConsistencyScenario()
    {
        Harness harness = CreateHarness(clientCount: 1);
        SimulatedClient client = harness.Clients[0];

        for (uint frame = 0; frame < DefaultTotalFrames; frame++)
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
                    "Consistency (server-client, 300 frames)",
                    $"server=0x{serverHash:X16} client=0x{clientHash:X16} frame={frame}");
            }
        }

        return ScenarioResult.Pass("Consistency (server-client, 300 frames)");
    }

    private static ScenarioResult RunConvergenceScenario()
    {
        Harness harness = CreateHarness(clientCount: 2);
        ulong? stableHash = null;

        for (uint frame = 0; frame < DefaultTotalFrames; frame++)
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
            ulong peerHash = harness.Clients[1].GetStateHash();
            if (currentHash != peerHash)
            {
                return ScenarioResult.Fail(
                    "Convergence (no input after frame 50)",
                    $"client hash mismatch frame={frame} client0=0x{currentHash:X16} client1=0x{peerHash:X16}");
            }

            if (!stableHash.HasValue)
            {
                stableHash = currentHash;
                continue;
            }

            if (currentHash != stableHash.Value)
            {
                return ScenarioResult.Fail(
                    "Convergence (no input after frame 50)",
                    $"hash drift detected frame={frame} baseline=0x{stableHash.Value:X16} current=0x{currentHash:X16}");
            }
        }

        if (!stableHash.HasValue)
        {
            return ScenarioResult.Fail("Convergence (no input after frame 50)", "stable hash was not captured");
        }

        return ScenarioResult.Pass("Convergence (no input after frame 50)");
    }

    private static ScenarioResult RunPlayerLeaveScenario()
    {
        Harness harness = CreateHarness(clientCount: 2);
        long playerA = harness.Clients[0].PlayerId;
        long playerB = harness.Clients[1].PlayerId;
        float beforeLeaveX = 0.0f;
        bool capturedBeforeLeave = false;

        for (uint frame = 0; frame < 151; frame++)
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
}
