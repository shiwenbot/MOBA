using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Fantasy.Network;
using Fantasy.Network.Interface;
using Fantasy.Serialize;
using FixedMathSharp;
using GameShared.FrameSync.Determinism;
using GameShared.FrameSync.Network;
using MongoDB.Bson;
using MongoDB.Driver;

#pragma warning disable FANTASY002 // Harness entities are detached from a Fantasy Scene on purpose.

namespace Fantasy;

internal static class S9ServerTestSuite
{
    public static readonly string[] AllCaseNames =
    {
        "reconnect-grace-period-keeps-player-state",
        "reconnect-grace-expiry-removes-player",
        "mark-disconnected-is-idempotent-and-grace-still-expires",
        "reconnect-token-claims-same-player-id",
        "reconnect-after-grace-expiry-creates-new-player",
        "reconnect-at-grace-boundary-is-deterministic",
        "online-slot-rejects-different-session",
        "online-slot-same-session-is-idempotent",
        "fast-reconnect-before-dispose-observed-retries-then-succeeds",
        "reconnect-cleans-stale-session-mappings",
        "claimed-entry-ignores-old-session-packets",
        "concurrent-claims-are-serialized",
        "login-issues-token-and-writes-session",
        "join-without-valid-token-is-rejected",
        "expired-login-token-cannot-join-or-claim",
        "battle-slot-is-bound-to-account-not-player-id",
        "same-account-second-join-is-idempotent",
        "probe-timeout-disconnect-is-revoked-on-late-packet",
        "disposed-disconnect-is-not-revoked-by-late-packet"
    };

    public static bool RunCase(string caseName, out string failure)
    {
        try
        {
            bool passed = caseName switch
            {
                "reconnect-grace-period-keeps-player-state" => ReconnectGracePeriodKeepsPlayerState(),
                "reconnect-grace-expiry-removes-player" => ReconnectGraceExpiryRemovesPlayer(),
                "mark-disconnected-is-idempotent-and-grace-still-expires" => MarkDisconnectedIsIdempotentAndGraceStillExpires(),
                "reconnect-token-claims-same-player-id" => ReconnectTokenClaimsSamePlayerId(),
                "reconnect-after-grace-expiry-creates-new-player" => ReconnectAfterGraceExpiryCreatesNewPlayer(),
                "reconnect-at-grace-boundary-is-deterministic" => ReconnectAtGraceBoundaryIsDeterministic(),
                "online-slot-rejects-different-session" => OnlineSlotRejectsDifferentSession(),
                "online-slot-same-session-is-idempotent" => OnlineSlotSameSessionIsIdempotent(),
                "fast-reconnect-before-dispose-observed-retries-then-succeeds" => FastReconnectBeforeDisposeObservedRetriesThenSucceeds(),
                "reconnect-cleans-stale-session-mappings" => ReconnectCleansStaleSessionMappings(),
                "claimed-entry-ignores-old-session-packets" => ClaimedEntryIgnoresOldSessionPackets(),
                "concurrent-claims-are-serialized" => ConcurrentClaimsAreSerialized(),
                "login-issues-token-and-writes-session" => LoginIssuesTokenAndWritesSession(),
                "join-without-valid-token-is-rejected" => JoinWithoutValidTokenIsRejected(),
                "expired-login-token-cannot-join-or-claim" => ExpiredLoginTokenCannotJoinOrClaim(),
                "battle-slot-is-bound-to-account-not-player-id" => BattleSlotIsBoundToAccountNotPlayerId(),
                "same-account-second-join-is-idempotent" => SameAccountSecondJoinIsIdempotent(),
                "probe-timeout-disconnect-is-revoked-on-late-packet" => ProbeTimeoutDisconnectIsRevokedOnLatePacket(),
                "disposed-disconnect-is-not-revoked-by-late-packet" => DisposedDisconnectIsNotRevokedByLatePacket(),
                _ => throw new ArgumentException($"Unknown S9 server case: {caseName}", nameof(caseName))
            };

            failure = passed ? string.Empty : caseName;
            return passed;
        }
        catch (Exception exception)
        {
            failure = $"{caseName}:{exception}";
            return false;
        }
    }

