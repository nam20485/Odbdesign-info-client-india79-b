using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OdbDesignInfoClient.Core.Services.Interfaces;
using OdbDesignInfoClient.Services.Api;
using Polly;
using Polly.Extensions.Http;
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

        // Register Refit REST API client with Polly retry policy
        services.AddRefitClient<IOdbDesignRestApi>()
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri(restBaseUrl);
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .AddPolicyHandler((serviceProvider, _) =>
            {
                var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("HttpRetryPolicy");
                return HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                        onRetry: (outcome, timespan, attempt, _) =>
                        {
                            logger?.LogWarning(
                                "REST API retry attempt {Attempt}/3 after {Delay}s due to {Reason}",
                                attempt,
                                timespan.TotalSeconds,
                                outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                        });
            });

        // Register core services as singletons (they manage connection state)
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<IDesignService, DesignService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ICrossProbeService, CrossProbeService>();

        return services;
    }
}
