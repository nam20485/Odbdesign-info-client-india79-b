using OdbDesignInfoClient.Core.Models;

namespace OdbDesignInfoClient.Core.Services.Interfaces;

/// <summary>
/// Manages the connection to the OdbDesign server.
/// </summary>
public interface IConnectionService
{
    /// <summary>
    /// Gets the current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Gets the current server configuration.
    /// </summary>
    ServerConnectionConfig Configuration { get; }

    /// <summary>
    /// Event raised when the connection state changes.
    /// </summary>
    event EventHandler<ConnectionState>? StateChanged;

    /// <summary>
    /// Connects to the server with the specified configuration.
    /// </summary>
    /// <param name="config">The connection configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection was successful.</returns>
    Task<bool> ConnectAsync(ServerConnectionConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    Task DisconnectAsync();

    /// <summary>
    /// Checks the health of the server connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the server is healthy.</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);
}
