namespace OdbDesignInfoClient.Core.Models;

/// <summary>
/// Represents a connection configuration for the OdbDesign server.
/// </summary>
public record ServerConnectionConfig
{
    /// <summary>
    /// Gets or sets the server host address.
    /// </summary>
    public string Host { get; init; } = "localhost";

    /// <summary>
    /// Gets or sets the REST API port (default: 443, matching the HTTPS Traefik ingress).
    /// </summary>
    public int RestPort { get; init; } = 443;

    /// <summary>
    /// Gets or sets the gRPC port (default: 50051).
    /// </summary>
    public int GrpcPort { get; init; } = 50051;

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets or sets whether the REST endpoint uses HTTPS/TLS.
    /// </summary>
    public bool UseHttps { get; init; } = true;

    /// <summary>
    /// Gets or sets whether the gRPC endpoint uses TLS.
    /// Independent of <see cref="UseHttps"/>: the standard deployment terminates
    /// TLS for REST at the ingress while gRPC is served as plaintext h2c.
    /// </summary>
    public bool GrpcUseTls { get; init; } = false;

    /// <summary>
    /// Gets or sets a full REST base URL override (e.g. from the
    /// ODBDESIGN_REST_URL environment variable). Takes precedence over
    /// the host/port/scheme settings.
    /// </summary>
    public string? RestUrlOverride { get; init; }

    /// <summary>
    /// Gets or sets a full gRPC base URL override (e.g. from the
    /// ODBDESIGN_GRPC_URL environment variable). Takes precedence over
    /// the host/port/scheme settings.
    /// </summary>
    public string? GrpcUrlOverride { get; init; }

    /// <summary>
    /// Gets the base URL for REST API.
    /// </summary>
    public string RestBaseUrl => RestUrlOverride ?? $"{(UseHttps ? "https" : "http")}://{Host}:{RestPort}";

    /// <summary>
    /// Gets the base URL for gRPC.
    /// </summary>
    public string GrpcBaseUrl => GrpcUrlOverride ?? $"{(GrpcUseTls ? "https" : "http")}://{Host}:{GrpcPort}";

    /// <summary>
    /// Gets the base URI for the REST server (for compatibility).
    /// </summary>
    public Uri BaseUri => new(RestBaseUrl);
}

/// <summary>
/// Represents the connection state of the application.
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// Not connected to the server.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Currently attempting to connect.
    /// </summary>
    Connecting,

    /// <summary>
    /// Connected and healthy.
    /// </summary>
    Connected,

    /// <summary>
    /// Connection lost, attempting to reconnect.
    /// </summary>
    Reconnecting
}
