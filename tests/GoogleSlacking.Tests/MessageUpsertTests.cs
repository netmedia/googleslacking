using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Notifications;
using GoogleSlacking.Core.WebPush;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Tests;

public class MessageUpsertTests
{
    private sealed class RecordingNotifier : IRealtimeNotifier
    {
        public List<MessageDto> Sent { get; } = [];
        public Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct)
        {
            Sent.AddRange(messages);
            return Task.CompletedTask;
        }
        public Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct) => Task.CompletedTask;
        public Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct) => Task.CompletedTask;
        public Task ToUserAsync(Guid appUserId, string eventName, object? payload, CancellationToken ct) => Task.CompletedTask;
        public Task ToSpaceAsync(Guid spaceId, string eventName, object? payload, CancellationToken ct) => Task.CompletedTask;
    }

    private static ChatSyncService Create(AppDbContext db, IRealtimeNotifier notifier)
    {
        var http = new HttpClient();
        var opts = Options.Create(new GoogleOptions());
        var tokens = new UserTokenService(new EphemeralDataProtectionProvider(), new GoogleOAuthClient(http, opts),
            TimeProvider.System, NullLogger<UserTokenService>.Instance);
        var notifications = new NotificationService(db, notifier, new NullPresence(),
            new WebPushSender(http, Options.Create(new WebPushOptions()), TimeProvider.System),
            TimeProvider.System, NullLogger<NotificationService>.Instance);
        return new ChatSyncService(db, new ChatApiClient(http), new PeopleApiClient(http), new WorkspaceEventsClient(http),
            tokens, notifier, notifications, opts, TimeProvider.System, NullLogger<ChatSyncService>.Instance);
    }

    private static ChatMessage Msg(string text, DateTimeOffset? updated = null) => new()
    {
        Name = "spaces/S1/messages/M1",
        Sender = new ChatUser { Name = "users/1" },
        Text = text,
        CreateTime = DateTimeOffset.Parse("2026-11-04T10:00:00Z"),
        LastUpdateTime = updated,
    };

    [Fact]
    public async Task Duplicate_events_are_deduplicated_and_updates_applied()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        db.Spaces.Add(new Space { Id = Guid.NewGuid(), GoogleName = "spaces/S1" });
        await db.SaveChangesAsync();

        var notifier = new RecordingNotifier();
        var sync = Create(db, notifier);

        Assert.Single(await sync.UpsertMessagesAsync([Msg("bok")], notify: true, default));
        // Same event delivered again via another member's subscription -> no change, no broadcast.
        Assert.Empty(await sync.UpsertMessagesAsync([Msg("bok")], notify: true, default));
        // Edited message -> applied.
        Assert.Single(await sync.UpsertMessagesAsync([Msg("bok!", DateTimeOffset.Parse("2026-11-04T10:05:00Z"))], notify: true, default));

        Assert.Equal(2, notifier.Sent.Count);
        Assert.Equal("bok!", (await db.Messages.SingleAsync()).Text);
    }

    [Fact]
    public async Task Messages_for_unknown_spaces_are_skipped()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        var sync = Create(db, new RecordingNotifier());
        Assert.Empty(await sync.UpsertMessagesAsync([Msg("x")], notify: false, default));
    }
}
