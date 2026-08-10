using System;
using System.Security.Cryptography;
using FixedMathSharp.Utility;

namespace GameShared.FrameSync.Network
{
    public static class BattleJoinErrorCodes
    {
        public const uint Success = 0u;
        public const uint BattleUnavailable = 2001u;
        public const uint AuthenticationFailed = 2002u;
        public const uint AccountAlreadyOnline = 2003u;
    }

    /// <summary>
    /// Source of unguessable probe nonces for server-initiated RTT measurement.
    /// Production uses crypto RNG; headless tests inject a seeded source.
    /// </summary>
    public interface IProbeNonceSource
    {
        ulong NextNonce();
    }

    /// <summary>
    /// Production nonce source. Cryptographically random; not deterministic.
    /// </summary>
    public sealed class CryptoProbeNonceSource : IProbeNonceSource
    {
        public static readonly CryptoProbeNonceSource Instance = new CryptoProbeNonceSource();

        private CryptoProbeNonceSource()
        {
        }

        public ulong NextNonce()
        {
            Span<byte> bytes = stackalloc byte[8];
            RandomNumberGenerator.Fill(bytes);
            return BitConverter.ToUInt64(bytes);
        }
    }

    /// <summary>
    /// Headless/test nonce source. Reproducible from seed via DeterministicRandom.
    /// Do not use in production — xoroshiro state is recoverable.
    /// </summary>
    public sealed class SeededProbeNonceSource : IProbeNonceSource
    {
        private DeterministicRandom _random;

        public SeededProbeNonceSource(ulong seed)
        {
            Seed = seed;
            _random = new DeterministicRandom(seed);
        }

        public ulong Seed { get; }

        public ulong NextNonce()
        {
            return _random.NextU64();
        }
    }
}
