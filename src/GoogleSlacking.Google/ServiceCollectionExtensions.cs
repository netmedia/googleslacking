using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoogleSlacking.Google;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleClients(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GoogleOptions>(config.GetSection(GoogleOptions.Section));
        services.AddSingleton(TimeProvider.System);

        services.AddHttpClient<ChatApiClient>();
        services.AddHttpClient<WorkspaceEventsClient>();
        services.AddHttpClient<PeopleApiClient>();
        services.AddHttpClient<GoogleOAuthClient>();
        services.AddHttpClient<PubSubClient>(c => c.Timeout = TimeSpan.FromSeconds(90));

        // Singleton: caches the service-account token across calls.
        services.AddHttpClient(nameof(ServiceAccountTokenProvider));
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ServiceAccountTokenProvider>(sp,
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ServiceAccountTokenProvider))));

        return services;
    }
}
