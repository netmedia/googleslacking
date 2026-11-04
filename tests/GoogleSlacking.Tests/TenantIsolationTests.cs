using GoogleSlacking.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoogleSlacking.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task Query_filter_hides_other_tenants_data()
    {
        using var t = new TestDb();
        await using (var a = t.Context(t.TenantA))
        {
            a.Spaces.Add(new Space { Id = Guid.NewGuid(), GoogleName = "spaces/A1" });
            await a.SaveChangesAsync();
        }
        await using (var b = t.Context(t.TenantB))
        {
            b.Spaces.Add(new Space { Id = Guid.NewGuid(), GoogleName = "spaces/B1" });
            await b.SaveChangesAsync();
        }

        await using var check = t.Context(t.TenantA);
        var names = await check.Spaces.Select(s => s.GoogleName).ToListAsync();
        Assert.Equal(new[] { "spaces/A1" }, names);
    }

    [Fact]
    public async Task Cross_tenant_write_is_blocked()
    {
        using var t = new TestDb();
        await using var a = t.Context(t.TenantA);
        a.Spaces.Add(new Space { Id = Guid.NewGuid(), TenantId = t.TenantB, GoogleName = "spaces/X" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
    }

    [Fact]
    public async Task Adding_without_tenant_fails()
    {
        using var t = new TestDb();
        await using var none = t.Context(null);
        none.Spaces.Add(new Space { Id = Guid.NewGuid(), GoogleName = "spaces/X" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.SaveChangesAsync());
    }
}
