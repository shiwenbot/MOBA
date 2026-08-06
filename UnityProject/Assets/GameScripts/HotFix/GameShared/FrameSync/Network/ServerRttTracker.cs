using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Network
{
    /// <summary>
    /// Per-session server-side RTT measurement state:
    /// probe table + EMA + sliding window min + asymmetric envelope control value.
    /// Zero engine dependency; pure logic for headless coverage.
    /// </summary>
    public sealed class ServerRttTracker
    {
        public const int DefaultProbeTableCapacity = 32;
        public const int DefaultWindowSampleCount = 16;
        public const float DefaultEmaAlpha = 0.2f;
        public const float DefaultTightenRateMsPerSec = 20f;
        public const long DefaultProbeTimeoutMs = 5000;
        public const float InitialEmaMs = 100f;

        private readonly Dictionary<ulong, long> _probeSentAtMsByNonce;
        private readonly Queue<ulong> _probeNonceOrder;
        private readonly long[] _windowSamplesMs;
        private readonly int _probeTableCapacity;
        private readonly int _windowCapacity;
        private readonly float _emaAlpha;
        private readonly float _tightenRateMsPerSec;
        private readonly bool _envelopeSampleCapEnabled;

        private int _windowCount;
        private int _windowWriteIndex;
        private float _emaMs;
        private float _envelopeMs;
        private long _lastEnvelopeUpdateMs;
        private bool _hasEnvelopeClock;
        private long _minMs;
        private bool _hasSample;
        private int _sampleCount;

        public ServerRttTracker(
            int probeTableCapacity = DefaultProbeTableCapacity,
            int windowSampleCount = DefaultWindowSampleCount,
            float emaAlpha = DefaultEmaAlpha,
            float tightenRateMsPerSec = DefaultTightenRateMsPerSec,
            bool envelopeSampleCapEnabled = true)
        {
            if (probeTableCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(probeTableCapacity));
            }

            if (windowSampleCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSampleCount));
            }

            if (emaAlpha <= 0f || emaAlpha > 1f || float.IsNaN(emaAlpha) || float.IsInfinity(emaAlpha))
            {
                throw new ArgumentOutOfRangeException(nameof(emaAlpha));
            }

            if (tightenRateMsPerSec <= 0f || float.IsNaN(tightenRateMsPerSec) || float.IsInfinity(tightenRateMsPerSec))
            {
                throw new ArgumentOutOfRangeException(nameof(tightenRateMsPerSec));
            }

            _probeTableCapacity = probeTableCapacity;
            _windowCapacity = windowSampleCount;
            _emaAlpha = emaAlpha;
            _tightenRateMsPerSec = tightenRateMsPerSec;
            _envelopeSampleCapEnabled = envelopeSampleCapEnabled;
            _probeSentAtMsByNonce = new Dictionary<ulong, long>(probeTableCapacity);
            _probeNonceOrder = new Queue<ulong>(probeTableCapacity);
            _windowSamplesMs = new long[windowSampleCount];
            _emaMs = InitialEmaMs;
            _envelopeMs = 0f;
            _minMs = 0;
        }

        public bool HasSample => _hasSample;
        public float EmaMs => _emaMs;
        public long MinMs => _hasSample ? _minMs : 0;
        public int SampleCount => _sampleCount;
        public int PendingProbeCount => _probeSentAtMsByNonce.Count;
        public int OverflowDroppedProbeCount { get; private set; }
        public int UnknownAckCount { get; private set; }
        public int TimedOutProbeCount { get; private set; }
        public int ClockBackwardCount { get; private set; }
        public int LeadOutOfBoundsCount { get; private set; }

        /// <summary>
        /// Control value: max(EMA, decaying envelope). Downstream consumers use only this.
        /// </summary>
        public float ControlRttMs
        {
            get
            {
                if (!_hasSample)
                {
                    return 0f;
                }

                return Math.Max(_emaMs, _envelopeMs);
            }
        }

        public void IncrementLeadOutOfBounds()
        {
            LeadOutOfBoundsCount++;
        }

        public void RecordProbeSent(ulong nonce, long nowMs)
        {
            if (_probeSentAtMsByNonce.ContainsKey(nonce))
            {
                // Overwrite same nonce timestamp (should not happen with crypto RNG).
                _probeSentAtMsByNonce[nonce] = nowMs;
                return;
            }

            while (_probeSentAtMsByNonce.Count >= _probeTableCapacity && _probeNonceOrder.Count > 0)
            {
                ulong oldest = _probeNonceOrder.Dequeue();
                if (_probeSentAtMsByNonce.Remove(oldest))
                {
                    OverflowDroppedProbeCount++;
                }
            }

            _probeSentAtMsByNonce[nonce] = nowMs;
            _probeNonceOrder.Enqueue(nonce);
        }

        /// <summary>
        /// Match Ack by nonce. On success updates EMA, window min, and envelope.
        /// Returns false for unknown nonce / negative RTT.
        /// </summary>
        public bool TryRecordAck(ulong nonce, long nowMs, out long rttMs)
        {
            rttMs = 0;
            if (!_probeSentAtMsByNonce.TryGetValue(nonce, out long sentAtMs))
            {
                UnknownAckCount++;
                return false;
            }

            _probeSentAtMsByNonce.Remove(nonce);
            rttMs = nowMs - sentAtMs;
            if (rttMs < 0)
            {
                ClockBackwardCount++;
                rttMs = 0;
                return false;
            }

            ApplySample(rttMs, nowMs);
            return true;
        }

        public void CleanupStale(long nowMs, long timeoutMs = DefaultProbeTimeoutMs)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            }

            if (_probeSentAtMsByNonce.Count == 0)
            {
                return;
            }

            // Drain in send order; stop at first not-stale (FIFO).
            int guard = _probeNonceOrder.Count;
            while (guard-- > 0 && _probeNonceOrder.Count > 0)
            {
                ulong nonce = _probeNonceOrder.Peek();
                if (!_probeSentAtMsByNonce.TryGetValue(nonce, out long sentAtMs))
                {
                    _probeNonceOrder.Dequeue();
                    continue;
                }

                if (nowMs - sentAtMs < timeoutMs)
                {
                    break;
                }

                _probeNonceOrder.Dequeue();
                _probeSentAtMsByNonce.Remove(nonce);
                TimedOutProbeCount++;
            }
        }

        /// <summary>
        /// Advance envelope decay by wall-clock time even without new samples.
        /// Call on Tick so TightenRate is time-based not sample-based.
        /// </summary>
        public void TickEnvelope(long nowMs)
        {
            if (!_hasSample)
            {
                return;
            }

            DecayEnvelope(nowMs);
        }

        private void ApplySample(long rttMs, long nowMs)
        {
            float controlBefore = _hasSample ? Math.Max(_emaMs, _envelopeMs) : 0f;

            // EMA always eats raw sample (preserve link info).
            if (!_hasSample)
            {
                _emaMs = rttMs;
                _hasSample = true;
            }
            else
            {
                DecayEnvelope(nowMs);
                _emaMs = (_emaAlpha * rttMs) + ((1.0f - _emaAlpha) * _emaMs);
            }

            // Envelope peak-hold on capped sample.
            // Cap = max(2×controlBefore, 1000ms):
            // - rise-fast still works (5→200 under the 1000 floor)
            // - a single 5000ms delayed Ack only lifts envelope to ~1000 (~48s @ 20ms/s)
            // - sustained high RTT still climbs as controlBefore grows
            float capped = rttMs;
            if (_envelopeSampleCapEnabled)
            {
                float cap = Math.Max(2f * Math.Max(controlBefore, 1f), 1000f);
                if (capped > cap)
                {
                    capped = cap;
                }
            }


            if (_sampleCount == 0 || capped > _envelopeMs)
            {
                _envelopeMs = capped;
            }

            PushWindowSample(rttMs);
            _sampleCount++;
            _lastEnvelopeUpdateMs = nowMs;
            _hasEnvelopeClock = true;
        }

        private void DecayEnvelope(long nowMs)
        {
            if (!_hasSample || !_hasEnvelopeClock)
            {
                _lastEnvelopeUpdateMs = nowMs;
                _hasEnvelopeClock = true;
                return;
            }

            long elapsedMs = nowMs - _lastEnvelopeUpdateMs;
            if (elapsedMs <= 0)
            {
                return;
            }

            float floor = _minMs;
            if (_envelopeMs <= floor)
            {
                _envelopeMs = floor;
                _lastEnvelopeUpdateMs = nowMs;
                return;
            }

            float decay = _tightenRateMsPerSec * (elapsedMs / 1000f);
            _envelopeMs = Math.Max(floor, _envelopeMs - decay);
            _lastEnvelopeUpdateMs = nowMs;
        }


        private void PushWindowSample(long rttMs)
        {
            if (_windowCount < _windowCapacity)
            {
                _windowSamplesMs[_windowWriteIndex] = rttMs;
                _windowWriteIndex = (_windowWriteIndex + 1) % _windowCapacity;
                _windowCount++;
            }
            else
            {
                _windowSamplesMs[_windowWriteIndex] = rttMs;
                _windowWriteIndex = (_windowWriteIndex + 1) % _windowCapacity;
            }

            RecomputeWindowMin();
        }

        private void RecomputeWindowMin()
        {
            long min = long.MaxValue;
            for (int i = 0; i < _windowCount; i++)
            {
                long sample = _windowSamplesMs[i];
                if (sample < min)
                {
                    min = sample;
                }
            }

            _minMs = min == long.MaxValue ? 0 : min;
        }
    }
}
