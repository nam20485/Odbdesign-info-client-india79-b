namespace OdbDesignInfoClient.Core.Services.Interfaces;

/// <summary>
/// Handles navigation between views and tabs in the application.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets or sets the current active tab index.
    /// </summary>
    int CurrentTabIndex { get; set; }

    /// <summary>
    /// Event raised when navigation occurs.
    /// </summary>
    event EventHandler<NavigationEventArgs>? Navigated;

    /// <summary>
    /// Navigates to a specific tab.
    /// </summary>
    /// <param name="tabIndex">The tab index to navigate to.</param>
    void NavigateToTab(int tabIndex);

    /// <summary>
    /// Navigates to a specific entity (e.g., component, net).
    /// </summary>
    /// <param name="entityType">The type of entity.</param>
    /// <param name="entityId">The entity identifier.</param>
    void NavigateToEntity(string entityType, string entityId);
}

/// <summary>
/// Event arguments for navigation events.
/// </summary>
public class NavigationEventArgs : EventArgs
{
    /// <summary>
    /// Gets the target tab index.
    /// </summary>
    public int TabIndex { get; init; }

    /// <summary>
    /// Gets the entity type being navigated to (if any).
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// Gets the entity ID being navigated to (if any).
    /// </summary>
    public string? EntityId { get; init; }
}
