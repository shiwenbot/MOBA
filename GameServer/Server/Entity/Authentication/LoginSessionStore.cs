using System;
using Fantasy.Async;
using Fantasy.Entitas;
using Fantasy.Helper;
using Fantasy.Platform.Net;
using GameShared.FrameSync.Network;
using MongoDB.Driver;

namespace Fantasy;

/// <summary>
/// Shared Mongo-backed token operations used by Authentication and Battle scenes.
/// </summary>
public static class LoginSessionStore
{
    private const int MaxTokenIssueAttempts = 5;
    private const string TokenIndexName = "ux_login_session_token";

    public static async FTask<string> Issue(Scene scene, long accountId, IProbeNonceSource nonceSource)
    {
        if (scene == null)
        {
            throw new ArgumentNullException(nameof(scene));
        }

        if (accountId <= 0L)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), accountId, "Account id must be positive.");
        }

        if (nonceSource == null)
        {
            throw new ArgumentNullException(nameof(nonceSource));
        }

        await EnsureTokenIndex(scene);
        LoginSessionConfig config = LoginSessionConfig.FromEnvironment();
        long nowMs = TimeHelper.Now;

        // The token field has a unique Mongo index. A 64-bit crypto nonce collision is
        // extremely unlikely, but the write path retries duplicate-key failures instead of
        // turning that rare event into a login outage.
        for (int attempt = 0; attempt < MaxTokenIssueAttempts; attempt++)
        {
            string token = CreateToken(nonceSource);
            LoginSession loginSession = Entity.Create<LoginSession>(scene, true, true);
            loginSession.Token = token;
            loginSession.AccountId = accountId;
            loginSession.IssuedAtMs = nowMs;
            loginSession.ExpiresAtMs = checked(nowMs + config.TokenTtlMs);

            try
            {
                await scene.World.Database.Insert(loginSession);
                return token;
            }
            catch (MongoWriteException exception) when (exception.WriteError?.Code == 11000)
            {
                // Try a fresh nonce after Mongo reports the unique-index duplicate key code.
            }
            finally
            {
                loginSession.Dispose();
            }
        }

        throw new InvalidOperationException("Unable to issue a unique login token after several attempts.");
    }

    public static async FTask<LoginSessionValidation> Validate(Scene scene, string? token)
    {
        if (scene == null || string.IsNullOrWhiteSpace(token))
        {
            return LoginSessionValidation.Invalid;
        }

        LoginSession? loginSession = await scene.World.Database.First<LoginSession>(
            d => d.Token == token.Trim());
        if (loginSession == null)
        {
            return LoginSessionValidation.Invalid;
        }

        // Join must compare expiry at read time; lazy deletion is the explicit cleanup policy,
        // not the correctness gate. Old tokens remain valid until their own natural expiry.
        if (loginSession.ExpiresAtMs <= TimeHelper.Now || loginSession.AccountId <= 0L)
        {
            await scene.World.Database.Remove<LoginSession>(loginSession.Id);
            loginSession.Dispose();
            return LoginSessionValidation.Invalid;
        }

        long accountId = loginSession.AccountId;
        loginSession.Dispose();
        return LoginSessionValidation.Valid(accountId);
    }

    internal static string CreateToken(IProbeNonceSource nonceSource)
    {
        if (nonceSource == null)
        {
            throw new ArgumentNullException(nameof(nonceSource));
        }

        return nonceSource.NextNonce().ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async FTask EnsureTokenIndex(Scene scene)
    {
        AuthenticationComponent? authentication = scene.GetComponent<AuthenticationComponent>();
        if (authentication != null && authentication.LoginSessionIndexReady)
        {
            return;
        }

        using (await scene.CoroutineLockComponent.Wait((int)LockType.Authentication_SessionIndexLock, 0))
        {
            if (authentication != null && authentication.LoginSessionIndexReady)
            {
                return;
            }

            object key = Builders<LoginSession>.IndexKeys.Ascending(d => d.Token);
            object options = new CreateIndexOptions
            {
                Unique = true,
                Name = TokenIndexName
            };
            await scene.World.Database.CreateIndex<LoginSession>(new[] { key }, new[] { options });
            if (authentication != null)
            {
                authentication.LoginSessionIndexReady = true;
            }
        }
    }
}
