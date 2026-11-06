using GoogleSlacking.Api.Auth;
using GoogleSlacking.Domain;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Search;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Api.Endpoints;

public sealed record SaveRequest(Guid MessageId, DateTimeOffset? RemindAt);
public sealed record SavedPatchRequest(DateTimeOffset? RemindAt, bool? ClearReminder, bool? Completed);
public sealed record ScheduleRequest(Guid SpaceId, string? ThreadName, string Text, DateTimeOffset SendAt);
public sealed record SchedulePatchRequest(string? Text, DateTimeOffset? SendAt);
public sealed record DraftRequest(Guid SpaceId, string? ThreadName, string Text);

/// <summary>Search, "Later" (saved + reminders), scheduled messages and synced drafts.</summary>
public static class LaterEndpoints
{
    public static void MapLaterEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---- Search ----
        api.MapGet("/search", (string? q, Guid? spaceId, int? limit, HttpContext ctx, SearchService search, CancellationToken ct) =>
            search.SearchAsync(ctx.User.UserId(), q ?? "", spaceId, limit ?? 50, ct));

        // ---- Later: saved messages with optional reminder ----
        api.MapGet("/saved", async (bool? completed, HttpContext ctx, AppDbContext db, ChatQueries q, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var items = await db.SavedItems
                .Where(s => s.AppUserId == userId && (completed == true ? s.CompletedAt != null : s.CompletedAt == null))
                .OrderBy(s => s.RemindAt == null).ThenBy(s => s.RemindAt).ThenByDescending(s => s.CreatedAt)
                .Take(200).ToListAsync(ct);
            return await ToSavedDtosAsync(db, q, userId, items, ct);
        });