    private static bool ReconnectGracePeriodKeepsPlayerState()
    {
        BattleComponent battle = CreateBattle(3u);
        HarnessSession original = new HarnessSession(101L);
        BattleJoinResult first = battle.Join(original, 1001L);
        battle.SubmitInput(original, CreateInput(0u, 1u, Fixed64.One));
        Tick(battle, 0u);
        Fixed64 xBeforeDisconnect = first.State!.X;
        original.DisconnectForTest();
        Tick(battle, 1u);

        if (!battle.TryGetDisconnectedPlayer(1001L, out DisconnectedPlayerEntry entry) ||
            entry.RemainingGraceFrames != 2u ||
            battle.ActivePlayerCountForTests != 1)
        {
            return false;
        }

        BattleJoinResult reclaimed = battle.Join(new HarnessSession(102L), 1001L);
        return reclaimed.IsSuccess &&
               reclaimed.IsReconnect &&
               reclaimed.State!.PlayerId == first.State.PlayerId &&
               reclaimed.State.X == xBeforeDisconnect;
    }

    private static bool ReconnectGraceExpiryRemovesPlayer()
    {
        BattleComponent battle = CreateBattle(2u);
        HarnessSession original = new HarnessSession(201L);
        BattleJoinResult first = battle.Join(original, 2001L);
        original.DisconnectForTest();
        Tick(battle, 0u);
        Tick(battle, 1u);
        Tick(battle, 2u);

        BattleJoinResult afterExpiry = battle.Join(new HarnessSession(202L), 2001L);
        return afterExpiry.IsSuccess &&
               !afterExpiry.IsReconnect &&
               afterExpiry.State!.PlayerId != first.State!.PlayerId &&
               battle.ActivePlayerCountForTests == 1;
    }

