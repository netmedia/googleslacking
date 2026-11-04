using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Api.Auth;

public static class AppClaims
{
    public const string TenantId = "tenant_id";
    public const string Picture = "picture";
}

/// <summary>
/// Google sign-in via the generic OAuth handler (no extra package): offline access so we get a
/// refresh token, which is stored server-side. The browser only ever gets our own cookie.
/// </summary>
public static class GoogleAuth
{
    public const string Scheme = "Google";

    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = Scheme;
            })
            .AddCookie(o =>
            {
                o.Cookie.Name = "gs.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                // SPA + SignalR: answer 401/403 instead of redirecting.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            })
            .AddOAuth(Scheme, o =>
            {
                o.ClientId = config["Google:ClientId"] ?? "";
                o.ClientSecret = config["Google:ClientSecret"] ?? "";
                o.AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
                o.TokenEndpoint = GoogleOAuthClient.TokenEndpoint;
                o.UserInformationEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
                o.CallbackPath = "/auth/callback";
                o.UsePkce = true;
                o.SaveTokens = false;
                o.Scope.Clear();
                foreach (var s in new[] { "openid", "email", "profile" }.Concat(GoogleScopes.UserScopes))
                    o.Scope.Add(s);

                o.Events.OnRedirectToAuthorizationEndpoint = ctx =>
                {
                    // offline + consent => Google always returns a refresh token.
                    var uri = ctx.RedirectUri
                              + "&access_type=offline&prompt=consent&include_granted_scopes=true";
                    ctx.Response.Redirect(uri);
                    return Task.CompletedTask;
                };
                o.Events.OnCreatingTicket = OnCreatingTicket;
                o.Events.OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/?authError=" + Uri.EscapeDataString(ctx.Failure?.Message ?? "Sign-in failed"));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static async Task OnCreatingTicket(OAuthCreatingTicketContext ctx)
    {
        var sp = ctx.HttpContext.RequestServices;
        var ct = ctx.HttpContext.RequestAborted;

        using var req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.AccessToken);
        using var res = await ctx.Backchannel.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var info = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var u = info.RootElement;

        var sub = u.GetProperty("sub").GetString()!;
        var email = u.GetProperty("email").GetString()!;
        var hd = u.TryGetProperty("hd", out var hdProp) ? hdProp.GetString() : null;
        var name = u.TryGetProperty("name", out var n) ? n.GetString() : null;
        var picture = u.TryGetProperty("picture", out var p) ? p.GetString() : null;

        if (hd is null)
        {
            ctx.Fail("Only Google Workspace accounts are supported.");
            return;
        }

        var db = sp.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Domain == hd && t.IsActive, ct);
        if (tenant is null)
        {
            ctx.Fail($"Workspace '{hd}' is not enabled for this app.");
            return;
        }
        sp.GetRequiredService<ITenantContext>().Set(tenant.Id);

        var googleUserName = "users/" + sub;
        var user = await db.Users.FirstOrDefaultAsync(x => x.GoogleUserName == googleUserName, ct);
        var now = DateTimeOffset.UtcNow;
        if (user is null)
        {
            user = new AppUser { Id = Guid.NewGuid(), GoogleUserName = googleUserName, Email = email, CreatedAt = now };
            db.Users.Add(user);
        }
        user.Email = email;
        user.DisplayName = name;
        user.AvatarUrl = picture;
        user.LastLoginAt = now;

        // Link existing memberships (seen in DMs etc. before this user signed in).
        await db.SpaceMembers.Where(m => m.GoogleUserName == googleUserName && m.AppUserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AppUserId, user.Id), ct);

        var tokens = sp.GetRequiredService<UserTokenService>();
        if (ctx.RefreshToken is { Length: > 0 } rt)
            tokens.StoreRefreshToken(user, rt, ctx.TokenResponse.Response?.RootElement.TryGetProperty("scope", out var sc) == true ? sc.GetString() : null);
        if (ctx.ExpiresIn is { } exp)
            tokens.SeedAccessToken(user.Id, ctx.AccessToken!, now.Add(exp));

        await db.SaveChangesAsync(ct);

        // Full sync on first login; afterwards only to self-heal (events keep the cache fresh).
        if (user.InitialSyncCompletedAt is null)
            sp.GetRequiredService<InitialSyncQueue>().Enqueue(tenant.Id, user.Id);

        var id = ctx.Identity!;
        id.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        id.AddClaim(new Claim(AppClaims.TenantId, tenant.Id.ToString()));
        id.AddClaim(new Claim(ClaimTypes.Email, email));
        if (name is not null) id.AddClaim(new Claim(ClaimTypes.Name, name));
        if (picture is not null) id.AddClaim(new Claim(AppClaims.Picture, picture));
    }

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());

    public static Guid TenantId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(AppClaims.TenantId) ?? throw new UnauthorizedAccessException());
}
