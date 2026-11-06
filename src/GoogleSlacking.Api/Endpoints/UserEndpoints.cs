using GoogleSlacking.Api.Auth;
using GoogleSlacking.Core.WebPush;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Notifications;
using GoogleSlacking.Infrastructure.ReadReceipts;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Api.Endpoints;

public sealed record SnoozeRequest(int? Minutes);
public sealed record PushKeys(string P256dh, string Auth);
public sealed record PushSubscribeRequest(string Endpoint, PushKeys Keys, string? UserAgent);
public sealed record PushUnsubscribeRequest(string Endpoint);
public sealed record ActivityReadRequest(List<Guid>? Ids);
public sealed record ActivityPage(IReadOnlyList<ActivityDto> Items, int Unread);
public sealed record SectionDto(Guid Id, string Name, int SortOrder, bool IsCollapsed);
public sealed record SectionRequest(string? Name, int? SortOrder, bool? IsCollapsed);
public sealed record SpacePrefsRequest(NotifyLevel? NotifyLevel, bool? IsMuted, bool? IsStarred, Guid? SectionId, bool? ClearSection, int? SortOrder);
public sealed record CreateSpaceRequest(string Kind, string? DisplayName, List<string> Members);
public sealed record AddMembersRequest(List<string> Members);
public sealed record MemberDto(string Id, string? DisplayName, string? AvatarUrl, string? Email, bool IsAppUser);

