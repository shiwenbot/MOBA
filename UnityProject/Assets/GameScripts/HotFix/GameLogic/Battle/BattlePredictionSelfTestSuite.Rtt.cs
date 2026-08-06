using System;
using System.Collections.Generic;
using FixedMathSharp;
using GameShared.FrameSync.Battle;
using GameShared.FrameSync.Core;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Network;
using GameShared.FrameSync.Snapshot;

namespace GameLogic
{
    public static partial class BattlePredictionSelfTestSuite
    {
        private static bool RttTrackerAckRoundtripMeasuresLatency()
        {
            ServerRttTracker tracker = new ServerRttTracker();
            tracker.RecordProbeSent(0xABCDul, 0);
            if (!tracker.TryRecordAck(0xABCDul, 120, out long rttMs) || rttMs != 120)
            {
                throw new InvalidOperationException($"rtt-tracker-ack-roundtrip expected rtt=120 got {rttMs}");
            }

            if (tracker.TryRecordAck(0xDEADBEEFul, 200, out _))
            {
                throw new InvalidOperationException("unknown nonce must return false");
            }

            if (tracker.UnknownAckCount != 1)
            {
                throw new InvalidOperationException($"UnknownAckCount={tracker.UnknownAckCount} expected 1");
            }

            return true;
        }

        private static bool RttTrackerWindowMinIgnoresOutlierSpike()
        {
            ServerRttTracker tracker = new ServerRttTracker(windowSampleCount: 4);
            long now = 0;
            for (int i = 0; i < 4; i++)
            {
                ulong n = (ulong)(i + 1);
                tracker.RecordProbeSent(n, now);
                tracker.TryRecordAck(n, now + 10, out _);
                now += 100;
            }

            if (tracker.MinMs != 10)
            {
                throw new InvalidOperationException($"MinMs after baseline samples = {tracker.MinMs}");
            }

            tracker.RecordProbeSent(99, now);
            tracker.TryRecordAck(99, now + 500, out _);
            if (tracker.MinMs != 10)
            {
                throw new InvalidOperationException($"MinMs must ignore spike, got {tracker.MinMs}");
            }

            for (int i = 0; i < 4; i++)
            {
                now += 100;
                ulong n = 200ul + (ulong)i;
                tracker.RecordProbeSent(n, now);
                tracker.TryRecordAck(n, now + 40, out _);
            }

            if (tracker.MinMs < 40)
            {
                throw new InvalidOperationException($"MinMs should rise after window slide, got {tracker.MinMs}");
            }

            return true;
        }

        private static bool RttTrackerStaleProbeCleanupBoundsTable()
        {
            ServerRttTracker tracker = new ServerRttTracker(probeTableCapacity: 32);
            for (int i = 0; i < 64; i++)
            {
                tracker.RecordProbeSent((ulong)(i + 1), i * 10L);
            }

            if (tracker.PendingProbeCount > 32)
            {
                throw new InvalidOperationException($"PendingProbeCount={tracker.PendingProbeCount} exceeds cap");
            }

            if (tracker.OverflowDroppedProbeCount <= 0)
            {
                throw new InvalidOperationException("expected overflow drops");
            }

            tracker.CleanupStale(nowMs: 10_000, timeoutMs: 5000);
            if (tracker.TimedOutProbeCount <= 0)
            {
                throw new InvalidOperationException("expected timed-out probes");
            }

            return true;
        }

        private static bool RttNonceSourceIsUnpredictableAndSeededIsReproducible()
        {
            CryptoProbeNonceSource crypto = CryptoProbeNonceSource.Instance;
            HashSet<ulong> seen = new HashSet<ulong>();
            ulong prev = 0;
            bool strictlyIncreasing = true;
            for (int i = 0; i < 1024; i++)
            {
                ulong n = crypto.NextNonce();
                if (!seen.Add(n))
                {
                    throw new InvalidOperationException("crypto nonce collision in 1024 samples");
                }

                if (i > 0 && n <= prev)
                {
                    strictlyIncreasing = false;
                }

                prev = n;
            }

            if (strictlyIncreasing)
            {
                throw new InvalidOperationException("crypto nonces must not be monotonically increasing");
            }

            SeededProbeNonceSource a = new SeededProbeNonceSource(0xC0FFEEul);
            SeededProbeNonceSource b = new SeededProbeNonceSource(0xC0FFEEul);
            for (int i = 0; i < 64; i++)
            {
                if (a.NextNonce() != b.NextNonce())
                {
                    throw new InvalidOperationException($"seeded mismatch at {i}");
                }
            }

            return true;
        }

