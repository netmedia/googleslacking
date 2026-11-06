using System.Text.Json;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoogleSlacking.Infrastructure.Sync;

/// <summary>Applies one Workspace event to the cache. Runs in its own DI scope per event.</summary>
public sealed class EventProcessor(
    AppDbContext db,
    ITenantContext tenant,
    ChatSyncService sync,
    ChatApiClient chat,
    UserTokenService tokens,
    IRealtimeNotifier notifier,
    ReadReceipts.ReadReceiptService receipts,
    ILogger<EventProcessor> log)
{
    public async Task HandleAsync(WorkspaceEvent e, CancellationToken ct)
    {
        // Cross-tenant lookup on purpose: the subscription tells us which tenant this event belongs to.
        var sub = await db.EventSubscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.GoogleName == e.SubscriptionName, ct);
        if (sub is null)
        {
            log.LogWarning("Event {Type} for unknown subscription {Sub} — ignoring", e.Type, e.SubscriptionName);
            return;
        }
        tenant.Set(sub.TenantId);

        switch (e.Type)
        {
            case ChatEventTypes.MessageCreated or ChatEventTypes.MessageUpdated
                or ChatEventTypes.MessageBatchCreated or ChatEventTypes.MessageBatchUpdated:
            {
                var msgs = WorkspaceEventParser.ExtractMessages(e).ToList();
                // Payload may omit the resource (too large / includeResource=false): fetch it.
                for (var i = 0; i < msgs.Count; i++)
                    if (msgs[i].CreateTime is null && msgs[i].Name.Length > 0)
                        msgs[i] = await FetchMessageAsync(sub.AppUserId, msgs[i].Name, ct) ?? msgs[i];
                await sync.UpsertMessagesAsync(msgs, notify: true, ct);
                break;
            }

            case ChatEventTypes.MessageDeleted or ChatEventTypes.MessageBatchDeleted:
                await sync.MarkDeletedAsync(WorkspaceEventParser.ExtractMessages(e).Select(m => m.Name).ToList(), ct);
                break;

            case ChatEventTypes.ReactionCreated or ChatEventTypes.ReactionDeleted:
            {
                var reaction = WorkspaceEventParser.ExtractReaction(e);
                if (reaction is null) break;
                var local = await sync.ApplyReactionAsync(reaction, removed: e.Type == ChatEventTypes.ReactionDeleted, ct);

                // Refresh the summary counts from Google, then broadcast (even if the summary didn't change,
                // the list of who reacted did).
                if (await FetchMessageAsync(sub.AppUserId, reaction.MessageName, ct) is { } fresh)
                {
                    var changed = await sync.UpsertMessagesAsync([fresh], notify: false, ct);
                    var target = changed.FirstOrDefault() ?? local;
                    if (target is not null) await sync.NotifyMessagesAsync([target], ct);
                }
                break;
            }

            case ChatEventTypes.SpaceReadStateUpdated:
            {
                if (WorkspaceEventParser.ExtractSpaceReadState(e) is not { } rs || rs.LastReadTime is not { } last) break;
                var member = await db.SpaceMembers.Include(m => m.Space)
                    .FirstOrDefaultAsync(m => m.AppUserId == sub.AppUserId && m.Space!.GoogleName == rs.SpaceName, ct);
                if (member is null || member.LastReadTime >= last) break;
                member.LastReadTime = last;
                await db.SaveChangesAsync(ct);
                await notifier.SpacesListChangedAsync(sub.TenantId, sub.AppUserId, ct);
                await receipts.OnReadAsync(sub.AppUserId, [member.SpaceId], ct); // read in an official client
                break;
            }

            case ChatEventTypes.MembershipCreated or ChatEventTypes.MembershipUpdated or ChatEventTypes.MembershipDeleted
                or ChatEventTypes.SpaceUpdated or ChatEventTypes.SpaceDeleted:
                if ((WorkspaceEventParser.SubjectSpaceName(e) ?? SpaceFromData(e)) is { } spaceName)
                    await sync.RefreshSpaceAsync(sub.AppUserId, spaceName, ct);
                break;

            case ChatEventTypes.SubscriptionExpirationReminder:
            case ChatEventTypes.SubscriptionExpired:
            case ChatEventTypes.SubscriptionSuspended:
                sub.State = e.Type switch
                {
                    ChatEventTypes.SubscriptionExpired => SubscriptionState.Expired,
                    ChatEventTypes.SubscriptionSuspended => SubscriptionState.Suspended,
                    _ => sub.State,
                };
                await db.SaveChangesAsync(ct);
                var user = await db.Users.SingleAsync(u => u.Id == sub.AppUserId, ct);
                await sync.EnsureSubscriptionsAsync(user, await tokens.GetAccessTokenAsync(db, user.Id, ct), ct);
                break;

            default:
                log.LogDebug("Unhandled event type {Type}", e.Type);
                break;
        }
    }

    private async Task<ChatMessage?> FetchMessageAsync(Guid userId, string name, CancellationToken ct)
    {
        try
        {
            return await chat.GetMessageAsync(await tokens.GetAccessTokenAsync(db, userId, ct), name, ct);
        }
        catch (GoogleApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static string? SpaceFromData(WorkspaceEvent e)
    {
        if (e.Data is not { ValueKind: JsonValueKind.Object } d) return null;
        foreach (var key in new[] { "space", "membership" })
            if (d.TryGetProperty(key, out var o) && o.TryGetProperty("name", out var n) && n.GetString() is { } s)
                return ChatMessage.SpaceNameOf(s);
        return null;
    }
}
