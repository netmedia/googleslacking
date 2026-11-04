using GoogleSlacking.Api.Auth;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Api.Realtime;

/// <summary>
/// Server → client events: "messagesChanged"(spaceId, MessageDto[]), "spaceChanged"(spaceId), "spacesChanged"().
/// Groups: "user:{appUserId}" and "space:{spaceId}" (space ids are globally unique, so tenant-safe).
/// </summary>
[Authorize]
public sealed class ChatHub(AppDbContext db, ITenantContext tenant) : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";
    public static string SpaceGroup(Guid spaceId) => $"space:{spaceId}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Context.User!.UserId()));
        await JoinSpaces();
        await base.OnConnectedAsync();
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
}

public sealed class SignalRNotifier(IHubContext<ChatHub> hub) : IRealtimeNotifier
{
    public Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.SpaceGroup(spaceId)).SendAsync("messagesChanged", spaceId, messages, ct);

    public Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.SpaceGroup(spaceId)).SendAsync("spaceChanged", spaceId, ct);

    public Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct) =>
        hub.Clients.Group(ChatHub.UserGroup(appUserId)).SendAsync("spacesChanged", ct);
}