        private static bool RttControlValueRisesFastAndFallsSlow()
        {
            ServerRttTracker tracker = new ServerRttTracker(tightenRateMsPerSec: 20f);
            long now = 0;
            tracker.RecordProbeSent(1, now);
            tracker.TryRecordAck(1, now + 5, out _);

            now = 1000;
            tracker.RecordProbeSent(2, now);
            tracker.TryRecordAck(2, now + 200, out _);
            if (tracker.ControlRttMs < 200f)
            {
                throw new InvalidOperationException(
                    $"control must rise to >=200 on first high sample, got {tracker.ControlRttMs}");
            }

            float afterRise = tracker.ControlRttMs;

            now = 2000;
            tracker.RecordProbeSent(3, now);
            tracker.TryRecordAck(3, now + 5, out _);
            if (tracker.ControlRttMs <= 5.5f)
            {
                throw new InvalidOperationException(
                    $"control must not snap down immediately, got {tracker.ControlRttMs}");
            }

            tracker.TickEnvelope(now + 1000);
            float afterOneSec = tracker.ControlRttMs;
            if (afterOneSec >= afterRise)
            {
                throw new InvalidOperationException(
                    $"control must decay over time: before={afterRise} after={afterOneSec}");
            }

            if (afterOneSec <= 5.5f)
            {
                throw new InvalidOperationException($"control decayed too fast in 1s: {afterOneSec}");
            }

            return true;
        }

