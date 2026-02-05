using Refit;

namespace OdbDesignInfoClient.Services.Api;

/// <summary>
/// Refit interface for OdbDesignServer REST API endpoints.
/// Used for control plane operations (metadata, health checks, design discovery).
/// </summary>
public interface IOdbDesignRestApi
{
    /// <summary>
    /// Gets the list of all available design names.
    /// </summary>
    [Get("/filemodels")]
    Task<List<string>> GetDesignNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full design details including steps, layers, and symbols.
    /// </summary>
    [Get("/filemodels/{name}")]
    Task<ApiResponse<string>> GetDesignAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of steps for a design.
    /// </summary>
    [Get("/filemodels/{name}/steps")]
    Task<List<string>> GetStepsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of layers for a step.
    /// </summary>
    [Get("/filemodels/{name}/steps/{step}/layers")]
    Task<List<string>> GetLayerNamesAsync(string name, string step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of symbols for a design.
    /// </summary>
    [Get("/filemodels/{name}/symbols")]
    Task<List<string>> GetSymbolNamesAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the components for a design.
    /// </summary>
    [Get("/designs/{name}/components")]
    Task<ApiResponse<string>> GetComponentsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the nets for a design.
    /// </summary>
    [Get("/designs/{name}/nets")]
    Task<ApiResponse<string>> GetNetsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the parts for a design.
    /// </summary>
    [Get("/designs/{name}/parts")]
    Task<ApiResponse<string>> GetPartsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the packages for a design.
    /// </summary>
    [Get("/designs/{name}/packages")]
    Task<ApiResponse<string>> GetPackagesAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liveness health check endpoint.
    /// </summary>
    [Get("/healthz/live")]
    Task<HttpResponseMessage> HealthLiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Readiness health check endpoint.
    /// </summary>
    [Get("/healthz/ready")]
    Task<HttpResponseMessage> HealthReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Startup health check endpoint.
    /// </summary>
    [Get("/healthz/started")]
    Task<HttpResponseMessage> HealthStartedAsync(CancellationToken cancellationToken = default);
}