        api.MapPost("/saved", async (SaveRequest body, HttpContext ctx, AppDbContext db, ChatQueries q, TimeProvider clock,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == body.MessageId, ct);
            if (msg is null || !await q.IsMemberAsync(userId, msg.SpaceId, ct)) return Results.NotFound();

            var item = await db.SavedItems.FirstOrDefaultAsync(s => s.AppUserId == userId && s.MessageId == msg.Id, ct);
            if (item is null)
            {
                item = new SavedItem { Id = Guid.NewGuid(), AppUserId = userId, MessageId = msg.Id, CreatedAt = clock.GetUtcNow() };
                db.SavedItems.Add(item);
            }
            item.CompletedAt = null;
            item.RemindAt = body.RemindAt;
            item.RemindedAt = null;
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "savedChanged", null, ct);
            return Results.Ok(new { item.Id });
        });

        api.MapPatch("/saved/{id:guid}", async (Guid id, SavedPatchRequest body, HttpContext ctx, AppDbContext db, TimeProvider clock,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var item = await db.SavedItems.FirstOrDefaultAsync(s => s.Id == id && s.AppUserId == userId, ct);
            if (item is null) return Results.NotFound();
            if (body.ClearReminder == true) item.RemindAt = null;
            else if (body.RemindAt is { } r) { item.RemindAt = r; item.RemindedAt = null; }
            if (body.Completed is { } c) item.CompletedAt = c ? clock.GetUtcNow() : null;
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "savedChanged", null, ct);
            return Results.NoContent();
        });

        api.MapDelete("/saved/{id:guid}", async (Guid id, HttpContext ctx, AppDbContext db, IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            await db.SavedItems.Where(s => s.Id == id && s.AppUserId == userId).ExecuteDeleteAsync(ct);
            await rt.ToUserAsync(userId, "savedChanged", null, ct);
            return Results.NoContent();
        });

        // ---- Scheduled messages ----
        api.MapGet("/scheduled", async (HttpContext ctx, AppDbContext db, ChatQueries q, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var items = await db.ScheduledMessages
                .Where(s => s.AppUserId == userId && (s.Status == ScheduledMessageStatus.Pending || s.Status == ScheduledMessageStatus.Failed))
                .OrderBy(s => s.SendAt).ToListAsync(ct);
            var names = (await q.GetSpacesAsync(userId, ct)).ToDictionary(s => s.Id, s => s.DisplayName);
            return items.Select(s => new ScheduledMessageDto(s.Id, s.SpaceId, names.GetValueOrDefault(s.SpaceId) ?? "", s.ThreadName,
                s.Text, s.SendAt, s.Status, s.Error)).ToList();
        });

        api.MapPost("/scheduled", async (ScheduleRequest body, HttpContext ctx, AppDbContext db, ChatQueries q, TimeProvider clock,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, body.SpaceId, ct)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.Text)) return Results.BadRequest("Text is required.");
            if (body.SendAt < clock.GetUtcNow().AddSeconds(-30)) return Results.BadRequest("Vrijeme slanja je u prošlosti.");
            var item = new ScheduledMessage
            {
                Id = Guid.NewGuid(), AppUserId = userId, SpaceId = body.SpaceId, ThreadName = body.ThreadName,
                Text = body.Text, SendAt = body.SendAt, CreatedAt = clock.GetUtcNow(),
            };
            db.ScheduledMessages.Add(item);
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "scheduledChanged", null, ct);
            return Results.Ok(new { item.Id });
        });

        api.MapPatch("/scheduled/{id:guid}", async (Guid id, SchedulePatchRequest body, HttpContext ctx, AppDbContext db,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var item = await db.ScheduledMessages.FirstOrDefaultAsync(s => s.Id == id && s.AppUserId == userId, ct);
            if (item is null || item.Status == ScheduledMessageStatus.Sent) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(body.Text)) item.Text = body.Text;
            if (body.SendAt is { } at) item.SendAt = at;
            item.Status = ScheduledMessageStatus.Pending; // also retries failed ones
            item.Error = null;
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "scheduledChanged", null, ct);
            return Results.NoContent();
        });

        api.MapDelete("/scheduled/{id:guid}", async (Guid id, HttpContext ctx, AppDbContext db, IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            await db.ScheduledMessages.Where(s => s.Id == id && s.AppUserId == userId && s.Status != ScheduledMessageStatus.Sent)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ScheduledMessageStatus.Cancelled), ct);
            await rt.ToUserAsync(userId, "scheduledChanged", null, ct);
            return Results.NoContent();
        });

        // ---- Drafts (synced across devices) ----
        api.MapGet("/drafts", async (HttpContext ctx, AppDbContext db, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            return (await db.Drafts.Where(d => d.AppUserId == userId).OrderByDescending(d => d.UpdatedAt).ToListAsync(ct))
                .Select(d => new DraftDto(d.SpaceId, d.ThreadKey == "" ? null : d.ThreadKey, d.Text, d.UpdatedAt)).ToList();
        });

        api.MapPut("/drafts", async (DraftRequest body, HttpContext ctx, AppDbContext db, ChatQueries q, TimeProvider clock,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, body.SpaceId, ct)) return Results.NotFound();
            var key = body.ThreadName ?? "";
            var d = await db.Drafts.FirstOrDefaultAsync(x => x.AppUserId == userId && x.SpaceId == body.SpaceId && x.ThreadKey == key, ct);
            var now = clock.GetUtcNow();

            if (string.IsNullOrWhiteSpace(body.Text))
            {
                if (d is not null) db.Drafts.Remove(d);
            }
            else if (d is null)
            {
                db.Drafts.Add(new Draft { Id = Guid.NewGuid(), AppUserId = userId, SpaceId = body.SpaceId, ThreadKey = key, Text = body.Text, UpdatedAt = now });
            }
            else
            {
                d.Text = body.Text;
                d.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "draftsChanged",
                new DraftDto(body.SpaceId, body.ThreadName, string.IsNullOrWhiteSpace(body.Text) ? "" : body.Text, now), ct);
            return Results.NoContent();
        });
    }

    private static async Task<List<SavedItemDto>> ToSavedDtosAsync(AppDbContext db, ChatQueries q, Guid userId,
        List<SavedItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.MessageId).ToList();
        var msgs = await db.Messages.Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        var dtos = (await q.ToDtosAsync(msgs, ct)).ToDictionary(m => m.Id);
        var names = (await q.GetSpacesAsync(userId, ct)).ToDictionary(s => s.Id, s => s.DisplayName);
        return items.Where(i => dtos.ContainsKey(i.MessageId))
            .Select(i =>
            {
                var m = dtos[i.MessageId];
                return new SavedItemDto(i.Id, m, names.GetValueOrDefault(m.SpaceId) ?? "", i.CreatedAt, i.RemindAt, i.CompletedAt);
            }).ToList();
    }
}
