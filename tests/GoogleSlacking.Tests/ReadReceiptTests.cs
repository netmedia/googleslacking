using GoogleSlacking.Core.Rules;
using GoogleSlacking.Domain;
using GoogleSlacking.Infrastructure;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.ReadReceipts;

namespace GoogleSlacking.Tests;

public class ReadReceiptTests
{
    [Fact] // CAT-050
    public void Rules_are_reciprocal()
    {
        Assert.True(ReadReceiptRules.CanView(tenantEnabled: true, viewerShares: true));
        Assert.False(ReadReceiptRules.CanView(tenantEnabled: true, viewerShares: false));
        Assert.False(ReadReceiptRules.CanView(tenantEnabled: false, viewerShares: true));
        Assert.False(ReadReceiptRules.IsVisible(tenantEnabled: true, memberShares: false, isViewer: false));
        Assert.False(ReadReceiptRules.IsVisible(tenantEnabled: true, memberShares: true, isViewer: true));
        var t = DateTimeOffset.Parse("2026-11-04T10:00:00Z");
        Assert.True(ReadReceiptRules.HasRead(t, t));
        Assert.False(ReadReceiptRules.HasRead(t.AddSeconds(-1), t));
        Assert.False(ReadReceiptRules.HasRead(null, t));
    }

    private static async Task<(Guid Me, Guid Space)> SeedAsync(AppDbContext db, bool meShares)
    {
        var me = new AppUser { Id = Guid.NewGuid(), GoogleUserName = "users/1", Email = "me@a.test" };
        var ana = new AppUser { Id = Guid.NewGuid(), GoogleUserName = "users/2", Email = "ana@a.test" };
        var marko = new AppUser { Id = Guid.NewGuid(), GoogleUserName = "users/3", Email = "marko@a.test" };
        var space = new Space { Id = Guid.NewGuid(), GoogleName = "spaces/S", Kind = SpaceKind.GroupChat };
        db.Users.AddRange(me, ana, marko);
        db.Spaces.Add(space);
        var read = DateTimeOffset.Parse("2026-11-04T10:00:00Z");
        db.SpaceMembers.AddRange(
            new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = "users/1", AppUserId = me.Id, LastReadTime = read },
            new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = "users/2", AppUserId = ana.Id, LastReadTime = read },
            new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = "users/3", AppUserId = marko.Id, LastReadTime = read },
            new SpaceMember { Id = Guid.NewGuid(), SpaceId = space.Id, GoogleUserName = "users/4" }); // not an app user
        db.UserSettings.AddRange(
            new UserSettings { Id = Guid.NewGuid(), AppUserId = me.Id, ShareReadReceipts = meShares },
            new UserSettings { Id = Guid.NewGuid(), AppUserId = ana.Id, ShareReadReceipts = true },
            new UserSettings { Id = Guid.NewGuid(), AppUserId = marko.Id, ShareReadReceipts = false });
        await db.SaveChangesAsync();
        return (me.Id, space.Id);
    }

    [Fact] // CAT-051
    public async Task Only_sharing_members_are_visible_to_a_sharing_viewer()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        var (me, space) = await SeedAsync(db, meShares: true);

        var r = await new ReadReceiptService(db, new NullRealtimeNotifier()).GetAsync(me, space, default);

        Assert.True(r.Enabled);
        Assert.Equal(new[] { "users/2" }, r.Readers.Select(x => x.UserName));
        Assert.Equal(2, r.UnknownCount); // Marko doesn't share, users/4 isn't an app user
    }

    [Fact] // CAT-052
    public async Task Viewer_who_does_not_share_sees_nothing()
    {
        using var t = new TestDb();
        await using var db = t.Context(t.TenantA);
        var (me, space) = await SeedAsync(db, meShares: false);

        var r = await new ReadReceiptService(db, new NullRealtimeNotifier()).GetAsync(me, space, default);

        Assert.False(r.Enabled);
        Assert.Empty(r.Readers);
    }

    [Fact] // CAT-053
    public async Task Company_switch_disables_everything()
    {
        using var t = new TestDb();
        await using (var admin = t.Context(null))
        {
            admin.Tenants.Single(x => x.Id == t.TenantA).ReadReceiptsEnabled = false;
            await admin.SaveChangesAsync();
        }
        await using var db = t.Context(t.TenantA);
        var (me, space) = await SeedAsync(db, meShares: true);

        var r = await new ReadReceiptService(db, new NullRealtimeNotifier()).GetAsync(me, space, default);

        Assert.False(r.Enabled);
    }
}