        private static bool RttLeadBoundsRegionIsNonEmpty()
        {
            LeadBoundsParams p = new LeadBoundsParams();
            foreach (float rtt in new[] { 0f, 200f, 400f })
            {
                LeadBoundsResult r = LeadBoundsCalculator.Evaluate(100, 90, true, rtt, p);
                if (r.UpperBound >= InputBufferTuning.MaxFutureInputFrames)
                {
                    throw new InvalidOperationException(
                        $"upperBound={r.UpperBound} must be < MaxFuture at rtt={rtt}");
                }
            }

            bool threw = false;
            try
            {
                _ = new LeadBoundsParams(windowMs: 10_000f);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            if (!threw)
            {
                throw new InvalidOperationException("constructor must throw when alarm region empty");
            }

            LeadBoundsResult high = LeadBoundsCalculator.Evaluate(100, 90, true, 700f, p);
            LeadBoundsResult extreme = LeadBoundsCalculator.Evaluate(100, 90, true, 5000f, p);
            if (high.UpperBound < InputBufferTuning.MaxFutureInputFrames)
            {
                throw new InvalidOperationException(
                    $"expected upperBound>=24 at controlRtt=700, got {high.UpperBound} dt={p.FixedDeltaMilliseconds}");
            }

            if (extreme.UpperBound < InputBufferTuning.MaxFutureInputFrames)
            {
                throw new InvalidOperationException(
                    $"expected upperBound>=24 at controlRtt=5000, got {extreme.UpperBound}");
            }

            if (high.IsOutOfBounds)
            {
                throw new InvalidOperationException("honest lead must not warn when upperBound >= 24");
            }

            return true;
        }

        private static bool RttHonestLeadNeverWarns()
        {
            LeadBoundsParams p = new LeadBoundsParams();
            foreach (float rtt in new[] { 0f, 200f, 400f })
            {
                for (int lead = 4; lead <= 6; lead++)
                {
                    uint last = 100;
                    uint claimed = last + (uint)lead;
                    LeadBoundsResult r = LeadBoundsCalculator.Evaluate(claimed, last, true, rtt, p);
                    if (r.IsOutOfBounds)
                    {
                        throw new InvalidOperationException(
                            $"honest lead={lead} rtt={rtt} upper={r.UpperBound} falsely out of bounds");
                    }
                }
            }

            return true;
        }

        private static bool RttNoSampleDoesNotWarn()
        {
            LeadBoundsResult r = LeadBoundsCalculator.Evaluate(200, 100, hasSample: false, controlRttMs: 0f);
            if (r.IsOutOfBounds || r.HasSample)
            {
                throw new InvalidOperationException("no-sample path must not warn");
            }

            return true;
        }

        private static bool LateInputDoesNotCountAsLeadOutOfBounds()
        {
            LeadBoundsResult late = LeadBoundsCalculator.Evaluate(50, 100, true, 200f);
            if (late.IsOutOfBounds)
            {
                throw new InvalidOperationException("late input must not count as out-of-bounds");
            }

            LeadBoundsResult equalZero = LeadBoundsCalculator.Evaluate(0, 0, true, 200f);
            if (equalZero.IsOutOfBounds)
            {
                throw new InvalidOperationException("frame0 equal case must not count as out-of-bounds");
            }

            return true;
        }

        private static bool RttInjectedDelayRaisesMeasuredRtt()
        {
#if FANTASY_UNITY
            return true;
#else
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(
                CreateNetworkConfig(uplinkDelayMs: 100, downlinkDelayMs: 100),
                clock);
            ServerRttTracker tracker = new ServerRttTracker();
            long measuredRtt = -1;

            Action<ulong> sendAck = gate.WrapSendRttProbeAck(nonce =>
            {
                tracker.TryRecordAck(nonce, clock.NowMs, out long rtt);
                measuredRtt = rtt;
            });

            ulong nonce = 7;
            tracker.RecordProbeSent(nonce, clock.NowMs);
            gate.TryAcceptRttProbe(clock.NowMs, nonce, n => sendAck(n));

            double fixedDtMs = DeterminismRules.FixedDeltaTime * 1000.0;
            for (uint frame = 1; frame <= 20; frame++)
            {
                clock.SetFrame(frame);
                gate.PumpDownlink(clock.NowMs);
                gate.PumpUplink(clock.NowMs);
            }

            long minExpected = 200;
            long maxExpected = 200 + (long)Math.Ceiling(2 * fixedDtMs) + 50;
            if (measuredRtt < minExpected || measuredRtt > maxExpected)
            {
                throw new InvalidOperationException(
                    $"rtt-injected-delay measured={measuredRtt} expected in [{minExpected},{maxExpected}]");
            }

            return true;
#endif
        }

        private static bool RttEnvelopeCapPreventsDelayedAckPollution()
        {
            ServerRttTracker capped = new ServerRttTracker(
                tightenRateMsPerSec: 20f,
                envelopeSampleCapEnabled: true);
            ServerRttTracker uncapped = new ServerRttTracker(
                tightenRateMsPerSec: 20f,
                envelopeSampleCapEnabled: false);

            long now = 0;
            capped.RecordProbeSent(1, now);
            capped.TryRecordAck(1, now + 20, out _);
            uncapped.RecordProbeSent(1, now);
            uncapped.TryRecordAck(1, now + 20, out _);

            now = 1000;
            capped.RecordProbeSent(2, now);
            capped.TryRecordAck(2, now + 5000, out _);
            uncapped.RecordProbeSent(2, now);
            uncapped.TryRecordAck(2, now + 5000, out _);

            if (uncapped.ControlRttMs < 4900f)
            {
                throw new InvalidOperationException(
                    $"uncapped control should peak near 5000, got {uncapped.ControlRttMs}");
            }

            // Cap keeps control well below the raw 5000 (EMA≈1016 / envelope≤1000).
            if (capped.ControlRttMs > 1200f)
            {
                throw new InvalidOperationException(
                    $"capped control too high after delayed Ack: {capped.ControlRttMs}");
            }

            float cappedPeak = capped.ControlRttMs;

            // Recovery needs low samples (EMA) + clock advance (envelope). TickEnvelope alone
            // cannot pull EMA down; control = max(EMA, envelope).
            now = 6000;
            for (int i = 0; i < 144; i++)
            {
                now += 333;
                ulong n = 1000ul + (ulong)i;
                capped.RecordProbeSent(n, now);
                capped.TryRecordAck(n, now + 20, out _);
                capped.TickEnvelope(now + 20);

                uncapped.RecordProbeSent(n, now);
                uncapped.TryRecordAck(n, now + 20, out _);
                uncapped.TickEnvelope(now + 20);
            }

            float cappedAfter = capped.ControlRttMs;
            float uncappedAfter = uncapped.ControlRttMs;

            if (cappedAfter > cappedPeak * 0.35f)
            {
                throw new InvalidOperationException(
                    $"capped control must fall substantially over 48s: peak={cappedPeak} after={cappedAfter}");
            }

            if (cappedAfter > 80f)
            {
                throw new InvalidOperationException(
                    $"capped control should near low RTT after recovery, got {cappedAfter}");
            }

            // Uncapped envelope still peak-held near 5000 → control stays high for minutes.
            if (uncappedAfter < 3000f)
            {
                throw new InvalidOperationException(
                    $"uncapped should still be high after 48s, got {uncappedAfter}");
            }

            return true;
        }


        private static bool TargetLeadDecreaseDoesNotDropActualLeadImmediately()
        {
            BattleSimulation sim = CreateSimulation(new BattleWorldState(), out _, out _);
            sim.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);
            sim.ApplyAuthoritativeTargetLead(10);
            uint leadBefore = sim.LeadFrames;
            if (leadBefore != 10)
            {
                throw new InvalidOperationException($"expected lead 10 after target10, got {leadBefore}");
            }

            sim.ApplyAuthoritativeTargetLead(3);
            if (sim.BaselineLeadFrames != 3)
            {
                throw new InvalidOperationException($"baseline after target3={sim.BaselineLeadFrames}");
            }

            // Actual lead must remain until feedback cooldown reduces it — no instant soft-cap snap.
            if (sim.LeadFrames != leadBefore)
            {
                throw new InvalidOperationException(
                    $"lead must not change on target decrease: before={leadBefore} after={sim.LeadFrames}");
            }

            if (sim.EffectiveMaxLeadFrames != Math.Min(InputBufferTuning.MaxLeadFrames, 3 + TargetLeadCalculator.SoftCapSlackFrames))
            {
                throw new InvalidOperationException(
                    $"effectiveMax after target3={sim.EffectiveMaxLeadFrames}");
            }

            return true;
        }

