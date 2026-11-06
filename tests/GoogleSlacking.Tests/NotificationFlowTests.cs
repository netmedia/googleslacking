using GoogleSlacking.Core.WebPush;
using GoogleSlacking.Domain;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Notifications;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoogleSlacking.Tests;

public class NotificationFlowTests
{
    private sealed class Recorder : IRealtimeNotifier
    {
        public List<(Guid User, string Event, object? Payload)> UserEvents { get; } = [];
        public Task MessagesChangedAsync(Guid tenantId, Guid spaceId, IReadOnlyList<MessageDto> messages, CancellationToken ct) => Task.CompletedTask;
        public Task SpaceChangedAsync(Guid tenantId, Guid spaceId, CancellationToken ct) => Task.CompletedTask;
        public Task SpacesListChangedAsync(Guid tenantId, Guid appUserId, CancellationToken ct) => Task.CompletedTask;
        public Task ToUserAsync(Guid appUserId, string eventName, object? payload, CancellationToken ct)
        {
            UserEvents.Add((appUserId, eventName, payload));
            return Task.CompletedTask;
        }
        public Task ToSpaceAsync(Guid spaceId, string eventName, object? payload, CancellationToken ct) => Task.CompletedTask;
    }

    private static ChatSyncService Create(AppDbContext db, IRealtimeNotifier rt)
    {
        var http = new HttpClient();
        var opts = Options.Create(new GoogleOptions());
        var tokens = new UserTokenService(new EphemeralDataProtectionProvider(), new GoogleOAuthClient(http, opts),
            TimeProvider.System, NullLogger<UserTokenService>.Instance);
        var notifications = new NotificationService(db, rt, new NullPresence(),
            new WebPushSender(http, Options.Create(new WebPushOptions()), TimeProvider.System),
            TimeProvider.System, NullLogger<NotificationService>.Instance);
        return new ChatSyncService(db, new ChatApiClient(http), new PeopleApiClient(http), new WorkspaceEventsClient(http),
            tokens, rt, notifications, opts, TimeProvider.System, NullLogger<ChatSyncService>.Instance);
    }

    private static async Task<(Guid Me, Space Space)> SeedAsync(AppDbContext db, SpaceKind kind)
    {
        var me = new AppUser { Id = Guid.NewGuid(), GoogleUserName = "users/1", Email = "me@a.test" };
        var space = new Space { Id = Guid.NewGuid(), GoogleName = "spaces/S", Kind = kind };
        db.Users.Add(me);
        db.Spaces.Add(space);
        db.SpaceMembers.Add(new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = "users/1", AppUserId = me.Id });
        db.SpaceMembers.Add(new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = "users/2" });
        await db.SaveChangesAsync();
        return (me.Id, space);
    }

    private static ChatMessage Msg(string text, string? mentioned = null) => new()
    {
        Name = "spaces/S/messages/" + Guid.NewGuid().ToString("N"),
        Sender = new ChatUser { Name = "users/2" },
        Text = text,
        CreateTime = DateTimeOffset.UtcNow,
        Annotations = mentioned is null ? null
            : System.Text.Json.JsonDocument.Parse($$"""[{"type":"USER_MENTION","userMention":{"user":{"name":"{{mentioned}}"}}}]""").RootElement,
    };

    [Fact] // CAT-030
    public async Task Dm_from_someone_else_notifies_me()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        var (me, _) = await SeedAsync(db, SpaceKind.DirectMessage);
        var rt = new Recorder();

        await Create(db, rt).UpsertMessagesAsync([Msg("bok")], notify: true, default);

        Assert.Contains(rt.UserEvents, e => e.User == me && e.Event == "notification");
    }

    [Fact] // CAT-031
    public async Task Space_message_without_mention_does_not_notify_but_mention_does_and_creates_activity()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        var (me, _) = await SeedAsync(db, SpaceKind.Space);
        var rt = new Recorder();
        var sync = Create(db, rt);

        await sync.UpsertMessagesAsync([Msg("bok svima")], notify: true, default);
        Assert.DoesNotContain(rt.UserEvents, e => e.Event == "notification");

        await sync.UpsertMessagesAsync([Msg("@Vladan pogledaj", "users/1")], notify: true, default);
        Assert.Contains(rt.UserEvents, e => e.User == me && e.Event == "notification");
        Assert.Equal(1, await db.Activity.CountAsync(a => a.AppUserId == me && a.Kind == ActivityKind.Mention));
    }

    [Fact] // CAT-032
    public async Task Own_messages_never_notify()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        await SeedAsync(db, SpaceKind.DirectMessage);
        var rt = new Recorder();
        var mine = Msg("moja poruka") with { Sender = new ChatUser { Name = "users/1" } };

        await Create(db, rt).UpsertMessagesAsync([mine], notify: true, default);

        Assert.DoesNotContain(rt.UserEvents, e => e.Event == "notification");
    }

    [Fact] // CAT-033
    public void Reactions_merge_summary_counts_with_known_users()
    {
        var known = new[]
        {
            new MessageReaction { GoogleName = "r1", UserGoogleName = "users/1", Emoji = "👍" },
            new MessageReaction { GoogleName = "r2", UserGoogleName = "users/2", Emoji = "🎉" },
        };
        var result = ChatQueries.BuildReactions("""[{"emoji":{"unicode":"👍"},"reactionCount":3}]""", known);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Single(r => r.Emoji == "👍").Count);
        Assert.Equal(new[] { "users/1" }, result.Single(r => r.Emoji == "👍").Users);
        Assert.Equal(1, result.Single(r => r.Emoji == "🎉").Count); // not yet in Google's summary
    }
}
