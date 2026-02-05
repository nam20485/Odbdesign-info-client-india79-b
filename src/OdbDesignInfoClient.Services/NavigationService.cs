using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Services;

/// <summary>
/// Implementation of the navigation service.
/// </summary>
public class NavigationService : INavigationService
{
    private int _currentTabIndex;

    /// <inheritdoc />
    public int CurrentTabIndex
    {
        get => _currentTabIndex;
        set
        {
            if (_currentTabIndex != value)
            {
                _currentTabIndex = value;
                Navigated?.Invoke(this, new NavigationEventArgs { TabIndex = value });
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<NavigationEventArgs>? Navigated;

    /// <inheritdoc />
    public void NavigateToTab(int tabIndex)
    {
        CurrentTabIndex = tabIndex;
    }

    /// <inheritdoc />
    public void NavigateToEntity(string entityType, string entityId)
    {
        // Map entity type to tab index
        var tabIndex = entityType.ToLowerInvariant() switch
        {
            "component" => 0,
            "net" => 1,
            "layer" => 2,
            _ => 0
        };

        Navigated?.Invoke(this, new NavigationEventArgs
        {
            TabIndex = tabIndex,
            EntityType = entityType,
            EntityId = entityId
        });
    }
}