        private static bool RttProbePassesDownlinkGate()
        {
#if FANTASY_UNITY
            return true;
#else
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(
                CreateNetworkConfig(downlinkDelayMs: 100, uplinkDelayMs: 0),
                clock);
            ServerRttTracker tracker = new ServerRttTracker();
            long measured = -1;
            Action<ulong> sendAck = gate.WrapSendRttProbeAck(nonce =>
            {
                tracker.TryRecordAck(nonce, clock.NowMs, out long rtt);
                measured = rtt;
            });

            ulong nonce = 42;
            tracker.RecordProbeSent(nonce, clock.NowMs);
            gate.TryAcceptRttProbe(clock.NowMs, nonce, n => sendAck(n));

            for (uint frame = 1; frame <= 15; frame++)
            {
                clock.SetFrame(frame);
                gate.PumpDownlink(clock.NowMs);
                gate.PumpUplink(clock.NowMs);
            }

            if (measured < 100)
            {
                throw new InvalidOperationException(
                    $"rtt-probe-passes-downlink-gate measured={measured} expected >=100");
            }

            return true;
#endif
        }

        private static bool AuthoritativeTargetLeadIsAppliedAndClamped()
        {
            BattleSimulation sim = CreateSimulation(new BattleWorldState(), out _, out _);
            sim.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);

            sim.ApplyAuthoritativeTargetLead(6);
            if (sim.BaselineLeadFrames != 6)
            {
                throw new InvalidOperationException($"baseline after target6={sim.BaselineLeadFrames}");
            }

            sim.ApplyAuthoritativeTargetLead(1);
            if (sim.BaselineLeadFrames != InputBufferTuning.MinLeadFrames)
            {
                throw new InvalidOperationException($"target1 must clamp to Min, got {sim.BaselineLeadFrames}");
            }

            sim.ApplyAuthoritativeTargetLead(100);
            if (sim.BaselineLeadFrames > InputBufferTuning.MaxLeadFrames)
            {
                throw new InvalidOperationException($"target100 must clamp to Max, got {sim.BaselineLeadFrames}");
            }

            return true;
        }

        private static bool ZeroTargetLeadFallsBackToClientRtt()
        {
            BattleSimulation sim = CreateSimulation(new BattleWorldState(), out _, out _);
            sim.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);
            sim.ProcessPong(200);
            uint clientBaseline = sim.BaselineLeadFrames;

            sim.ApplyAuthoritativeTargetLead(9);
            if (sim.BaselineLeadFrames != 9)
            {
                throw new InvalidOperationException($"expected baseline 9, got {sim.BaselineLeadFrames}");
            }

            sim.ApplyAuthoritativeTargetLead(0);
            if (sim.HasAuthoritativeTargetLead)
            {
                throw new InvalidOperationException("zero target must clear authoritative flag");
            }

