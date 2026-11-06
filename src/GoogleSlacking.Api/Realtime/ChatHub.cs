using System.Collections.Concurrent;
using GoogleSlacking.Api.Auth;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Api.Realtime;

/// <summary>
/// Server → client events:
///   "messagesChanged"(spaceId, MessageDto[]), "spaceChanged"(spaceId), "spacesChanged"(),
///   "notification"(NotificationPayload), "activityChanged", "savedChanged", "scheduledChanged",
///   "draftsChanged", "settingsChanged", "sectionsChanged", "pinsChanged"(spaceId), "bookmarksChanged"(spaceId),
///   "presenceChanged"(), "typing"(spaceId, threadName, userName), "readReceiptsChanged"(spaceId).
/// Client → server: JoinSpaces(), Heartbeat(focused, activeSpaceId), Typing(spaceId, threadName).
/// Groups: "user:{appUserId}", "space:{spaceId}", "tenant:{tenantId}".
/// </summary>
[Authorize]
public sealed class ChatHub(AppDbContext db, ITenantContext tenant, PresenceTracker presence) : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string SpaceGroup(Guid spaceId) => $"space:{spaceId}";
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.UserId()));
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(user.TenantId()));
        await JoinSpaces();
        if (presence.Connect(Context.ConnectionId, user.TenantId(), user.UserId()))
            await Clients.Group(TenantGroup(user.TenantId())).SendAsync("presenceChanged");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (presence.Disconnect(Context.ConnectionId) is { } tenantId)
            await Clients.Group(TenantGroup(tenantId)).SendAsync("presenceChanged");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Client calls this after "spacesChanged" so it starts receiving messages for new spaces.</summary>
    public async Task JoinSpaces()
    {
        // Hub invocations get their own DI scope, so set the tenant here (not via middleware).
        if (tenant.TenantId is null) tenant.Set(Context.User!.TenantId());
        var userId = Context.User!.UserId();
        var spaceIds = await db.SpaceMembers.Where(m => m.AppUserId == userId).Select(m => m.SpaceId).ToListAsync();
        foreach (var id in spaceIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, SpaceGroup(id));
    }

    /// <summary>
    /// "… is typing". Clients call this at most every ~3 s while the user types; nothing is stored.
    /// Only app users see it (Google Chat has no typing API).
    /// </summary>
    public async Task Typing(Guid spaceId, string? threadName)
    {
        if (tenant.TenantId is null) tenant.Set(Context.User!.TenantId());
        var userId = Context.User!.UserId();

        // One query: must be a member, and must not have "hide typing" on.
        var userName = await (from m in db.SpaceMembers
                              where m.SpaceId == spaceId && m.AppUserId == userId
                              join s in db.UserSettings on m.AppUserId equals s.AppUserId into sj
                              from s in sj.DefaultIfEmpty()
                              where s == null || !s.HideTyping
                              select m.GoogleUserName)
            .FirstOrDefaultAsync();
        if (userName is null) return;

        await Clients.OthersInGroup(SpaceGroup(spaceId)).SendAsync("typing", spaceId, threadName, userName);
    }

    /// <summary>Sent by clients every ~30 s and on focus/blur/space change.</summary>
    public Task Heartbeat(bool focused, Guid? activeSpaceId)
    {
        presence.Touch(Context.ConnectionId, focused, activeSpaceId);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory connection tracker (single instance; move to Redis/Azure SignalR for scale-out).</summary>
public sealed class PresenceTracker(TimeProvider clock) : IPresence
{
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(10);

    private sealed class Conn
    {
        public required Guid TenantId { get; init; }
        public required Guid UserId { get; init; }
        public bool Focused { get; set; }
        public Guid? ActiveSpaceId { get; set; }
        public DateTimeOffset LastSeen { get; set; }
        public DateTimeOffset LastFocused { get; set; }
    }

    private readonly ConcurrentDictionary<string, Conn> _conns = new();

    /// <returns>True if this is the user's first connection (came online).</returns>
    public bool Connect(string connectionId, Guid tenantId, Guid userId)
    {
        var first = !_conns.Values.Any(c => c.UserId == userId);
        var now = clock.GetUtcNow();
        _conns[connectionId] = new Conn { TenantId = tenantId, UserId = userId, LastSeen = now, LastFocused = now, Focused = true };
        return first;
    }

    /// <returns>Tenant id if the user went offline.</returns>
    public Guid? Disconnect(string connectionId)
    {
        if (!_conns.TryRemove(connectionId, out var c)) return null;
        return _conns.Values.Any(x => x.UserId == c.UserId) ? null : c.TenantId;
    }

    public void Touch(string connectionId, bool focused, Guid? activeSpaceId)
    {
        if (!_conns.TryGetValue(connectionId, out var c)) return;
        var now = clock.GetUtcNow();
        c.LastSeen = now;
        c.Focused = focused;
        c.ActiveSpaceId = activeSpaceId;
        if (focused) c.LastFocused = now;
    }

    public bool IsActive(Guid appUserId)
    {
        var now = clock.GetUtcNow();
        return _conns.Values.Any(c => c.UserId == appUserId && c.Focused && now - c.LastSeen < ActiveWindow);
    }

    public Guid? FocusedSpace(Guid appUserId)
    {
        var now = clock.GetUtcNow();
        return _conns.Values
            .Where(c => c.UserId == appUserId && c.Focused && now - c.LastSeen < ActiveWindow)
            .OrderByDescending(c => c.LastSeen)
            .Select(c => c.ActiveSpaceId)
            .FirstOrDefault();
    }

    public IReadOnlyDictionary<Guid, string> Snapshot(Guid tenantId)
    {
        var now = clock.GetUtcNow();
        return _conns.Values.Where(c => c.TenantId == tenantId)
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.Any(c => now - c.LastFocused < AwayAfter) ? "active" : "away");
    }
}

public sealed class SignalRNotifier(IHubContext<ChatHub> hub) : IRealtimeNotifier
{
    public Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.SpaceGroup(spaceId)).SendAsync("messagesChanged", spaceId, messages, ct);

    public Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.SpaceGroup(spaceId)).SendAsync("spaceChanged", spaceId, ct);

    public Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.UserGroup(appUserId)).SendAsync("spacesChanged", ct);

    public Task ToUserAsync(Guid appUserId, string eventName, object? payload, CancellationToken ct) =>
        payload is null
            ? hub.Clients.Group(ChatHub.UserGroup(appUserId)).SendAsync(eventName, ct)
            : hub.Clients.Group(ChatHub.UserGroup(appUserId)).SendAsync(eventName, payload, ct);

    public Task ToSpaceAsync(Guid spaceId, string eventName, object? payload, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.SpaceGroup(spaceId)).SendAsync(eventName, spaceId, payload, ct);
}
