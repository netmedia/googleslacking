using System.Collections.Concurrent;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoogleSlacking.Infrastructure.Auth;

public sealed class ReauthenticationRequiredException(Guid userId)
    : Exception($"User {userId} must sign in again (refresh token missing or revoked).");

/// <summary>
/// Stores refresh tokens (encrypted with Data Protection) and hands out cached access tokens.
/// Singleton-safe cache; DB access goes through the scoped DbContext passed in.
/// </summary>
public sealed class UserTokenService(
    IDataProtectionProvider dp,
    GoogleOAuthClient oauth,
    TimeProvider clock,
    ILogger<UserTokenService> log)
{
    private const string Purpose = "GoogleSlacking.RefreshTokens.v1";
    private static readonly ConcurrentDictionary<Guid, (string Token, DateTimeOffset Expires)> Cache = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private IDataProtector Protector => dp.CreateProtector(Purpose);

    public void StoreRefreshToken(AppUser user, string refreshToken, string? scopes)
    {
        user.ProtectedRefreshToken = Protector.Protect(refreshToken);
        user.GrantedScopes = scopes;
        Cache.TryRemove(user.Id, out _);
    }

    /// <summary>Seeds the cache with the access token we just got from the sign-in flow.</summary>
    public void SeedAccessToken(Guid userId, string accessToken, DateTimeOffset expires) =>
        Cache[userId] = (accessToken, expires.AddMinutes(-2));

    public async Task<string> GetAccessTokenAsync(AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(userId, out var c) && clock.GetUtcNow() < c.Expires) return c.Token;

        var gate = Locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(userId, out c) && clock.GetUtcNow() < c.Expires) return c.Token;

            var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct)
                       ?? throw new ReauthenticationRequiredException(userId);
            if (user.ProtectedRefreshToken is null) throw new ReauthenticationRequiredException(userId);

            TokenResponse token;
            try
            {
                token = await oauth.RefreshAsync(Protector.Unprotect(user.ProtectedRefreshToken), ct);
            }
            catch (GoogleApiException ex) when (ex.Body.Contains("invalid_grant", StringComparison.Ordinal))
            {
                log.LogWarning("Refresh token revoked for user {UserId}", userId);
                user.ProtectedRefreshToken = null;
                await db.SaveChangesAsync(ct);
                throw new ReauthenticationRequiredException(userId);
            }

            var expires = clock.GetUtcNow().AddSeconds(token.ExpiresIn - 120);
            Cache[userId] = (token.AccessToken, expires);
            return token.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }
}
