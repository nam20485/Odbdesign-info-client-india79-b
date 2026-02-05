using Microsoft.Extensions.DependencyInjection;
using OdbDesignInfoClient.Core.Services.Interfaces;

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
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOdbDesignInfoClientServices(this IServiceCollection services)
    {
        // Register services as singletons (they manage connection state)
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IDesignService, DesignService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICrossProbeService, CrossProbeService>();

        return services;
    }
}
