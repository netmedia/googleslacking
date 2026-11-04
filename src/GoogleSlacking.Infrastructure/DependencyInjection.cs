using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Auth;
using GoogleSlacking.Infrastructure.Data;
using GoogleSlacking.Infrastructure.Sync;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoogleSlacking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddGoogleClients(config);
        services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));

        services.AddScoped<ITenantContext, TenantContext>();
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure()));

        // Key ring in the DB so refresh tokens survive restarts and work across instances.
        // Production: add .ProtectKeysWithAzureKeyVault(...) as well.
        services.AddDataProtection()
            .SetApplicationName("GoogleSlacking")
            .PersistKeysToDbContext<AppDbContext>();

        services.AddScoped<UserTokenService>(); // token cache itself is static/process-wide
        services.AddScoped<ChatSyncService>();
        services.AddScoped<ChatQueries>();
        services.AddScoped<EventProcessor>();
        services.TryAddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();

        services.AddSingleton<InitialSyncQueue>();
        services.AddHostedService<InitialSyncWorker>();
        services.AddHostedService<PubSubPullWorker>();
        services.AddHostedService<SubscriptionMaintenanceWorker>();
        return services;
    }
}
