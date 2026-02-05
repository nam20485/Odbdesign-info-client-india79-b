namespace OdbDesignInfoClient.Core.Services.Interfaces;

/// <summary>
/// Manages cross-probing communication with the 3D viewer.
/// </summary>
public interface ICrossProbeService
{
    /// <summary>
    /// Gets whether the viewer is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Event raised when the viewer connection state changes.
    /// </summary>
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Event raised when a selection event is received from the viewer.
    /// </summary>
    event EventHandler<CrossProbeEventArgs>? SelectionReceived;

    /// <summary>
    /// Attempts to connect to the 3D viewer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection was successful.</returns>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the viewer.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends a selection command to the viewer.
    /// </summary>
    /// <param name="entityType">The type of entity (component, net, etc.).</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="zoomToFit">Whether to zoom to the entity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SelectAsync(string entityType, string entityId, bool zoomToFit = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a highlight command to the viewer.
    /// </summary>
    /// <param name="netName">The net name to highlight.</param>
    /// <param name="color">The highlight color (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HighlightNetAsync(string netName, string? color = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for cross-probe events from the viewer.
/// </summary>
public class CrossProbeEventArgs : EventArgs
{
    /// <summary>
    /// Gets the event type (selection_changed, etc.).
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the entity type.
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the entity ID.
    /// </summary>
    public string EntityId { get; init; } = string.Empty;
}
