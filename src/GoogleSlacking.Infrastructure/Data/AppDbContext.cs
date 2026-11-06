using GoogleSlacking.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GoogleSlacking.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Space> Spaces => Set<Space>();
    public DbSet<SpaceMember> SpaceMembers => Set<SpaceMember>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<EventSubscription> EventSubscriptions => Set<EventSubscription>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<ActivityItem> Activity => Set<ActivityItem>();
    public DbSet<MessageReaction> Reactions => Set<MessageReaction>();
    public DbSet<UserSection> Sections => Set<UserSection>();
    public DbSet<ScheduledMessage> ScheduledMessages => Set<ScheduledMessage>();
    public DbSet<SavedItem> SavedItems => Set<SavedItem>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<PinnedMessage> Pins => Set<PinnedMessage>();
    public DbSet<SpaceBookmark> Bookmarks => Set<SpaceBookmark>();

    /// <summary>Referenced by the global query filters; evaluated per query.</summary>
    public Guid? CurrentTenantId => tenant.TenantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // SQLite (integration tests) can't compare/order DateTimeOffset natively.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Domain).HasMaxLength(200);
            e.Property(x => x.GoogleCustomerId).HasMaxLength(50);
            e.HasIndex(x => x.Domain).IsUnique();
        });

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.GoogleUserName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.AvatarUrl).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.GoogleUserName }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<Person>(e =>
        {
            e.Property(x => x.GoogleUserName).HasMaxLength(100);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.AvatarUrl).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.GoogleUserName }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<Space>(e =>
        {
            e.Property(x => x.GoogleName).HasMaxLength(100);
            e.Property(x => x.DisplayName).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.GoogleName }).IsUnique();
            e.HasMany(x => x.Members).WithOne(x => x.Space).HasForeignKey(x => x.SpaceId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<SpaceMember>(e =>
        {
            e.Property(x => x.GoogleUserName).HasMaxLength(100);
            e.HasIndex(x => new { x.SpaceId, x.GoogleUserName }).IsUnique();
            e.HasIndex(x => x.AppUserId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<Message>(e =>
        {
            e.Property(x => x.GoogleName).HasMaxLength(200);
            e.Property(x => x.ThreadName).HasMaxLength(200);
            e.Property(x => x.SenderGoogleUserName).HasMaxLength(100);
            e.Property(x => x.ClientAssignedMessageId).HasMaxLength(100);
            e.Property(x => x.MentionedUserNames).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.GoogleName }).IsUnique();
            e.HasIndex(x => new { x.SpaceId, x.CreateTime });
            e.HasIndex(x => new { x.SpaceId, x.ThreadName, x.CreateTime });
            e.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<UserSettings>(e =>
        {
            e.Property(x => x.Keywords).HasMaxLength(1000);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.Property(x => x.StatusEmoji).HasMaxLength(32);
            e.Property(x => x.StatusText).HasMaxLength(200);
            e.HasIndex(x => x.AppUserId).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<WebPushSubscription>(e =>
        {
            e.Property(x => x.Endpoint).HasMaxLength(1000);
            e.Property(x => x.P256dh).HasMaxLength(200);
            e.Property(x => x.Auth).HasMaxLength(100);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.AppUserId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<ActivityItem>(e =>
        {
            e.Property(x => x.ActorGoogleUserName).HasMaxLength(100);
            e.Property(x => x.Preview).HasMaxLength(300);
            e.HasIndex(x => new { x.AppUserId, x.CreatedAt });
            e.HasIndex(x => new { x.AppUserId, x.MessageId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<MessageReaction>(e =>
        {
            e.Property(x => x.GoogleName).HasMaxLength(300);
            e.Property(x => x.UserGoogleName).HasMaxLength(100);
            e.Property(x => x.Emoji).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.GoogleName }).IsUnique();
            e.HasIndex(x => x.MessageId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<UserSection>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.AppUserId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<ScheduledMessage>(e =>
        {
            e.Property(x => x.ThreadName).HasMaxLength(200);
            e.Property(x => x.Error).HasMaxLength(1000);
            e.Property(x => x.SentMessageName).HasMaxLength(200);
            e.HasIndex(x => new { x.Status, x.SendAt });
            e.HasIndex(x => x.AppUserId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<SavedItem>(e =>
        {
            e.HasIndex(x => new { x.AppUserId, x.MessageId }).IsUnique();
            e.HasIndex(x => x.RemindAt);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<Draft>(e =>
        {
            e.Property(x => x.ThreadKey).HasMaxLength(200);
            e.HasIndex(x => new { x.AppUserId, x.SpaceId, x.ThreadKey }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<PinnedMessage>(e =>
        {
            e.HasIndex(x => new { x.SpaceId, x.MessageId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<SpaceBookmark>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(2000);
            e.HasIndex(x => x.SpaceId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<EventSubscription>(e =>
        {
            e.Property(x => x.GoogleName).HasMaxLength(200);
            e.Property(x => x.TargetResource).HasMaxLength(300);
            e.HasIndex(x => x.GoogleName).IsUnique();
            e.HasIndex(x => x.ExpireTime);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AppUserId);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State != EntityState.Added) continue;
            if (entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = CurrentTenantId
                    ?? throw new InvalidOperationException($"No tenant set while adding {entry.Entity.GetType().Name}.");
            else if (CurrentTenantId is { } t && entry.Entity.TenantId != t)
                throw new InvalidOperationException("Cross-tenant write blocked.");
        }
        return base.SaveChangesAsync(ct);
    }
}
