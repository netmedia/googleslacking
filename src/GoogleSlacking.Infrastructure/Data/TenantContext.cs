namespace GoogleSlacking.Infrastructure.Data;

/// <summary>
/// Current tenant for this DI scope. Set from the auth cookie in HTTP requests and explicitly
/// by background services (which create one scope per tenant/event).
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    void Set(Guid tenantId);
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public void Set(Guid tenantId)
    {
        if (TenantId is { } existing && existing != tenantId)
            throw new InvalidOperationException("Tenant already set for this scope.");
        TenantId = tenantId;
    }
}
