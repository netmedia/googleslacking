using System.Threading.Channels;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Infrastructure.Sync;

public sealed class SyncOptions
{
    public const string Section = "Sync";

    /// <summary>Turn off locally if you don't have Pub/Sub set up yet (no real-time, everything else works).</summary>
    public bool EnablePubSub { get; set; } = true;
    public bool EnableSubscriptionMaintenance { get; set; } = true;
}

/// <summary>Queue of users needing initial sync, so sign-in returns immediately.</summary>
public sealed class InitialSyncQueue
{
    private readonly Channel<(Guid TenantId, Guid UserId)> _channel = Channel.CreateUnbounded<(Guid, Guid)>();
    public void Enqueue(Guid tenantId, Guid userId) => _channel.Writer.TryWrite((tenantId, userId));
    public IAsyncEnumerable<(Guid TenantId, Guid UserId)> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public sealed class InitialSyncWorker(InitialSyncQueue queue, IServiceScopeFactory scopes, ILogger<InitialSyncWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var (tenantId, userId) in queue.ReadAllAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                await scope.ServiceProvider.GetRequiredService<ChatSyncService>().InitialSyncAsync(userId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Initial sync failed for user {UserId}", userId);
            }
        }
    }
}

/// <summary>Pulls Workspace events from Pub/Sub and applies them. One instance is enough for an MVP.</summary>
public sealed class PubSubPullWorker(
    PubSubClient pubsub,
    IServiceScopeFactory scopes,
    IOptions<SyncOptions> options,
    ILogger<PubSubPullWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.EnablePubSub)
        {
            log.LogWarning("Pub/Sub disabled (Sync:EnablePubSub=false) — no real-time updates.");
            return;
        }

        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var batch = await pubsub.PullAsync(100, ct);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                    continue;
                }

                var acks = new List<string>(batch.Count);
                foreach (var received in batch.OrderBy(r => r.Message.PublishTime))
                {
                    try
                    {
                        var evt = WorkspaceEventParser.Parse(received.Message);
                        if (evt is not null)
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            await scope.ServiceProvider.GetRequiredService<EventProcessor>().HandleAsync(evt, ct);
                        }
                        acks.Add(received.AckId);
                    }
                    catch (ReauthenticationRequiredException ex)
                    {
                        log.LogWarning("{Message} — dropping event", ex.Message);
                        acks.Add(received.AckId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Not acked -> Pub/Sub redelivers. Configure a dead-letter topic to stop poison messages.
                        log.LogError(ex, "Failed to process Pub/Sub message {Id}", received.Message.MessageId);
                    }
                }

                await pubsub.AcknowledgeAsync(acks, ct);
                backoff = TimeSpan.FromSeconds(1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Pub/Sub pull failed; retrying in {Delay}", backoff);
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
            }
        }
    }
}

/// <summary>
/// Renews subscriptions before they expire (max ~4h when the payload includes resource data)
/// and recreates expired/suspended ones.
/// </summary>
public sealed class SubscriptionMaintenanceWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<SyncOptions> options,
    ILogger<SubscriptionMaintenanceWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RenewWindow = TimeSpan.FromMinutes(45);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.EnableSubscriptionMaintenance) return;

        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await RunOnceAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Subscription maintenance failed");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        List<(Guid TenantId, Guid UserId)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var limit = clock.GetUtcNow().Add(RenewWindow);

            // Users with a refresh token whose subscription is missing, expiring, expired or suspended.
            var rows = await db.Users.IgnoreQueryFilters()
                .Where(u => u.ProtectedRefreshToken != null && u.InitialSyncCompletedAt != null)
                .Where(u => !db.EventSubscriptions.IgnoreQueryFilters().Any(s =>
                    s.AppUserId == u.Id && s.State == SubscriptionState.Active && s.ExpireTime > limit))
                .Select(u => new { u.TenantId, u.Id })
                .ToListAsync(ct);
            due = rows.Select(x => (x.TenantId, UserId: x.Id)).ToList();
        }

        foreach (var (tenantId, userId) in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Set(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tokens = scope.ServiceProvider.GetRequiredService<UserTokenService>();
                var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
                await scope.ServiceProvider.GetRequiredService<ChatSyncService>()
                    .EnsureSubscriptionAsync(user, await tokens.GetAccessTokenAsync(db, userId, ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Could not renew subscription for user {UserId}", userId);
            }
        }
    }
}
