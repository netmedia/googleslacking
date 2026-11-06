using GoogleSlacking.Core.Rules;
using GoogleSlacking.Core.WebPush;
using GoogleSlacking.Domain;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GoogleSlacking.Infrastructure.Notifications;

/// <summary>
/// Decides who gets notified about new messages (Slack rules, see <see cref="NotificationRules"/>),
/// records Activity, and delivers via SignalR (open clients) and Web Push (closed/background clients).
/// </summary>
public sealed class NotificationService(
    AppDbContext db,
    IRealtimeNotifier realtime,
    IPresence presence,
    WebPushSender push,
    TimeProvider clock,
    ILogger<NotificationService> log)
{
    public async Task OnMessagesCreatedAsync(IReadOnlyCollection<Message> created, CancellationToken ct)
    {
        foreach (var msg in created.Where(m => m.DeleteTime is null).OrderBy(m => m.CreateTime))
        {
            try
            {
                await HandleAsync(msg, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Notification handling failed for {Message}", msg.GoogleName);
            }
        }
    }

    private async Task HandleAsync(Message msg, CancellationToken ct)
    {
        // Old messages (backfill, late events) never notify.
        if (clock.GetUtcNow() - msg.CreateTime > TimeSpan.FromMinutes(15)) return;

        var space = await db.Spaces.SingleAsync(s => s.Id == msg.SpaceId, ct);
        var recipients = await db.SpaceMembers
            .Where(m => m.SpaceId == msg.SpaceId && m.AppUserId != null && m.GoogleUserName != msg.SenderGoogleUserName)
            .ToListAsync(ct);
        if (recipients.Count == 0) return;

        var userIds = recipients.Select(r => r.AppUserId!.Value).ToList();
        var settings = await db.UserSettings.Where(s => userIds.Contains(s.AppUserId)).ToDictionaryAsync(s => s.AppUserId, ct);

        HashSet<string> threadParticipants = [];
        if (msg.IsThreadReply && msg.ThreadName is not null)
            threadParticipants = (await db.Messages
                    .Where(m => m.ThreadName == msg.ThreadName && m.Id != msg.Id)
                    .Select(m => m.SenderGoogleUserName).Distinct().ToListAsync(ct))
                .ToHashSet();

        var sender = await db.People.FirstOrDefaultAsync(p => p.GoogleUserName == msg.SenderGoogleUserName, ct);
        var senderName = sender?.DisplayName ?? sender?.Email ?? "Netko";
        var spaceTitle = space.Kind == SpaceKind.Space ? "#" + (space.DisplayName ?? "prostor") : senderName;
        var now = clock.GetUtcNow();

        foreach (var r in recipients)
        {
            var uid = r.AppUserId!.Value;
            settings.TryGetValue(uid, out var s);

            var kind = NotificationRules.Decide(new NotificationInput(
                space.Kind,
                r.NotifyLevel,
                r.IsMuted,
                Mentions.Contains(msg.MentionedUserNames, r.GoogleUserName),
                Mentions.Contains(msg.MentionedUserNames, Mentions.All),
                NotificationRules.MatchesKeyword(msg.Text, NotificationRules.ParseKeywords(s?.Keywords)),
                msg.IsThreadReply,
                threadParticipants.Contains(r.GoogleUserName),
                s?.NotifyThreadReplies ?? true));
            if (kind is null) continue;

            // Activity feed: mentions, keywords, thread replies (not every DM / channel message).
            if (kind is ActivityKind.Mention or ActivityKind.Keyword or ActivityKind.ThreadReply)
            {
                db.Activity.Add(new ActivityItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = msg.TenantId,
                    AppUserId = uid,
                    Kind = kind.Value,
                    SpaceId = space.Id,
                    MessageId = msg.Id,
                    ActorGoogleUserName = msg.SenderGoogleUserName,
                    Preview = Truncate(msg.Text, 280),
                    CreatedAt = now,
                });
                await realtime.ToUserAsync(uid, "activityChanged", null, ct);
            }

            if (NotificationRules.IsDoNotDisturb(s, now)) continue;

            // Already looking at this conversation: no notification at all.
            if (presence.FocusedSpace(uid) == space.Id && (!msg.IsThreadReply || kind != ActivityKind.ThreadReply)) continue;

            var payload = new NotificationPayload(
                Title: kind switch
                {
                    ActivityKind.ThreadReply => $"{senderName} u niti · {spaceTitle}",
                    ActivityKind.Mention when space.Kind == SpaceKind.Space => $"{senderName} te spomenuo/la u {spaceTitle}",
                    _ when space.Kind == SpaceKind.Space => $"{senderName} u {spaceTitle}",
                    _ => senderName,
                },
                Body: Truncate(msg.Text, 180) ?? (msg.AttachmentsJson is not null ? "📎 Privitak" : ""),
                SpaceId: space.Id,
                ThreadName: msg.IsThreadReply ? msg.ThreadName : null,
                MessageName: msg.GoogleName,
                Kind: kind.Value,
                Icon: sender?.AvatarUrl,
                Tag: msg.GoogleName);

            // Open clients decide themselves whether to show it (hidden tab, other space...).
            await realtime.ToUserAsync(uid, "notification", payload, ct);

            if (s?.PushOnlyWhenInactive != false && presence.IsActive(uid)) continue;
            await SendPushAsync(uid, payload, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Activity entry for reactions on the user's own messages (no push, like Slack's default).</summary>
    public async Task OnReactionAddedAsync(MessageReaction reaction, CancellationToken ct)
    {
        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == reaction.MessageId, ct);
        if (msg is null || msg.SenderGoogleUserName == reaction.UserGoogleName) return;
        var author = await db.Users.FirstOrDefaultAsync(u => u.GoogleUserName == msg.SenderGoogleUserName, ct);
        if (author is null) return;

        db.Activity.Add(new ActivityItem
        {
            Id = Guid.NewGuid(),
            TenantId = msg.TenantId,
            AppUserId = author.Id,
            Kind = ActivityKind.Reaction,
            SpaceId = msg.SpaceId,
            MessageId = msg.Id,
            ActorGoogleUserName = reaction.UserGoogleName,
            Preview = reaction.Emoji,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        await realtime.ToUserAsync(author.Id, "activityChanged", null, ct);
    }

    /// <summary>Reminder for a saved item ("Remind me"): Activity + SignalR + push (ignores DND like Slack reminders).</summary>
    public async Task SendReminderAsync(SavedItem item, Message msg, CancellationToken ct)
    {
        var space = await db.Spaces.SingleAsync(s => s.Id == msg.SpaceId, ct);
        db.Activity.Add(new ActivityItem
        {
            Id = Guid.NewGuid(),
            TenantId = item.TenantId,
            AppUserId = item.AppUserId,
            Kind = ActivityKind.Reminder,
            SpaceId = msg.SpaceId,
            MessageId = msg.Id,
            Preview = Truncate(msg.Text, 280),
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        var payload = new NotificationPayload(
            "⏰ Podsjetnik", Truncate(msg.Text, 180) ?? "", space.Id, msg.IsThreadReply ? msg.ThreadName : null,
            msg.GoogleName, ActivityKind.Reminder, null, "reminder:" + item.Id);
        await realtime.ToUserAsync(item.AppUserId, "notification", payload, ct);
        await realtime.ToUserAsync(item.AppUserId, "activityChanged", null, ct);
        await realtime.ToUserAsync(item.AppUserId, "savedChanged", null, ct);
        if (!presence.IsActive(item.AppUserId)) await SendPushAsync(item.AppUserId, payload, ct);
    }

    public async Task<int> SendPushAsync(Guid userId, NotificationPayload payload, CancellationToken ct)
    {
        if (!push.IsConfigured) return 0;
        var subs = await db.WebPushSubscriptions.Where(s => s.AppUserId == userId).ToListAsync(ct);
        var delivered = 0;
        foreach (var sub in subs)
        {
            try
            {
                var result = await push.SendAsync(new WebPushTarget(sub.Endpoint, sub.P256dh, sub.Auth), payload,
                    TimeSpan.FromHours(12), topic: null, ct);
                switch (result)
                {
                    case WebPushResult.Delivered:
                        sub.LastSuccessAt = clock.GetUtcNow();
                        sub.FailureCount = 0;
                        delivered++;
                        break;
                    case WebPushResult.Gone:
                        db.WebPushSubscriptions.Remove(sub);
                        break;
                    default:
                        if (++sub.FailureCount >= 10) db.WebPushSubscriptions.Remove(sub);
                        break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log.LogWarning(ex, "Web push failed for {Endpoint}", sub.Endpoint);
                sub.FailureCount++;
            }
        }
        await db.SaveChangesAsync(ct);
        return delivered;
    }

    private static string? Truncate(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n] + "…";
}