            if (sim.BaselineLeadFrames != clientBaseline)
            {
                throw new InvalidOperationException(
                    $"zero target must restore client baseline {clientBaseline}, got {sim.BaselineLeadFrames}");
            }

            return true;
        }

        private static bool TargetLeadSoftCapAllowsFeedbackTransient()
        {
            BattleSimulation sim = CreateSimulation(new BattleWorldState(), out _, out _);
            sim.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);
            sim.ApplyAuthoritativeTargetLead(6);

            int effectiveMax = sim.EffectiveMaxLeadFrames;
            int expected = Math.Min(InputBufferTuning.MaxLeadFrames, 6 + TargetLeadCalculator.SoftCapSlackFrames);
            if (effectiveMax != expected)
            {
                throw new InvalidOperationException($"effectiveMax={effectiveMax} expected {expected}");
            }

            if (effectiveMax <= 6)
            {
                throw new InvalidOperationException("soft cap must exceed target for feedback transients");
            }

            return true;
        }


        private static bool SnapshotTargetLeadIsAppliedAfterDownlinkGate()
        {
#if FANTASY_UNITY
            return true;
#else
            FrameBattleClock clock = new FrameBattleClock();
            BattleNetworkGate gate = new BattleNetworkGate(
                CreateNetworkConfig(downlinkDelayMs: 100),
                clock);
            BattleWorldState world = new BattleWorldState();
            BattleSimulation sim = new BattleSimulation(
                world,
                gate.WrapSendInput((_, _, _, _, _) => { }),
                gate.WrapSendPing(_ => { }),
                clock: clock);
            sim.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);

            BattleWorldSnapshot snap = CreateMinimalSelfSnapshot(1, 5);
            uint targetLead = 8;
            long nowMs = clock.NowMs;
            if (!gate.TryAcceptSnapshotMessage(nowMs))
            {
                throw new InvalidOperationException("snapshot should pass drop check");
            }

            gate.EnqueueConvertedSnapshot(nowMs, snap, value =>
            {
                sim.ApplyAuthoritativeTargetLead(targetLead);
                sim.EnqueueServerSnapshot(value, 5);
            });

            if (sim.HasAuthoritativeTargetLead)
            {
                throw new InvalidOperationException("target applied before downlink release");
            }

            for (uint frame = 1; frame <= 10; frame++)
            {
                clock.SetFrame(frame);
                gate.PumpDownlink(clock.NowMs);
            }

            if (!sim.HasAuthoritativeTargetLead || sim.AppliedTargetLeadFrames != targetLead)
            {
                throw new InvalidOperationException(
                    $"target not applied after release: has={sim.HasAuthoritativeTargetLead} applied={sim.AppliedTargetLeadFrames}");
            }

            return true;
#endif
        }

        private static bool AuthoritativeLeadSwitchOffClearsStaleTarget()
        {
            BattleSimulation sim = CreateSimulation(new BattleWorldState(), out _, out _);
            sim.SetJoined(1, 0, Fixed64.Zero, Fixed64.Zero);
            sim.ProcessPong(200);
            uint clientBaseline = sim.BaselineLeadFrames;

            sim.ApplyAuthoritativeTargetLead(12);
            sim.ClearAuthoritativeTargetLead();
            if (sim.HasAuthoritativeTargetLead || sim.AppliedTargetLeadFrames != 0)
            {
                throw new InvalidOperationException("stale target remains after clear");
            }

            if (sim.BaselineLeadFrames != clientBaseline)
            {
                throw new InvalidOperationException(
                    $"after clear baseline={sim.BaselineLeadFrames} expected client {clientBaseline}");
            }

            return true;
        }

        private static bool TargetLeadFormulaMapsRttToFrames()
        {
            int t0 = TargetLeadCalculator.Compute(0f);
            int t200 = TargetLeadCalculator.Compute(200f);
            int t400 = TargetLeadCalculator.Compute(400f);
            if (t0 != 3 || t200 != 6 || t400 != 9)
            {
                throw new InvalidOperationException(
                    $"target-lead-formula got {t0}/{t200}/{t400} expected 3/6/9");
            }

            return true;
        }

        private static BattleWorldSnapshot CreateMinimalSelfSnapshot(long playerId, uint frameIndex)
        {
            BattleWorldState world = new BattleWorldState();
            world.AddOrUpdatePlayer(playerId, Fixed64.Zero, Fixed64.Zero);
            return world.TakeSnapshot().WithFrameIndex(frameIndex);
        }
    }
}
