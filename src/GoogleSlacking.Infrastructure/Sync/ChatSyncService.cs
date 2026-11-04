using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Infrastructure.Sync;

/// <summary>
/// Write side: pulls data from Google into the local cache and keeps it consistent.
/// Assumes the tenant is already set on the scope.
/// </summary>
public sealed class ChatSyncService(
    AppDbContext db,
    ChatApiClient chat,
    PeopleApiClient people,
    WorkspaceEventsClient events,
    UserTokenService tokens,
    IRealtimeNotifier notifier,
    IOptions<GoogleOptions> options,
    TimeProvider clock,
    ILogger<ChatSyncService> log)
{
    /// <summary>Spaces (by recent activity) that get eager backfill + read state on initial sync. The rest load lazily.</summary>
    private const int EagerSpaces = 50;

    public async Task InitialSyncAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var token = await tokens.GetAccessTokenAsync(db, userId, ct);
        log.LogInformation("Initial sync for {Email}", user.Email);

        // 1. Spaces + own membership
        var remote = new List<ChatSpace>();
        await foreach (var s in chat.ListSpacesAsync(token, ct))
            if (s.SingleUserBotDm != true) remote.Add(s);

        var spaces = await UpsertSpacesAsync(remote, ct);
        foreach (var space in spaces.Values)
            await EnsureMemberAsync(space, user.GoogleUserName, user.Id, ct);
        await db.SaveChangesAsync(ct);

        // 2. Eager part: most recently active spaces
        var eager = spaces.Values
            .OrderByDescending(s => s.LastActiveTime ?? DateTimeOffset.MinValue)
            .Take(EagerSpaces)
            .ToList();

        foreach (var space in eager)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (space.Kind != SpaceKind.Space)
                    await SyncMembersAsync(token, space, ct);

                var page = await chat.ListMessagesAsync(token, space.GoogleName, options.Value.InitialBackfillPerSpace, ct: ct);
                var msgs = page?.Messages ?? [];
                if (page?.NextPageToken is null) space.HistoryComplete = true;
                await UpsertMessagesAsync(msgs, notify: false, ct);

                var rs = await chat.GetReadStateAsync(token, space.GoogleName, ct);
                var me = await db.SpaceMembers.SingleAsync(m => m.SpaceId == space.Id && m.AppUserId == userId, ct);
                me.LastReadTime = rs?.LastReadTime;
                await db.SaveChangesAsync(ct);
            }
            catch (GoogleApiException ex)
            {
                log.LogWarning(ex, "Backfill failed for {Space}", space.GoogleName);
            }
        }

        // 3. Names/avatars for everyone we've seen
        await ResolvePeopleAsync(token, ct);

        // 4. Real-time subscription
        await EnsureSubscriptionAsync(user, token, ct);

        user.InitialSyncCompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await notifier.SpacesListChangedAsync(user.TenantId, user.Id, ct);
    }

    /// <summary>Loads older messages from Google when the cache runs out ("infinite scroll up").</summary>
    public async Task<int> BackfillOlderAsync(Guid userId, Space space, DateTimeOffset before, int pageSize, CancellationToken ct)
    {
        if (space.HistoryComplete) return 0;
        var token = await tokens.GetAccessTokenAsync(db, userId, ct);
        var page = await chat.ListMessagesAsync(token, space.GoogleName, pageSize, createdBefore: before, ct: ct);
        var msgs = page?.Messages ?? [];
        if (page?.NextPageToken is null || msgs.Count < pageSize) space.HistoryComplete = true;
        await UpsertMessagesAsync(msgs, notify: false, ct);
        await db.SaveChangesAsync(ct);
        await ResolvePeopleAsync(token, ct);
        return msgs.Count;
    }

    public async Task<Dictionary<string, Space>> UpsertSpacesAsync(IReadOnlyCollection<ChatSpace> remote, CancellationToken ct)
    {
        var names = remote.Select(s => s.Name).ToList();
        var existing = await db.Spaces.Where(s => names.Contains(s.GoogleName)).ToDictionaryAsync(s => s.GoogleName, ct);

        foreach (var r in remote)
        {
            if (!existing.TryGetValue(r.Name, out var s))
            {
                s = new Space { Id = Guid.NewGuid(), GoogleName = r.Name };
                db.Spaces.Add(s);
                existing[r.Name] = s;
            }
            s.DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? s.DisplayName : r.DisplayName;
            s.Kind = r.SpaceType switch
            {
                "DIRECT_MESSAGE" => SpaceKind.DirectMessage,
                "GROUP_CHAT" => SpaceKind.GroupChat,
                _ => SpaceKind.Space,
            };
            s.IsThreaded = r.SpaceThreadingState == "THREADED_MESSAGES";
            if (r.LastActiveTime is { } t && (s.LastActiveTime is null || t > s.LastActiveTime)) s.LastActiveTime = t;
            s.DeletedAt = null;
        }
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<SpaceMember> EnsureMemberAsync(Space space, string googleUserName, Guid? appUserId, CancellationToken ct)
    {
        var m = db.SpaceMembers.Local.FirstOrDefault(x => x.SpaceId == space.Id && x.GoogleUserName == googleUserName)
                ?? await db.SpaceMembers.FirstOrDefaultAsync(x => x.SpaceId == space.Id && x.GoogleUserName == googleUserName, ct);
        if (m is null)
        {
            m = new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = googleUserName };
            db.SpaceMembers.Add(m);
        }
        if (appUserId is not null) m.AppUserId = appUserId;
        return m;
    }

    private async Task SyncMembersAsync(string token, Space space, CancellationToken ct)
    {
        var appUsers = await db.Users.ToDictionaryAsync(u => u.GoogleUserName, u => u.Id, ct);
        await foreach (var m in chat.ListMembersAsync(token, space.GoogleName, ct))
        {
            if (m.Member is not { Type: "HUMAN", Name: { Length: > 0 } name } || m.State != "JOINED") continue;
            await EnsureMemberAsync(space, name, appUsers.TryGetValue(name, out var id) ? id : null, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Idempotent upsert. The same event arrives once per subscribed member, so we only count/notify
    /// messages that actually changed (newer LastUpdateTime or new delete).
    /// </summary>
    public async Task<IReadOnlyList<Message>> UpsertMessagesAsync(IReadOnlyCollection<ChatMessage> incoming, bool notify, CancellationToken ct)
    {
        var complete = incoming.Where(m => m.CreateTime is not null && !string.IsNullOrEmpty(m.Name)).ToList();
        if (complete.Count == 0) return [];

        var spaceNames = complete.Select(m => m.SpaceName).Distinct().ToList();
        var spaces = await db.Spaces.Where(s => spaceNames.Contains(s.GoogleName)).ToDictionaryAsync(s => s.GoogleName, ct);
        var names = complete.Select(m => m.Name).ToList();
        var existing = await db.Messages.Where(m => names.Contains(m.GoogleName)).ToDictionaryAsync(m => m.GoogleName, ct);

        var changed = new List<Message>();
        foreach (var r in complete)
        {
            if (!spaces.TryGetValue(r.SpaceName, out var space))
            {
                log.LogDebug("Skipping message for unknown space {Space}", r.SpaceName);
                continue;
            }

            if (existing.TryGetValue(r.Name, out var m))
            {
                var newer = (r.LastUpdateTime ?? r.CreateTime) > (m.LastUpdateTime ?? m.CreateTime);
                var deleted = r.DeleteTime is not null && m.DeleteTime is null;
                var reactionsChanged = JsonHelpers.ToJson(r.EmojiReactionSummaries) != m.ReactionsJson;
                if (!newer && !deleted && !reactionsChanged) continue;
            }
            else
            {
                m = new Message
                {
                    Id = Guid.NewGuid(),
                    SpaceId = space.Id,
                    GoogleName = r.Name,
                    SenderGoogleUserName = r.Sender?.Name ?? "users/unknown",
                };
                db.Messages.Add(m);
                existing[r.Name] = m;
            }

            m.ThreadName = r.Thread?.Name;
            m.IsThreadReply = r.ThreadReply ?? false;
            m.Text = r.Text;
            m.FormattedText = r.FormattedText;
            m.ReactionsJson = JsonHelpers.ToJson(r.EmojiReactionSummaries);
            m.AttachmentsJson = JsonHelpers.ToJson(r.Attachment);
            m.CreateTime = r.CreateTime!.Value;
            m.LastUpdateTime = r.LastUpdateTime;
            m.DeleteTime = r.DeleteTime;
            m.ClientAssignedMessageId = r.ClientAssignedMessageId;

            if (space.LastActiveTime is null || m.CreateTime > space.LastActiveTime) space.LastActiveTime = m.CreateTime;
            changed.Add(m);
        }

        await db.SaveChangesAsync(ct);
        if (notify) await NotifyAsync(changed, ct);
        return changed;
    }

    public async Task MarkDeletedAsync(IReadOnlyCollection<string> messageNames, CancellationToken ct)
    {
        var msgs = await db.Messages.Where(m => messageNames.Contains(m.GoogleName) && m.DeleteTime == null).ToListAsync(ct);
        foreach (var m in msgs) m.DeleteTime = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyAsync(msgs, ct);
    }

    /// <summary>Re-fetches one space after a space/membership event.</summary>
    public async Task RefreshSpaceAsync(Guid userId, string spaceName, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var token = await tokens.GetAccessTokenAsync(db, userId, ct);
        try
        {
            var remote = await chat.GetSpaceAsync(token, spaceName, ct);
            if (remote is null) return;
            var spaces = await UpsertSpacesAsync([remote], ct);
            var space = spaces[spaceName];
            await EnsureMemberAsync(space, user.GoogleUserName, user.Id, ct);
            if (space.Kind != SpaceKind.Space) await SyncMembersAsync(token, space, ct);
            await db.SaveChangesAsync(ct);
            await ResolvePeopleAsync(token, ct);
        }
        catch (GoogleApiException ex) when (ex.Status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
        {
            // We lost access: drop our membership.
            var member = await db.SpaceMembers.Include(m => m.Space)
                .FirstOrDefaultAsync(m => m.AppUserId == userId && m.Space!.GoogleName == spaceName, ct);
            if (member is not null) db.SpaceMembers.Remove(member);
            await db.SaveChangesAsync(ct);
        }
        await notifier.SpacesListChangedAsync(user.TenantId, user.Id, ct);
    }

    /// <summary>Resolves display names/avatars for unknown senders and members (People API, 50 per call).</summary>
    public async Task ResolvePeopleAsync(string token, CancellationToken ct)
    {
        var known = db.People.Select(p => p.GoogleUserName);
        var unknown = await db.Messages.Select(m => m.SenderGoogleUserName)
            .Union(db.SpaceMembers.Select(m => m.GoogleUserName))
            .Where(n => n.StartsWith("users/") && n != "users/unknown" && !known.Contains(n))
            .Distinct()
            .Take(500)
            .ToListAsync(ct);

        foreach (var chunk in unknown.Chunk(50))
        {
            IReadOnlyList<PersonInfo> found;
            try
            {
                found = await people.GetBatchAsync(token, chunk, ct);
            }
            catch (GoogleApiException ex)
            {
                log.LogWarning(ex, "People lookup failed");
                found = [];
            }

            var byName = found.ToDictionary(p => p.GoogleUserName);
            foreach (var name in chunk)
            {
                byName.TryGetValue(name, out var p);
                db.People.Add(new Person
                {
                    Id = Guid.NewGuid(),
                    GoogleUserName = name,
                    DisplayName = p?.DisplayName,
                    Email = p?.Email,
                    AvatarUrl = p?.AvatarUrl,
                    ResolvedAt = clock.GetUtcNow(),
                });
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Makes sure the user has an active "all my spaces" subscription.</summary>
    public async Task EnsureSubscriptionAsync(AppUser user, string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sub = await db.EventSubscriptions
            .Where(s => s.AppUserId == user.Id && s.TargetResource == ChatEventTypes.AllSpacesTarget && s.State != SubscriptionState.Deleted)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (sub is { State: SubscriptionState.Active } && sub.ExpireTime > now.AddMinutes(30)) return;

        WorkspaceSubscription? remote = null;
        try
        {
            if (sub is { State: SubscriptionState.Suspended })
            {
                await events.ReactivateAsync(token, sub.GoogleName, ct);
                remote = await events.GetSubscriptionAsync(token, sub.GoogleName, ct);
            }
            else if (sub is { State: SubscriptionState.Active })
            {
                await events.RenewAsync(token, sub.GoogleName, ct);
                remote = await events.GetSubscriptionAsync(token, sub.GoogleName, ct);
            }
        }
        catch (GoogleApiException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
        {
            remote = null; // gone on Google's side -> recreate below
        }

        if (remote is null)
        {
            try
            {
                remote = await events.CreateSubscriptionAsync(token, ChatEventTypes.AllSpacesTarget,
                    ChatEventTypes.AllSpacesSubscription, options.Value.TopicResourceName, includeResource: true, ct);
            }
            catch (GoogleApiException ex) when (ex.Status == System.Net.HttpStatusCode.Conflict)
            {
                // Already exists (e.g. DB was reset): adopt it.
                var list = await events.ListSubscriptionsAsync(token, ChatEventTypes.AllSpacesTarget, ChatEventTypes.MessageCreated, ct);
                remote = list.FirstOrDefault();
                if (remote?.Name is { } n) await events.RenewAsync(token, n, ct);
            }
        }

        if (remote?.Name is null)
        {
            log.LogError("Could not obtain an events subscription for {Email}", user.Email);
            return;
        }

        if (sub is null || sub.GoogleName != remote.Name)
        {
            if (sub is not null) sub.State = SubscriptionState.Deleted;
            sub = await db.EventSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.GoogleName == remote.Name, ct);
            if (sub is null)
            {
                sub = new EventSubscription
                {
                    Id = Guid.NewGuid(),
                    TenantId = user.TenantId,
                    AppUserId = user.Id,
                    GoogleName = remote.Name,
                    TargetResource = ChatEventTypes.AllSpacesTarget,
                };
                db.EventSubscriptions.Add(sub);
            }
        }

        sub.State = remote.State switch
        {
            "SUSPENDED" => SubscriptionState.Suspended,
            "DELETED" => SubscriptionState.Deleted,
            _ => SubscriptionState.Active,
        };
        sub.ExpireTime = remote.ExpireTime;
        sub.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Subscription {Name} for {Email} active until {Expire}", sub.GoogleName, user.Email, sub.ExpireTime);
    }

    private async Task NotifyAsync(IReadOnlyCollection<Message> changed, CancellationToken ct)
    {
        if (changed.Count == 0) return;
        var tenantId = changed.First().TenantId;
        var dtos = await ChatQueries.ToDtosAsync(db, changed, ct);
        foreach (var g in dtos.GroupBy(d => d.SpaceId))
            await notifier.MessagesChangedAsync(tenantId, g.Key, g.ToList(), ct);
    }
}
