using System.Text.Json;
using GoogleSlacking.Api.Auth;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Api.Endpoints;

public sealed record SendMessageRequest(string Text, string? ThreadName, string ClientMessageId, List<JsonElement>? Attachments);
public sealed record MarkReadRequest(DateTimeOffset LastReadTime);
public sealed record MeDto(Guid Id, string GoogleUserName, string Email, string? DisplayName, string? AvatarUrl, bool SyncCompleted);

public static class ChatEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/login", (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties
            {
                // Only allow local return URLs (no open redirect).
                RedirectUri = returnUrl is { Length: > 0 } r && r.StartsWith('/') && !r.StartsWith("//") ? r : "/",
            }, [GoogleAuth.Scheme]));

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }

    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", async (HttpContext ctx, AppDbContext db, CancellationToken ct) =>
        {
            var id = ctx.User.UserId();
            var u = await db.Users.SingleAsync(x => x.Id == id, ct);
            return new MeDto(u.Id, u.GoogleUserName, u.Email, u.DisplayName, u.AvatarUrl, u.InitialSyncCompletedAt is not null);
        });

        api.MapGet("/spaces", (HttpContext ctx, ChatQueries q, CancellationToken ct) =>
            q.GetSpacesAsync(ctx.User.UserId(), ct));

        api.MapGet("/spaces/{spaceId:guid}/messages", async (
            Guid spaceId, DateTimeOffset? before, DateTimeOffset? around, int? limit, string? thread,
            HttpContext ctx, AppDbContext db, ChatQueries q, ChatSyncService sync, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            var take = Math.Clamp(limit ?? 50, 1, 200);

            if (around is { } at)
                return Results.Ok(await q.GetMessagesAroundAsync(spaceId, at, take / 2, ct));

            var page = await q.GetMessagesAsync(spaceId, before, take, thread, ct);
            if (page.Count < take && thread is null)
            {
                // Cache exhausted: page further back from Google, then serve from cache again.
                var space = await db.Spaces.SingleAsync(s => s.Id == spaceId, ct);
                var from = page.Count > 0 ? page.Min(m => m.CreateTime) : before ?? DateTimeOffset.UtcNow.AddSeconds(1);
                if (await sync.BackfillOlderAsync(userId, space, from, take, ct) > 0)
                    page = await q.GetMessagesAsync(spaceId, before, take, thread, ct);
            }
            return Results.Ok(page);
        });

        api.MapPost("/spaces/{spaceId:guid}/messages", async (
            Guid spaceId, SendMessageRequest body,
            HttpContext ctx, AppDbContext db, ChatQueries q, ChatSyncService sync,
            ChatApiClient chat, UserTokenService tokens, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Text) && body.Attachments is not { Count: > 0 }) return Results.BadRequest("Text is required.");
            if (!body.ClientMessageId.StartsWith("client-", StringComparison.Ordinal) || body.ClientMessageId.Length > 63)
                return Results.BadRequest("ClientMessageId must start with 'client-' and be at most 63 chars.");

            var space = await db.Spaces.SingleAsync(s => s.Id == spaceId, ct);
            var token = await tokens.GetAccessTokenAsync(db, userId, ct);
            var sent = await chat.CreateMessageAsync(token, space.GoogleName, body.Text ?? "", body.ThreadName, body.ClientMessageId,
                body.Attachments, ct);
            if (sent is null) return Results.StatusCode(502);

            // Upsert + broadcast now (other tabs/devices); the Pub/Sub echo is deduplicated.
            await sync.UpsertMessagesAsync([sent], notify: true, ct);
            return Results.Ok(await q.GetMessageByNameAsync(sent.Name, ct));
        });

        api.MapPost("/spaces/{spaceId:guid}/read", async (
            Guid spaceId, MarkReadRequest body,
            HttpContext ctx, AppDbContext db, ChatApiClient chat, UserTokenService tokens,
            IRealtimeNotifier notifier, GoogleSlacking.Infrastructure.ReadReceipts.ReadReceiptService receipts, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var member = await db.SpaceMembers.Include(m => m.Space)
                .FirstOrDefaultAsync(m => m.AppUserId == userId && m.SpaceId == spaceId, ct);
            if (member is null) return Results.NotFound();
            if (member.LastReadTime >= body.LastReadTime) return Results.NoContent();

            member.LastReadTime = body.LastReadTime;
            await db.SaveChangesAsync(ct);
            await notifier.SpacesListChangedAsync(ctx.User.TenantId(), userId, ct);
            await receipts.OnReadAsync(userId, [spaceId], ct);

            // Keep the official Google Chat clients in sync too.
            var token = await tokens.GetAccessTokenAsync(db, userId, ct);
            await chat.UpdateReadStateAsync(token, member.Space!.GoogleName, body.LastReadTime, ct);
            return Results.NoContent();
        });

        api.MapPost("/sync", (HttpContext ctx, InitialSyncQueue queue) =>
        {
            queue.Enqueue(ctx.User.TenantId(), ctx.User.UserId());
            return Results.Accepted();
        });
    }
}
