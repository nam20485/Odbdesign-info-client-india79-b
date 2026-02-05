using Microsoft.Extensions.DependencyInjection;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services.Api;
using Refit;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Extension methods for registering services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all OdbDesignInfoClient services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="restBaseUrl">The base URL for REST API (default: http://localhost:8888).
    /// WARNING: Production deployments should override this value via environment variables or appsettings.json.
    /// Configuration precedence: Environment Variables > appsettings.json > parameter default.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOdbDesignInfoClientServices(
        this IServiceCollection services,
        string restBaseUrl = "http://localhost:8888")
    {
        // Register authentication service
        services.AddSingleton<IAuthService, BasicAuthService>();
        services.AddTransient<AuthHeaderHandler>();

        // Register Refit REST API client
        services.AddRefitClient<IOdbDesignRestApi>()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri(restBaseUrl);
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<AuthHeaderHandler>();

        // Register core services as singletons (they manage connection state)
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IDesignService, DesignService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICrossProbeService, CrossProbeService>();

        return services;
    }
}