    private static bool MarkDisconnectedIsIdempotentAndGraceStillExpires()
    {
        BattleComponent battle = CreateBattle(2u);
        HarnessSession original = new HarnessSession(301L);
        BattleJoinResult first = battle.Join(original, 3001L);
        if (!battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed) ||
            battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed) ||
            !battle.TryGetDisconnectedPlayer(3001L, out DisconnectedPlayerEntry before) ||
            before.RemainingGraceFrames != 2u)
        {
            return false;
        }

        Tick(battle, 0u);
        battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed);
        Tick(battle, 1u);
        Tick(battle, 2u);
        BattleJoinResult next = battle.Join(new HarnessSession(302L), 3001L);
        return next.IsSuccess && next.State!.PlayerId != first.State!.PlayerId;
    }

    private static bool ReconnectTokenClaimsSamePlayerId()
    {
        using S9MongoFixture mongo = new S9MongoFixture();
        const long accountId = 4001L;
        string token = mongo.Issue(accountId, new SeededProbeNonceSource(4001UL), 1_000L, 60_000L);
        if (!mongo.TryValidate(token, 2_000L, out long validatedAccountId))
        {
            return false;
        }

        BattleComponent battle = CreateBattle(3u);
        HarnessSession original = new HarnessSession(401L);
        BattleJoinResult first = battle.Join(original, validatedAccountId);
        first.State!.Y = (Fixed64)9;
        battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed);
        if (!mongo.TryValidate(token, 2_001L, out validatedAccountId))
        {
            return false;
        }

        BattleJoinResult reclaimed = battle.Join(new HarnessSession(402L), validatedAccountId);
        return reclaimed.IsSuccess &&
               reclaimed.IsReconnect &&
               reclaimed.State!.PlayerId == first.State.PlayerId &&
               reclaimed.State.Y == (Fixed64)9;
    }

    private static bool ReconnectAfterGraceExpiryCreatesNewPlayer()
    {
        BattleComponent battle = CreateBattle(1u);
        HarnessSession original = new HarnessSession(501L);
        BattleJoinResult first = battle.Join(original, 5001L);
        battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed);
        Tick(battle, 0u);

        BattleJoinResult next = battle.Join(new HarnessSession(502L), 5001L);
        return next.IsSuccess &&
               !next.IsReconnect &&
               next.State!.PlayerId != first.State!.PlayerId;
    }

    private static bool ReconnectAtGraceBoundaryIsDeterministic()
    {
        BattleComponent beforeBoundary = CreateBattle(1u);
        HarnessSession beforeOriginal = new HarnessSession(601L);
        BattleJoinResult beforeFirst = beforeBoundary.Join(beforeOriginal, 6001L);
        beforeBoundary.MarkDisconnected(beforeOriginal.Id, DisconnectCause.SessionDisposed);
        BattleJoinResult reclaimed = beforeBoundary.Join(new HarnessSession(602L), 6001L);

        BattleComponent atBoundary = CreateBattle(1u);
        HarnessSession boundaryOriginal = new HarnessSession(603L);
        BattleJoinResult boundaryFirst = atBoundary.Join(boundaryOriginal, 6002L);
        atBoundary.MarkDisconnected(boundaryOriginal.Id, DisconnectCause.SessionDisposed);
        Tick(atBoundary, 0u);
        BattleJoinResult recreated = atBoundary.Join(new HarnessSession(604L), 6002L);

        return reclaimed.IsReconnect &&
               reclaimed.State!.PlayerId == beforeFirst.State!.PlayerId &&
               !recreated.IsReconnect &&
               recreated.State!.PlayerId != boundaryFirst.State!.PlayerId;
    }

    private static bool OnlineSlotRejectsDifferentSession()
    {
        BattleComponent battle = CreateBattle();
        BattleJoinResult first = battle.Join(new HarnessSession(701L), 7001L);
        BattleJoinResult second = battle.Join(new HarnessSession(702L), 7001L);
        return first.IsSuccess &&
               !second.IsSuccess &&
               second.ErrorCode == BattleJoinErrorCodes.AccountAlreadyOnline &&
               battle.ActivePlayerCountForTests == 1;
    }

    private static bool OnlineSlotSameSessionIsIdempotent()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession session = new HarnessSession(801L);
        BattleJoinResult first = battle.Join(session, 8001L);
        BattleJoinResult second = battle.Join(session, 8001L);
        return first.IsSuccess &&
               second.IsSuccess &&
               first.State!.PlayerId == second.State!.PlayerId &&
               battle.ActivePlayerCountForTests == 1;
    }

    private static bool FastReconnectBeforeDisposeObservedRetriesThenSucceeds()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession original = new HarnessSession(901L);
        BattleJoinResult first = battle.Join(original, 9001L);
        HarnessSession replacement = new HarnessSession(902L);
        BattleJoinResult early = battle.Join(replacement, 9001L);
        original.DisconnectForTest();
        BattleJoinResult retry = battle.Join(replacement, 9001L);

        return early.ErrorCode == BattleJoinErrorCodes.AccountAlreadyOnline &&
               retry.IsSuccess &&
               retry.IsReconnect &&
               retry.State!.PlayerId == first.State!.PlayerId;
    }

    private static bool ReconnectCleansStaleSessionMappings()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession original = new HarnessSession(1001L);
        BattleJoinResult first = battle.Join(original, 10_001L);
        Tick(battle, 0u);
        if (!battle.HasAttributeBaselineForTests(original.Id, first.State!.PlayerId) ||
            !battle.HasBuffBaselineForTests(original.Id, first.State.PlayerId))
        {
            return false;
        }

        original.DisconnectForTest();
        HarnessSession replacement = new HarnessSession(1002L);
        BattleJoinResult reclaimed = battle.Join(replacement, 10_001L);
        return reclaimed.IsSuccess &&
               !battle.HasSessionMappingForTests(original.Id) &&
               !battle.HasRttTrackerForTests(original.Id) &&
               !battle.HasAttributeBaselineForTests(original.Id, first.State.PlayerId) &&
               !battle.HasBuffBaselineForTests(original.Id, first.State.PlayerId) &&
               battle.HasSessionMappingForTests(replacement.Id) &&
               battle.HasRttTrackerForTests(replacement.Id);
    }

    private static bool ClaimedEntryIgnoresOldSessionPackets()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession original = new HarnessSession(1101L);
        BattleJoinResult first = battle.Join(original, 11_001L);
        battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed);
        HarnessSession replacement = new HarnessSession(1102L);
        BattleJoinResult reclaimed = battle.Join(replacement, 11_001L);
        Fixed64 xBefore = reclaimed.State!.X;

        battle.SubmitInput(replacement, CreateInput(0u, 1u, Fixed64.One));
        battle.SubmitInput(original, CreateInput(0u, 999u, -Fixed64.One));
        Tick(battle, 0u);
        return reclaimed.State.X > xBefore &&
               reclaimed.State.PlayerId == first.State!.PlayerId;
    }

    private static bool ConcurrentClaimsAreSerialized()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession original = new HarnessSession(1201L);
        battle.Join(original, 12_001L);
        battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed);

        object joinGate = new object();
        BattleJoinResult[] results = new BattleJoinResult[2];
        Parallel.Invoke(
            () =>
            {
                lock (joinGate)
                {
                    results[0] = battle.Join(new HarnessSession(1202L), 12_001L);
                }
            },
            () =>
            {
                lock (joinGate)
                {
                    results[1] = battle.Join(new HarnessSession(1203L), 12_001L);
                }
            });

        return results.Count(result => result.IsSuccess && result.IsReconnect) == 1 &&
               results.Count(result => result.ErrorCode == BattleJoinErrorCodes.AccountAlreadyOnline) == 1 &&
               battle.ActivePlayerCountForTests == 1;
    }

    private static bool LoginIssuesTokenAndWritesSession()
    {
        using S9MongoFixture mongo = new S9MongoFixture();
        SequenceNonceSource source = new SequenceNonceSource(42UL, 42UL, 43UL);
        string collisionToken = LoginSessionStore.CreateToken(new SequenceNonceSource(42UL));
        mongo.Insert(collisionToken, 1L, 1_000L, 61_000L);
        string token = mongo.Issue(13_001L, source, 2_000L, 60_000L);

        return token.Length == 16 &&
               ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) &&
               token != collisionToken &&
               mongo.Count(token) == 1L &&
               mongo.TryValidate(token, 3_000L, out long accountId) &&
               accountId == 13_001L;
    }

    private static bool JoinWithoutValidTokenIsRejected()
    {
        using S9MongoFixture mongo = new S9MongoFixture();
        BattleComponent battle = CreateBattle();
        bool invalid = !mongo.TryValidate("not-a-token", 1_000L, out _);
        string validToken = mongo.Issue(14_001L, new SeededProbeNonceSource(14_001UL), 1_000L, 60_000L);
        if (!mongo.TryValidate(validToken, 2_000L, out long accountId))
        {
            return false;
        }

        BattleJoinResult firstActualJoin = battle.Join(new HarnessSession(1401L), accountId);
        return invalid &&
               firstActualJoin.IsSuccess &&
               firstActualJoin.State!.PlayerId == 1L &&
               battle.ActivePlayerCountForTests == 1;
    }

    private static bool ExpiredLoginTokenCannotJoinOrClaim()
    {
        using S9MongoFixture mongo = new S9MongoFixture();
        const long accountId = 15_001L;
        const string token = "0000000000001501";
        mongo.Insert(token, accountId, 1_000L, 2_000L);

        BattleComponent battle = CreateBattle(3u);
        HarnessSession original = new HarnessSession(1501L);
        BattleJoinResult first = battle.Join(original, accountId);
        battle.MarkDisconnected(original.Id, DisconnectCause.SessionDisposed);
        bool rejected = !mongo.TryValidate(token, 2_001L, out _);

        bool invalidConfigRejected = false;
        try
        {
            new LoginSessionConfig(100L).ValidateReconnectGrace(new BattleReconnectConfig(150u));
        }
        catch (InvalidOperationException)
        {
            invalidConfigRejected = true;
        }

        return rejected &&
               invalidConfigRejected &&
               mongo.Count(token) == 0L &&
               battle.TryGetDisconnectedPlayer(accountId, out DisconnectedPlayerEntry entry) &&
               entry.PlayerId == first.State!.PlayerId &&
               battle.IsInputSuppressedForTests(first.State.PlayerId);
    }

    private static bool BattleSlotIsBoundToAccountNotPlayerId()
    {
        using S9MongoFixture mongo = new S9MongoFixture();
        string firstToken = mongo.Issue(16_001L, new SeededProbeNonceSource(16_001UL), 1_000L, 60_000L);
        string secondToken = mongo.Issue(16_002L, new SeededProbeNonceSource(16_002UL), 1_000L, 60_000L);
        if (!mongo.TryValidate(firstToken, 2_000L, out long firstAccount) ||
            !mongo.TryValidate(secondToken, 2_000L, out long secondAccount))
        {
            return false;
        }

        BattleComponent battle = CreateBattle();
        BattleJoinResult first = battle.Join(new HarnessSession(1601L), firstAccount);
        BattleJoinResult second = battle.Join(new HarnessSession(1602L), secondAccount);
        return typeof(LoginSession).GetProperty("PlayerId") == null &&
               first.IsSuccess &&
               second.IsSuccess &&
               first.State!.PlayerId != second.State!.PlayerId &&
               battle.ActivePlayerCountForTests == 2;
    }

    private static bool SameAccountSecondJoinIsIdempotent()
    {
        using S9MongoFixture mongo = new S9MongoFixture();
        string token = mongo.Issue(17_001L, new SeededProbeNonceSource(17_001UL), 1_000L, 60_000L);
        if (!mongo.TryValidate(token, 2_000L, out long accountId))
        {
            return false;
        }

        BattleComponent battle = CreateBattle();
        HarnessSession session = new HarnessSession(1701L);
        BattleJoinResult first = battle.Join(session, accountId);
        BattleJoinResult second = battle.Join(session, accountId);
        return first.IsSuccess &&
               second.IsSuccess &&
               first.State!.PlayerId == second.State!.PlayerId &&
               battle.ActivePlayerCountForTests == 1;
    }

    private static bool ProbeTimeoutDisconnectIsRevokedOnLatePacket()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession session = new HarnessSession(1801L);
        BattleJoinResult joined = battle.Join(session, 18_001L);
        if (!battle.MarkDisconnected(session.Id, DisconnectCause.ProbeTimeout) ||
            !battle.TryGetDisconnectedPlayer(18_001L, out DisconnectedPlayerEntry suspected) ||
            suspected.State != DisconnectedPlayerState.SuspectedDisconnected)
        {
            return false;
        }

        Fixed64 xBefore = joined.State!.X;
        battle.SubmitInput(session, CreateInput(0u, 1u, Fixed64.One));
        Tick(battle, 0u);
        return !battle.TryGetDisconnectedPlayer(18_001L, out _) &&
               !battle.IsInputSuppressedForTests(joined.State.PlayerId) &&
               joined.State.X > xBefore;
    }

    private static bool DisposedDisconnectIsNotRevokedByLatePacket()
    {
        BattleComponent battle = CreateBattle();
        HarnessSession session = new HarnessSession(1901L);
        BattleJoinResult joined = battle.Join(session, 19_001L);
        battle.MarkDisconnected(session.Id, DisconnectCause.SessionDisposed);
        Fixed64 xBefore = joined.State!.X;
        battle.SubmitInput(session, CreateInput(0u, 1u, Fixed64.One));
        Tick(battle, 0u);

        return battle.TryGetDisconnectedPlayer(19_001L, out DisconnectedPlayerEntry confirmed) &&
               confirmed.State == DisconnectedPlayerState.ConfirmedDisconnected &&
               battle.IsInputSuppressedForTests(joined.State.PlayerId) &&
               joined.State.X == xBefore;
    }

    private static BattleComponent CreateBattle(uint graceFrames = 5u)
    {
        return new BattleComponent(new BattleReconnectConfig(graceFrames));
    }

    private static void Tick(BattleComponent battle, uint frameIndex)
    {
        if (SerializerManager.ProtoBufHelper == null)
        {
            SerializerManager.Initialize().GetAwaiter().GetResult();
        }

        battle.Tick(frameIndex, DeterminismRules.FixedDeltaTimeFixed64);
    }

    private static C2B_PlayerInput CreateInput(uint frameIndex, uint sequence, Fixed64 dx)
    {
        return new C2B_PlayerInput
        {
            FrameIndex = frameIndex,
            InputSeq = sequence,
            DxRaw = dx.m_rawValue,
            DyRaw = Fixed64.Zero.m_rawValue
        };
    }

    internal sealed class HarnessSession : Session
    {
        public HarnessSession(long id)
        {
            Id = id;
            RuntimeId = id;
        }

        public int SentMessageCount { get; private set; }

        public override void Send<T>(T message, uint rpcId = 0, long address = 0)
        {
            SentMessageCount++;
        }

        public void DisconnectForTest()
        {
            RuntimeId = 0L;
        }

        public override void Dispose()
        {
            RuntimeId = 0L;
        }
    }

    private sealed class SequenceNonceSource : IProbeNonceSource
    {
        private readonly Queue<ulong> _values;

        public SequenceNonceSource(params ulong[] values)
        {
            _values = new Queue<ulong>(values);
        }

        public ulong NextNonce()
        {
            if (_values.Count == 0)
            {
                throw new InvalidOperationException("Nonce sequence exhausted.");
            }

            return _values.Dequeue();
        }
    }

    private sealed class S9MongoFixture : IDisposable
    {
        private const int MaxIssueAttempts = 5;
        private readonly IMongoDatabase _database;
        private readonly string _collectionName;
        private readonly IMongoCollection<BsonDocument> _collection;
        private bool _disposed;

        public S9MongoFixture()
        {
            string connection = Environment.GetEnvironmentVariable("S9_MONGO_CONNECTION") ??
                                "mongodb://127.0.0.1";
            string databaseName = Environment.GetEnvironmentVariable("S9_MONGO_DATABASE") ??
                                  "fantasy_main";
            MongoClientSettings settings = MongoClientSettings.FromConnectionString(connection);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
            MongoClient client = new MongoClient(settings);
            _database = client.GetDatabase(databaseName);
            _database.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            _collectionName = $"s9_login_session_harness_{Environment.ProcessId}_{Guid.NewGuid():N}";
            _collection = _database.GetCollection<BsonDocument>(_collectionName);
            _collection.Indexes.CreateOne(
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Ascending("Token"),
                    new CreateIndexOptions { Unique = true, Name = "ux_s9_token" }));
        }

        public string Issue(
            long accountId,
            IProbeNonceSource nonceSource,
            long issuedAtMs,
            long tokenTtlMs)
        {
            for (int attempt = 0; attempt < MaxIssueAttempts; attempt++)
            {
                string token = LoginSessionStore.CreateToken(nonceSource);
                try
                {
                    Insert(token, accountId, issuedAtMs, checked(issuedAtMs + tokenTtlMs));
                    return token;
                }
                catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
                {
                }
            }

            throw new InvalidOperationException("Unable to issue a unique harness token.");
        }

        public void Insert(string token, long accountId, long issuedAtMs, long expiresAtMs)
        {
            _collection.InsertOne(new BsonDocument
            {
                { "Token", token },
                { "AccountId", accountId },
                { "IssuedAtMs", issuedAtMs },
                { "ExpiresAtMs", expiresAtMs }
            });
        }

        public bool TryValidate(string token, long nowMs, out long accountId)
        {
            BsonDocument document = _collection.Find(
                    Builders<BsonDocument>.Filter.Eq("Token", token))
                .FirstOrDefault();
            if (document == null)
            {
                accountId = 0L;
                return false;
            }

            long expiresAtMs = document["ExpiresAtMs"].AsInt64;
            accountId = document["AccountId"].AsInt64;
            if (expiresAtMs <= nowMs || accountId <= 0L)
            {
                _collection.DeleteOne(Builders<BsonDocument>.Filter.Eq("Token", token));
                accountId = 0L;
                return false;
            }

            return true;
        }

        public long Count(string token)
        {
            return _collection.CountDocuments(Builders<BsonDocument>.Filter.Eq("Token", token));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _database.DropCollection(_collectionName);
        }
    }
}
