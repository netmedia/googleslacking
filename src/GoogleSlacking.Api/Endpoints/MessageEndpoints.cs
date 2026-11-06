using System.Net;
using System.Text.Json;
using GoogleSlacking.Api.Auth;
using GoogleSlacking.Core.Unfurl;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Api.Endpoints;

public sealed record EditMessageRequest(string Text);
public sealed record ReactionRequest(string Emoji);
public sealed record PinRequest(Guid MessageId);
public sealed record BookmarkRequest(string Title, string Url);
public sealed record BookmarkDto(Guid Id, string Title, string Url, DateTimeOffset CreatedAt);

/// <summary>Edit/delete, reactions, attachments, link previews, pins and bookmarks.</summary>
public static class MessageEndpoints
{
    public const long MaxUploadBytes = 200L * 1024 * 1024; // Google Chat limit

    public static void MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/messages/{id:guid}", async (Guid id, HttpContext ctx, AppDbContext db, ChatQueries q, CancellationToken ct) =>
        {
            var (_, msg) = await LoadOwnAsync(db, ctx, id, ct);
            return msg is null ? Results.NotFound() : Results.Ok(await q.GetMessageAsync(id, ct));
        });

        // ---- Edit / delete (own messages only) ----
        api.MapPatch("/messages/{id:guid}", async (Guid id, EditMessageRequest body, HttpContext ctx, AppDbContext db,
            ChatApiClient chat, UserTokenService tokens, ChatSyncService sync, ChatQueries q, CancellationToken ct) =>
        {
            var (me, msg) = await LoadOwnAsync(db, ctx, id, ct);
            if (msg is null) return Results.NotFound();
            if (msg.SenderGoogleUserName != me.GoogleUserName) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(body.Text)) return Results.BadRequest("Text is required.");

            var updated = await chat.UpdateMessageTextAsync(await tokens.GetAccessTokenAsync(db, me.Id, ct), msg.GoogleName, body.Text, ct);
            if (updated is not null) await sync.UpsertMessagesAsync([updated], notify: true, ct);
            return Results.Ok(await q.GetMessageAsync(id, ct));
        });

        api.MapDelete("/messages/{id:guid}", async (Guid id, HttpContext ctx, AppDbContext db,
            ChatApiClient chat, UserTokenService tokens, ChatSyncService sync, CancellationToken ct) =>
        {
            var (me, msg) = await LoadOwnAsync(db, ctx, id, ct);
            if (msg is null) return Results.NotFound();
            if (msg.SenderGoogleUserName != me.GoogleUserName) return Results.Forbid();

            try
            {
                await chat.DeleteMessageAsync(await tokens.GetAccessTokenAsync(db, me.Id, ct), msg.GoogleName, ct);
            }
            catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.NotFound) { /* already gone */ }
            await sync.MarkDeletedAsync([msg.GoogleName], ct);
            return Results.NoContent();
        });

        // ---- Reactions ----
        api.MapPost("/messages/{id:guid}/reactions", async (Guid id, ReactionRequest body, HttpContext ctx, AppDbContext db,
            ChatApiClient chat, UserTokenService tokens, ChatSyncService sync, ChatQueries q, CancellationToken ct) =>
        {
            var (me, msg) = await LoadOwnAsync(db, ctx, id, ct);
            if (msg is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Emoji) || body.Emoji.Length > 32) return Results.BadRequest();

            var token = await tokens.GetAccessTokenAsync(db, me.Id, ct);
            ChatReaction? created = null;
            try
            {
                created = await chat.CreateReactionAsync(token, msg.GoogleName, body.Emoji, ct);
            }
            catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.Conflict) { /* already reacted */ }

            if (created is not null)
                await sync.ApplyReactionAsync(created with
                {
                    User = created.User ?? new ChatUser { Name = me.GoogleUserName },
                    Emoji = created.Emoji ?? new ChatEmoji { Unicode = body.Emoji },
                }, removed: false, ct);

            await RefreshAndBroadcastAsync(chat, token, sync, db, msg, ct);
            return Results.Ok(await q.GetMessageAsync(id, ct));
        });

        api.MapDelete("/messages/{id:guid}/reactions", async (Guid id, [FromQuery] string emoji, HttpContext ctx, AppDbContext db,
            ChatApiClient chat, UserTokenService tokens, ChatSyncService sync, ChatQueries q, CancellationToken ct) =>
        {
            var (me, msg) = await LoadOwnAsync(db, ctx, id, ct);
            if (msg is null) return Results.NotFound();
            var token = await tokens.GetAccessTokenAsync(db, me.Id, ct);

            var names = await db.Reactions
                .Where(r => r.MessageId == id && r.UserGoogleName == me.GoogleUserName && r.Emoji == emoji)
                .Select(r => r.GoogleName).ToListAsync(ct);
            if (names.Count == 0)
                names = (await chat.ListReactionsAsync(token, msg.GoogleName, emoji, me.GoogleUserName, ct)).Select(r => r.Name).ToList();

            foreach (var name in names)
            {
                try
                {
                    await chat.DeleteReactionAsync(token, name, ct);
                }
                catch (GoogleApiException ex) when (ex.Status == HttpStatusCode.NotFound) { }
                await sync.ApplyReactionAsync(new ChatReaction { Name = name }, removed: true, ct);
            }

            await RefreshAndBroadcastAsync(chat, token, sync, db, msg, ct);
            return Results.Ok(await q.GetMessageAsync(id, ct));
        });

        // ---- Attachments ----
        api.MapPost("/spaces/{spaceId:guid}/attachments", async (Guid spaceId, IFormFile file, HttpContext ctx, AppDbContext db,
            ChatQueries q, ChatApiClient chat, UserTokenService tokens, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            if (file.Length == 0 || file.Length > MaxUploadBytes) return Results.BadRequest("File is empty or larger than 200 MB.");

            var space = await db.Spaces.SingleAsync(s => s.Id == spaceId, ct);
            await using var stream = file.OpenReadStream();
            var uploaded = await chat.UploadAttachmentAsync(await tokens.GetAccessTokenAsync(db, userId, ct),
                space.GoogleName, file.FileName, file.ContentType, stream, ct);
            // Opaque upload reference: the client sends it back in SendMessageRequest.Attachments.
            return Results.Ok(uploaded);
        })
        .DisableAntiforgery() // cookie is SameSite=Lax and the SPA is same-origin
        .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes + 1024 * 1024));

        api.MapGet("/messages/{id:guid}/attachments/{index:int}", async (Guid id, int index, bool? download, HttpContext ctx,
            AppDbContext db, ChatQueries q, ChatApiClient chat, UserTokenService tokens, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (msg?.AttachmentsJson is null || !await q.IsMemberAsync(userId, msg.SpaceId, ct)) return Results.NotFound();

            using var doc = JsonDocument.Parse(msg.AttachmentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || index < 0 || index >= doc.RootElement.GetArrayLength())
                return Results.NotFound();
            var a = doc.RootElement[index];
            var name = Str(a, "contentName") ?? "file";
            var type = Str(a, "contentType") ?? "application/octet-stream";

            if (a.TryGetProperty("attachmentDataRef", out var dataRef) && Str(dataRef, "resourceName") is { } resource)
            {
                var res = await chat.DownloadMediaAsync(await tokens.GetAccessTokenAsync(db, userId, ct), resource, ct);
                ctx.Response.RegisterForDispose(res);
                ctx.Response.Headers.CacheControl = "private, max-age=86400";
                return Results.Stream(await res.Content.ReadAsStreamAsync(ct),
                    res.Content.Headers.ContentType?.ToString() ?? type,
                    download == true ? name : null);
            }

            // Google Drive files and others: open in Google.
            return Str(a, "downloadUri") is { } uri ? Results.Redirect(uri) : Results.NotFound();
        });

        // ---- Link previews ----
        api.MapGet("/unfurl", async ([FromQuery] string url, LinkPreviewService unfurl, CancellationToken ct) =>
            await unfurl.GetAsync(url, ct) is { } preview ? Results.Ok(preview) : Results.NoContent());

        // ---- Pins (shared by app users of the space) ----
        api.MapGet("/spaces/{spaceId:guid}/pins", async (Guid spaceId, HttpContext ctx, AppDbContext db, ChatQueries q, CancellationToken ct) =>
        {
            if (!await q.IsMemberAsync(ctx.User.UserId(), spaceId, ct)) return Results.NotFound();
            var msgs = await db.Pins.Where(p => p.SpaceId == spaceId).OrderByDescending(p => p.PinnedAt)
                .Join(db.Messages, p => p.MessageId, m => m.Id, (p, m) => m).ToListAsync(ct);
            return Results.Ok(await q.ToDtosAsync(msgs, ct));
        });

        api.MapPost("/spaces/{spaceId:guid}/pins", async (Guid spaceId, PinRequest body, HttpContext ctx, AppDbContext db,
            ChatQueries q, ChatSyncService sync, IRealtimeNotifier notifier, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == body.MessageId && m.SpaceId == spaceId, ct);
            if (msg is null) return Results.NotFound();
            if (!await db.Pins.AnyAsync(p => p.SpaceId == spaceId && p.MessageId == msg.Id, ct))
            {
                db.Pins.Add(new PinnedMessage { Id = Guid.NewGuid(), SpaceId = spaceId, MessageId = msg.Id, PinnedByUserId = userId, PinnedAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(ct);
            }
            await sync.NotifyMessagesAsync([msg], ct);
            await notifier.ToSpaceAsync(spaceId, "pinsChanged", null, ct);
            return Results.NoContent();
        });

        api.MapDelete("/spaces/{spaceId:guid}/pins/{messageId:guid}", async (Guid spaceId, Guid messageId, HttpContext ctx,
            AppDbContext db, ChatQueries q, ChatSyncService sync, IRealtimeNotifier notifier, CancellationToken ct) =>
        {
            if (!await q.IsMemberAsync(ctx.User.UserId(), spaceId, ct)) return Results.NotFound();
            await db.Pins.Where(p => p.SpaceId == spaceId && p.MessageId == messageId).ExecuteDeleteAsync(ct);
            if (await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct) is { } msg)
                await sync.NotifyMessagesAsync([msg], ct);
            await notifier.ToSpaceAsync(spaceId, "pinsChanged", null, ct);
            return Results.NoContent();
        });

        // ---- Bookmarks (links in the space header) ----
        api.MapGet("/spaces/{spaceId:guid}/bookmarks", async (Guid spaceId, HttpContext ctx, AppDbContext db, ChatQueries q, CancellationToken ct) =>
        {
            if (!await q.IsMemberAsync(ctx.User.UserId(), spaceId, ct)) return Results.NotFound();
            return Results.Ok(await db.Bookmarks.Where(b => b.SpaceId == spaceId).OrderBy(b => b.SortOrder).ThenBy(b => b.CreatedAt)
                .Select(b => new BookmarkDto(b.Id, b.Title, b.Url, b.CreatedAt)).ToListAsync(ct));
        });

        api.MapPost("/spaces/{spaceId:guid}/bookmarks", async (Guid spaceId, BookmarkRequest body, HttpContext ctx, AppDbContext db,
            ChatQueries q, IRealtimeNotifier notifier, TimeProvider clock, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            if (!Uri.TryCreate(body.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return Results.BadRequest("Neispravan URL.");
            var count = await db.Bookmarks.CountAsync(b => b.SpaceId == spaceId, ct);
            var b = new SpaceBookmark
            {
                Id = Guid.NewGuid(), SpaceId = spaceId, Url = uri.AbsoluteUri, CreatedByUserId = userId, CreatedAt = clock.GetUtcNow(),
                Title = string.IsNullOrWhiteSpace(body.Title) ? uri.Host : body.Title.Trim(), SortOrder = count,
            };
            db.Bookmarks.Add(b);
            await db.SaveChangesAsync(ct);
            await notifier.ToSpaceAsync(spaceId, "bookmarksChanged", null, ct);
            return Results.Ok(new BookmarkDto(b.Id, b.Title, b.Url, b.CreatedAt));
        });

        api.MapDelete("/spaces/{spaceId:guid}/bookmarks/{id:guid}", async (Guid spaceId, Guid id, HttpContext ctx, AppDbContext db,
            ChatQueries q, IRealtimeNotifier notifier, CancellationToken ct) =>
        {
            if (!await q.IsMemberAsync(ctx.User.UserId(), spaceId, ct)) return Results.NotFound();
            await db.Bookmarks.Where(b => b.Id == id && b.SpaceId == spaceId).ExecuteDeleteAsync(ct);
            await notifier.ToSpaceAsync(spaceId, "bookmarksChanged", null, ct);
            return Results.NoContent();
        });
    }

    /// <summary>Loads the current user and a message in one of their spaces (null if not visible).</summary>
    internal static async Task<(AppUser Me, Message? Msg)> LoadOwnAsync(AppDbContext db, HttpContext ctx, Guid messageId, CancellationToken ct)
    {
        var userId = ctx.User.UserId();
        var me = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId
            && db.SpaceMembers.Any(sm => sm.SpaceId == m.SpaceId && sm.AppUserId == userId), ct);
        return (me, msg);
    }

    private static async Task RefreshAndBroadcastAsync(ChatApiClient chat, string token, ChatSyncService sync, AppDbContext db,
        Message msg, CancellationToken ct)
    {
        try
        {
            if (await chat.GetMessageAsync(token, msg.GoogleName, ct) is { } fresh)
                await sync.UpsertMessagesAsync([fresh], notify: false, ct);
        }
        catch (GoogleApiException) { /* best effort: summary refresh */ }
        var current = await db.Messages.SingleAsync(m => m.Id == msg.Id, ct);
        await sync.NotifyMessagesAsync([current], ct);
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
