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
    /// Gets or sets the server port.
    /// </summary>
    public int Port { get; init; } = 5000;

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets or sets whether to use HTTPS.
    /// </summary>
    public bool UseHttps { get; init; } = false;

    /// <summary>
    /// Gets the base URI for the server.
    /// </summary>
    public Uri BaseUri => new($"{(UseHttps ? "https" : "http")}://{Host}:{Port}");
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
