using Refit;

namespace OdbDesignInfoClient.Services.Api;

/// <summary>
/// Refit interface for OdbDesignServer REST API endpoints.
/// </summary>
/// <remarks>
/// REST carries the control plane only (AGENTS.md §5: design discovery, metadata,
/// health checks) and is Basic-authenticated. All design content reads (matrix, step
/// headers, EDA data, symbols, components/nets/packages/parts/drill tools) go over
/// gRPC via <see cref="IOdbDesignGrpcClient"/> — the server's derived-data contract
/// is gRPC-only and REST is frozen at security fixes (handoff §5b, decision D5).
/// The step-name list stays here because gRPC offers no cheap equivalent: the only
/// design-wide rpc (<c>GetDesign</c>) downloads the full file model, which is
/// prohibitive for list views.
/// </remarks>
public interface IOdbDesignRestApi
{
    /// <summary>
    /// Gets the list of all available design names.
    /// </summary>
    [Get("/filemodels")]
    Task<ApiResponse<string>> GetDesignNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of steps for a design.
    /// </summary>
    [Get("/filemodels/{name}/steps")]
    Task<ApiResponse<string>> GetStepsAsync(string name, CancellationToken cancellationToken = default);

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
