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
    /// Gets or sets the REST API port (default: 8888).
    /// </summary>
    public int RestPort { get; init; } = 8888;

    /// <summary>
    /// Gets or sets the gRPC port (default: 50051).
    /// </summary>
    public int GrpcPort { get; init; } = 50051;

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets or sets whether to use HTTPS/TLS.
    /// </summary>
    public bool UseHttps { get; init; } = false;

    /// <summary>
    /// Gets the base URL for REST API.
    /// </summary>
    public string RestBaseUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{RestPort}";

    /// <summary>
    /// Gets the base URL for gRPC.
    /// </summary>
    public string GrpcBaseUrl => $"{(UseHttps ? "https" : "http")}://{Host}:{GrpcPort}";

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