/// <summary>Settings, status/presence, Web Push, activity, sections, per-space prefs, people and space management.</summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // ---- Settings / DND / status ----
        api.MapGet("/settings", async (HttpContext ctx, AppDbContext db, ReadReceiptService receipts, CancellationToken ct) =>
            ToDto(await GetOrCreateSettingsAsync(db, ctx.User.UserId(), ct), await receipts.TenantEnabledAsync(ct)));

        api.MapPut("/settings", async (SettingsDto body, HttpContext ctx, AppDbContext db, IRealtimeNotifier rt,
            ReadReceiptService receipts, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var s = await GetOrCreateSettingsAsync(db, userId, ct);
            var receiptsAvailable = await receipts.TenantEnabledAsync(ct);
            var sharingChanged = s.ShareReadReceipts != (body.ShareReadReceipts && receiptsAvailable);
            s.ShareReadReceipts = body.ShareReadReceipts && receiptsAvailable;
            s.HideTyping = body.HideTyping;
            s.Keywords = body.Keywords?.Trim();
            s.TimeZone = string.IsNullOrWhiteSpace(body.TimeZone) ? "Europe/Zagreb" : body.TimeZone;
            s.DndScheduleEnabled = body.DndScheduleEnabled;
            s.DndStart = body.DndStart;
            s.DndEnd = body.DndEnd;
            s.SnoozeUntil = body.SnoozeUntil;
            s.NotifyThreadReplies = body.NotifyThreadReplies;
            s.PushOnlyWhenInactive = body.PushOnlyWhenInactive;
            s.StatusEmoji = body.StatusEmoji;
            s.StatusText = body.StatusText?.Trim();
            s.StatusExpiresAt = body.StatusExpiresAt;
            s.ForceAway = body.ForceAway;
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "settingsChanged", null, ct);
            await rt.ToTenantPresenceAsync(ctx, ct);
            if (sharingChanged)
            {
                // Others in my conversations should start/stop seeing my read state right away.
                var spaceIds = await db.SpaceMembers.Where(m => m.AppUserId == userId).Select(m => m.SpaceId).ToListAsync(ct);
                foreach (var id in spaceIds) await rt.ToSpaceAsync(id, "readReceiptsChanged", null, ct);
            }
            return ToDto(s, receiptsAvailable);
        });

        api.MapPost("/settings/snooze", async (SnoozeRequest body, HttpContext ctx, AppDbContext db, TimeProvider clock,
            IRealtimeNotifier rt, ReadReceiptService receipts, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var s = await GetOrCreateSettingsAsync(db, userId, ct);
            s.SnoozeUntil = body.Minutes is > 0 ? clock.GetUtcNow().AddMinutes(body.Minutes.Value) : null;
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "settingsChanged", null, ct);
            return ToDto(s, await receipts.TenantEnabledAsync(ct));
        });

        // ---- Read receipts (reciprocal opt-in; app users only) ----
        api.MapGet("/spaces/{spaceId:guid}/read-receipts", async (Guid spaceId, HttpContext ctx, ChatQueries q,
            ReadReceiptService receipts, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            return Results.Ok(await receipts.GetAsync(userId, spaceId, ct));
        });

        api.MapGet("/presence", async (HttpContext ctx, AppDbContext db, IPresence presence, TimeProvider clock, CancellationToken ct) =>
        {
            var snapshot = presence.Snapshot(ctx.User.TenantId());
            var now = clock.GetUtcNow();
            var users = await db.Users.Select(u => new { u.Id, u.GoogleUserName }).ToListAsync(ct);
            var settings = await db.UserSettings.ToDictionaryAsync(s => s.AppUserId, ct);
            return users.Select(u =>
            {
                settings.TryGetValue(u.Id, out var s);
                var state = snapshot.TryGetValue(u.Id, out var st) ? (s?.ForceAway == true ? "away" : st) : "offline";
                var statusValid = s?.StatusExpiresAt is null || s.StatusExpiresAt > now;
                return new PresenceDto(u.GoogleUserName, state,
                    statusValid ? s?.StatusEmoji : null, statusValid ? s?.StatusText : null);
            }).ToList();
        });

        // ---- Web Push ----
        api.MapGet("/push/key", (IOptions<WebPushOptions> o) =>
            o.Value.IsConfigured ? Results.Ok(new { publicKey = o.Value.VapidPublicKey }) : Results.NoContent());

        api.MapPost("/push/subscribe", async (PushSubscribeRequest body, HttpContext ctx, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(body.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") return Results.BadRequest();
            var userId = ctx.User.UserId();
            // Endpoint is globally unique; a browser profile may have switched users.
            var existing = await db.WebPushSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Endpoint == body.Endpoint, ct);
            if (existing is not null && existing.AppUserId != userId)
            {
                db.WebPushSubscriptions.Remove(existing);
                await db.SaveChangesAsync(ct);
                existing = null;
            }
            if (existing is null)
            {
                db.WebPushSubscriptions.Add(new WebPushSubscription
                {
                    Id = Guid.NewGuid(), AppUserId = userId, Endpoint = body.Endpoint, P256dh = body.Keys.P256dh, Auth = body.Keys.Auth,
                    UserAgent = body.UserAgent?.Length > 500 ? body.UserAgent[..500] : body.UserAgent, CreatedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                existing.P256dh = body.Keys.P256dh;
                existing.Auth = body.Keys.Auth;
                existing.FailureCount = 0;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/push/unsubscribe", async (PushUnsubscribeRequest body, HttpContext ctx, AppDbContext db, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            await db.WebPushSubscriptions.Where(s => s.Endpoint == body.Endpoint && s.AppUserId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        api.MapPost("/push/test", async (HttpContext ctx, AppDbContext db, NotificationService notifications, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var space = await db.SpaceMembers.Where(m => m.AppUserId == userId).Select(m => m.SpaceId).FirstOrDefaultAsync(ct);
            var sent = await notifications.SendPushAsync(userId, new NotificationPayload(
                "GoogleSlacking", "Obavijesti rade 🎉", space, null, "test", ActivityKind.DirectMessage, null, "test-" + Guid.NewGuid()), ct);
            return Results.Ok(new { delivered = sent });
        });

        // ---- Activity ----
        api.MapGet("/activity", async (int? limit, HttpContext ctx, AppDbContext db, ChatQueries q, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var items = await db.Activity.Where(a => a.AppUserId == userId)
                .OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(limit ?? 50, 1, 200)).ToListAsync(ct);
            var unread = await db.Activity.CountAsync(a => a.AppUserId == userId && a.ReadAt == null, ct);

            var msgIds = items.Where(i => i.MessageId != null).Select(i => i.MessageId!.Value).Distinct().ToList();
            var msgs = await db.Messages.Where(m => msgIds.Contains(m.Id)).ToListAsync(ct);
            var dtos = (await q.ToDtosAsync(msgs, ct)).ToDictionary(m => m.Id);
            var spaceNames = (await q.GetSpacesAsync(userId, ct)).ToDictionary(s => s.Id, s => s.DisplayName);
            var actorNames = items.Select(i => i.ActorGoogleUserName).OfType<string>().Distinct().ToList();
            var actors = await db.People.Where(p => actorNames.Contains(p.GoogleUserName)).ToDictionaryAsync(p => p.GoogleUserName, ct);

            return new ActivityPage(items.Select(i => new ActivityDto(
                i.Id, i.Kind, i.SpaceId, spaceNames.GetValueOrDefault(i.SpaceId) ?? "",
                i.MessageId is { } mid ? dtos.GetValueOrDefault(mid) : null,
                i.ActorGoogleUserName is { } an ? ChatQueries.ToPerson(an, actors.GetValueOrDefault(an)) : null,
                i.Preview, i.CreatedAt, i.ReadAt is not null)).ToList(), unread);
        });

        api.MapPost("/activity/read", async (ActivityReadRequest body, HttpContext ctx, AppDbContext db, TimeProvider clock,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var q = db.Activity.Where(a => a.AppUserId == userId && a.ReadAt == null);
            if (body.Ids is { Count: > 0 } ids) q = q.Where(a => ids.Contains(a.Id));
            var now = clock.GetUtcNow();
            await q.ExecuteUpdateAsync(s => s.SetProperty(a => a.ReadAt, now), ct);
            await rt.ToUserAsync(userId, "activityChanged", null, ct);
            return Results.NoContent();
        });

        // ---- Sidebar sections + per-space preferences ----
        api.MapGet("/sections", async (HttpContext ctx, AppDbContext db, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            return await db.Sections.Where(s => s.AppUserId == userId).OrderBy(s => s.SortOrder)
                .Select(s => new SectionDto(s.Id, s.Name, s.SortOrder, s.IsCollapsed)).ToListAsync(ct);
        });

        api.MapPost("/sections", async (SectionRequest body, HttpContext ctx, AppDbContext db, IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (string.IsNullOrWhiteSpace(body.Name)) return Results.BadRequest();
            var max = await db.Sections.Where(s => s.AppUserId == userId).MaxAsync(s => (int?)s.SortOrder, ct) ?? -1;
            var section = new UserSection { Id = Guid.NewGuid(), AppUserId = userId, Name = body.Name.Trim(), SortOrder = max + 1 };
            db.Sections.Add(section);
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "sectionsChanged", null, ct);
            return Results.Ok(new SectionDto(section.Id, section.Name, section.SortOrder, false));
        });

        api.MapPatch("/sections/{id:guid}", async (Guid id, SectionRequest body, HttpContext ctx, AppDbContext db,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var s = await db.Sections.FirstOrDefaultAsync(x => x.Id == id && x.AppUserId == userId, ct);
            if (s is null) return Results.NotFound();
            if (!string.IsNullOrWhiteSpace(body.Name)) s.Name = body.Name.Trim();
            if (body.SortOrder is { } so) s.SortOrder = so;
            if (body.IsCollapsed is { } c) s.IsCollapsed = c;
            await db.SaveChangesAsync(ct);
            await rt.ToUserAsync(userId, "sectionsChanged", null, ct);
            return Results.NoContent();
        });

        api.MapDelete("/sections/{id:guid}", async (Guid id, HttpContext ctx, AppDbContext db, IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            await db.SpaceMembers.Where(m => m.AppUserId == userId && m.SectionId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.SectionId, (Guid?)null), ct);
            await db.Sections.Where(s => s.Id == id && s.AppUserId == userId).ExecuteDeleteAsync(ct);
            await rt.ToUserAsync(userId, "sectionsChanged", null, ct);
            await rt.ToUserAsync(userId, "spacesChanged", null, ct);
            return Results.NoContent();
        });

        api.MapPut("/spaces/{spaceId:guid}/prefs", async (Guid spaceId, SpacePrefsRequest body, HttpContext ctx, AppDbContext db,
            IRealtimeNotifier rt, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var m = await db.SpaceMembers.FirstOrDefaultAsync(x => x.SpaceId == spaceId && x.AppUserId == userId, ct);
            if (m is null) return Results.NotFound();
            if (body.NotifyLevel is { } nl) m.NotifyLevel = nl;
            if (body.IsMuted is { } mu) m.IsMuted = mu;
            if (body.IsStarred is { } st) m.IsStarred = st;
            if (body.ClearSection == true) m.SectionId = null;
            else if (body.SectionId is { } sid && await db.Sections.AnyAsync(s => s.Id == sid && s.AppUserId == userId, ct)) m.SectionId = sid;
            if (body.SortOrder is { } so) m.SortOrder = so;
            await db.SaveChangesAsync(ct);
            await rt.SpacesListChangedAsync(ctx.User.TenantId(), userId, ct);
            return Results.NoContent();
        });

        api.MapPost("/read-all", async (HttpContext ctx, AppDbContext db, ChatApiClient chat, UserTokenService tokens,
            IRealtimeNotifier rt, ReadReceiptService receipts, ILoggerFactory lf, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var unread = await db.SpaceMembers.Include(m => m.Space)
                .Where(m => m.AppUserId == userId && m.Space!.LastActiveTime != null
                            && (m.LastReadTime == null || m.LastReadTime < m.Space.LastActiveTime))
                .ToListAsync(ct);
            foreach (var m in unread) m.LastReadTime = m.Space!.LastActiveTime;
            await db.SaveChangesAsync(ct);
            await rt.SpacesListChangedAsync(ctx.User.TenantId(), userId, ct);
            await receipts.OnReadAsync(userId, unread.Select(m => m.SpaceId).ToList(), ct);

            // Mirror to Google so official clients agree (best effort, bounded).
            var token = await tokens.GetAccessTokenAsync(db, userId, ct);
            foreach (var m in unread.Take(50))
            {
                try { await chat.UpdateReadStateAsync(token, m.Space!.GoogleName, m.LastReadTime!.Value, ct); }
                catch (GoogleApiException ex) { lf.CreateLogger("ReadAll").LogDebug(ex, "Read state sync failed"); }
            }
            return Results.NoContent();
        });

        // ---- People, members, new conversations ----
        api.MapGet("/people/search", async (string q, HttpContext ctx, AppDbContext db, PeopleApiClient people,
            UserTokenService tokens, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Results.Ok(Array.Empty<MemberDto>());
            var userId = ctx.User.UserId();
            var found = await people.SearchDirectoryAsync(await tokens.GetAccessTokenAsync(db, userId, ct), q.Trim(), ct);

            // Keep the local people cache warm (names in messages, mentions).
            var names = found.Select(f => f.GoogleUserName).ToList();
            var known = await db.People.Where(p => names.Contains(p.GoogleUserName)).ToDictionaryAsync(p => p.GoogleUserName, ct);
            foreach (var f in found)
            {
                if (!known.TryGetValue(f.GoogleUserName, out var p))
                    db.People.Add(p = new Person { Id = Guid.NewGuid(), GoogleUserName = f.GoogleUserName });
                p.DisplayName = f.DisplayName ?? p.DisplayName;
                p.Email = f.Email ?? p.Email;
                p.AvatarUrl = f.AvatarUrl ?? p.AvatarUrl;
                p.ResolvedAt = clock.GetUtcNow();
            }
            await db.SaveChangesAsync(ct);

            var appUsers = (await db.Users.Where(u => names.Contains(u.GoogleUserName)).Select(u => u.GoogleUserName).ToListAsync(ct)).ToHashSet();
            var me = await db.Users.Where(u => u.Id == userId).Select(u => u.GoogleUserName).SingleAsync(ct);
            return Results.Ok(found.Where(f => f.GoogleUserName != me)
                .Select(f => new MemberDto(f.GoogleUserName, f.DisplayName, f.AvatarUrl, f.Email, appUsers.Contains(f.GoogleUserName))));
        });

        api.MapGet("/spaces/{spaceId:guid}/members", async (Guid spaceId, HttpContext ctx, AppDbContext db, ChatQueries q,
            ChatApiClient chat, UserTokenService tokens, ChatSyncService sync, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();

            // Named spaces only know members who are app users or posted; load the full list once.
            var count = await db.SpaceMembers.CountAsync(m => m.SpaceId == spaceId, ct);
            if (count <= 1)
            {
                var space = await db.Spaces.SingleAsync(s => s.Id == spaceId, ct);
                await sync.RefreshSpaceAsync(userId, space.GoogleName, ct, includeMembers: true);
            }

            var members = await (from m in db.SpaceMembers
                                 where m.SpaceId == spaceId
                                 join p in db.People on m.GoogleUserName equals p.GoogleUserName into pj
                                 from p in pj.DefaultIfEmpty()
                                 select new MemberDto(m.GoogleUserName, p != null ? p.DisplayName : null, p != null ? p.AvatarUrl : null,
                                     p != null ? p.Email : null, m.AppUserId != null))
                .ToListAsync(ct);
            return Results.Ok(members.OrderBy(m => m.DisplayName ?? m.Email ?? m.Id).ToList());
        });

        api.MapPost("/spaces", async (CreateSpaceRequest body, HttpContext ctx, AppDbContext db, ChatApiClient chat,
            UserTokenService tokens, ChatSyncService sync, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            var members = body.Members.Where(m => m.StartsWith("users/", StringComparison.Ordinal)).Distinct().ToList();
            var token = await tokens.GetAccessTokenAsync(db, userId, ct);

            ChatSpace? created = body.Kind switch
            {
                "dm" when members.Count == 1 =>
                    await chat.FindDirectMessageAsync(token, members[0], ct)
                    ?? await chat.SetupSpaceAsync(token, "DIRECT_MESSAGE", null, members, ct),
                "group" when members.Count >= 2 => await chat.SetupSpaceAsync(token, "GROUP_CHAT", null, members, ct),
                "space" when !string.IsNullOrWhiteSpace(body.DisplayName) =>
                    await chat.SetupSpaceAsync(token, "SPACE", body.DisplayName.Trim(), members, ct),
                _ => null,
            };
            if (created is null) return Results.BadRequest("Neispravan zahtjev.");

            await sync.RefreshSpaceAsync(userId, created.Name, ct, includeMembers: true);
            var id = await db.Spaces.Where(s => s.GoogleName == created.Name).Select(s => s.Id).SingleAsync(ct);
            return Results.Ok(new { id });
        });

        api.MapPost("/spaces/{spaceId:guid}/members", async (Guid spaceId, AddMembersRequest body, HttpContext ctx, AppDbContext db,
            ChatQueries q, ChatApiClient chat, UserTokenService tokens, ChatSyncService sync, CancellationToken ct) =>
        {
            var userId = ctx.User.UserId();
            if (!await q.IsMemberAsync(userId, spaceId, ct)) return Results.NotFound();
            var space = await db.Spaces.SingleAsync(s => s.Id == spaceId, ct);
            var token = await tokens.GetAccessTokenAsync(db, userId, ct);
            var errors = new List<string>();
            foreach (var u in body.Members.Where(m => m.StartsWith("users/", StringComparison.Ordinal)).Distinct())
            {
                try { await chat.AddMemberAsync(token, space.GoogleName, u, ct); }
                catch (GoogleApiException ex) when (ex.Status == System.Net.HttpStatusCode.Conflict) { /* already a member */ }
                catch (GoogleApiException ex) { errors.Add($"{u}: {(int)ex.Status}"); }
            }
            await sync.RefreshSpaceAsync(userId, space.GoogleName, ct, includeMembers: true);
            return errors.Count == 0 ? Results.NoContent() : Results.Ok(new { errors });
        });
    }

    internal static async Task<UserSettings> GetOrCreateSettingsAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var s = await db.UserSettings.FirstOrDefaultAsync(x => x.AppUserId == userId, ct);
        if (s is not null) return s;
        s = new UserSettings { Id = Guid.NewGuid(), AppUserId = userId };
        db.UserSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    private static SettingsDto ToDto(UserSettings s, bool readReceiptsAvailable) => new(
        s.Keywords, s.TimeZone, s.DndScheduleEnabled, s.DndStart, s.DndEnd, s.SnoozeUntil, s.NotifyThreadReplies,
        s.PushOnlyWhenInactive, s.StatusEmoji, s.StatusText, s.StatusExpiresAt, s.ForceAway,
        s.ShareReadReceipts && readReceiptsAvailable, readReceiptsAvailable, s.HideTyping);

    /// <summary>Tells everyone in the tenant to refresh presence/status.</summary>
    private static Task ToTenantPresenceAsync(this IRealtimeNotifier rt, HttpContext ctx, CancellationToken ct) =>
        ctx.RequestServices.GetRequiredService<IHubContext<Realtime.ChatHub>>()
            .Clients.Group(Realtime.ChatHub.TenantGroup(ctx.User.TenantId())).SendAsync("presenceChanged", ct);
}
