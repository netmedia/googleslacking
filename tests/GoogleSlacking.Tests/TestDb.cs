using GoogleSlacking.Domain;
using GoogleSlacking.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Tests;

/// <summary>SQLite in-memory database shared by several contexts (one per simulated scope/tenant).</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public Guid TenantA { get; } = Guid.NewGuid();
    public Guid TenantB { get; } = Guid.NewGuid();

    public TestDb()
    {
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = Context(null);
        db.Database.EnsureCreated();
        db.Tenants.AddRange(
            new Tenant { Id = TenantA, Name = "A", Domain = "a.test" },
            new Tenant { Id = TenantB, Name = "B", Domain = "b.test" });
        db.SaveChanges();
    }

    public AppDbContext Context(Guid? tenantId)
    {
        var t = new TenantContext();
        if (tenantId is { } id) t.Set(id);
        return new AppDbContext(_options, t);
    }

    public void Dispose() => _conn.Dispose();
}
