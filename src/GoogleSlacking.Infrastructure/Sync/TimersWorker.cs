using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoogleSlacking.Infrastructure.Sync;

/// <summary>Sends scheduled messages and fires "remind me" reminders. Things Google Chat doesn't have.</summary>
public sealed class TimersWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TimersWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await SendScheduledAsync(ct);
                await FireRemindersAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Timers tick failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    internal async Task SendScheduledAsync(CancellationToken ct)
    {
        List<(Guid Id, Guid TenantId)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = clock.GetUtcNow();
            // Cross-tenant on purpose: the worker serves all tenants; each item is then handled in its own tenant scope.
            due = (await db.ScheduledMessages.IgnoreQueryFilters()
                    .Where(m => m.Status == ScheduledMessageStatus.Pending && m.SendAt <= now)
                    .OrderBy(m => m.SendAt).Take(50)
                    .Select(m => new { m.Id, m.TenantId }).ToListAsync(ct))
                .Select(x => (x.Id, x.TenantId)).ToList();
        }

        foreach (var (id, tenantId) in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().Set(tenantId);
            var db = sp.GetRequiredService<AppDbContext>();
            var item = await db.ScheduledMessages.SingleAsync(m => m.Id == id, ct);
            if (item.Status != ScheduledMessageStatus.Pending) continue;

            try
            {
                var space = await db.Spaces.SingleAsync(s => s.Id == item.SpaceId, ct);
                var token = await sp.GetRequiredService<UserTokenService>().GetAccessTokenAsync(db, item.AppUserId, ct);
                var sent = await sp.GetRequiredService<ChatApiClient>().CreateMessageAsync(
                    token, space.GoogleName, item.Text, item.ThreadName, "client-" + item.Id.ToString("N"), ct: ct);
                item.Status = ScheduledMessageStatus.Sent;
                item.SentMessageName = sent?.Name;
                await db.SaveChangesAsync(ct);
                if (sent is not null) await sp.GetRequiredService<ChatSyncService>().UpsertMessagesAsync([sent], notify: true, ct);
            }
            catch (Exception ex) when (ex is GoogleApiException or ReauthenticationRequiredException)
            {
                log.LogWarning(ex, "Scheduled message {Id} failed", id);
                item.Status = ScheduledMessageStatus.Failed;
                item.Error = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
                await db.SaveChangesAsync(ct);
            }
            await sp.GetRequiredService<IRealtimeNotifier>().ToUserAsync(item.AppUserId, "scheduledChanged", null, ct);
        }
    }

    internal async Task FireRemindersAsync(CancellationToken ct)
    {
        List<(Guid Id, Guid TenantId)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = clock.GetUtcNow();
            // Cross-tenant on purpose (see above).
            due = (await db.SavedItems.IgnoreQueryFilters()
                    .Where(s => s.RemindAt != null && s.RemindAt <= now && s.RemindedAt == null && s.CompletedAt == null)
                    .Take(50)
                    .Select(s => new { s.Id, s.TenantId }).ToListAsync(ct))
                .Select(x => (x.Id, x.TenantId)).ToList();
        }

        foreach (var (id, tenantId) in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContext>().Set(tenantId);
            var db = sp.GetRequiredService<AppDbContext>();
            var item = await db.SavedItems.SingleAsync(s => s.Id == id, ct);
            item.RemindedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);

            var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == item.MessageId, ct);
            if (msg is not null)
                await sp.GetRequiredService<NotificationService>().SendReminderAsync(item, msg, ct);
        }
    }
}
