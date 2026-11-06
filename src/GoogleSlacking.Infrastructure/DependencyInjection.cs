using GoogleSlacking.Core.Unfurl;
using GoogleSlacking.Core.WebPush;
using GoogleSlacking.Google;
using GoogleSlacking.Infrastructure.Notifications;
using GoogleSlacking.Infrastructure.Search;
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
        services.AddScoped<NotificationService>();
        services.AddScoped<SearchService>();
        services.AddScoped<ReadReceipts.ReadReceiptService>();
        services.TryAddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();
        services.TryAddSingleton<IPresence, NullPresence>();

        services.Configure<WebPushOptions>(config.GetSection(WebPushOptions.Section));
        services.AddHttpClient<WebPushSender>(c => c.Timeout = TimeSpan.FromSeconds(15));

        services.AddMemoryCache();
        services.AddHttpClient(LinkPreviewService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(6))
            .ConfigurePrimaryHttpMessageHandler(LinkPreviewService.CreateHandler);
        services.AddSingleton<LinkPreviewService>();

        services.AddSingleton<InitialSyncQueue>();
        services.AddHostedService<InitialSyncWorker>();
        services.AddHostedService<PubSubPullWorker>();
        services.AddHostedService<SubscriptionMaintenanceWorker>();
        services.AddHostedService<TimersWorker>();
        return services;
    }
}
