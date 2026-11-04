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
            e.HasIndex(x => new { x.TenantId, x.GoogleName }).IsUnique();
            e.HasIndex(x => new { x.SpaceId, x.CreateTime });
            e.HasIndex(x => new { x.SpaceId, x.ThreadName, x.CreateTime });
            e.HasOne<Space>().WithMany().HasForeignKey(x => x.SpaceId);
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
