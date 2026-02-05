using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// Main ViewModel for the application shell.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionService _connectionService;
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;
    private readonly ICrossProbeService _crossProbeService;

    [ObservableProperty]
    private string _title = "OdbDesignInfo Client";

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private Design? _selectedDesign;

    [ObservableProperty]
    private IReadOnlyList<Design> _designs = [];

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isViewerConnected;

    /// <summary>
    /// Gets whether the connection is active.
    /// </summary>
    public bool IsConnected => ConnectionState == ConnectionState.Connected;

    /// <summary>
    /// Gets whether the connection is currently reconnecting.
    /// </summary>
    public bool IsReconnecting => ConnectionState == ConnectionState.Reconnecting;

    // Tab ViewModels - set once on construction, never change
    public ComponentsTabViewModel ComponentsTab { get; }
    public NetsTabViewModel NetsTab { get; }
    public StackupTabViewModel StackupTab { get; }
    public DrillToolsTabViewModel DrillToolsTab { get; }
    public PackagesTabViewModel PackagesTab { get; }
    public PartsTabViewModel PartsTab { get; }

    /// <summary>
    /// Initializes a new instance of the MainViewModel.
    /// </summary>
    public MainViewModel(
        IConnectionService connectionService,
        IDesignService designService,
        INavigationService navigationService,
        ICrossProbeService crossProbeService,
        ComponentsTabViewModel componentsTab,
        NetsTabViewModel netsTab,
        StackupTabViewModel stackupTab,
        DrillToolsTabViewModel drillToolsTab,
        PackagesTabViewModel packagesTab,
        PartsTabViewModel partsTab)
    {
        _connectionService = connectionService;
        _designService = designService;
        _navigationService = navigationService;
        _crossProbeService = crossProbeService;

        // Initialize tab ViewModels as readonly properties
        ComponentsTab = componentsTab;
        NetsTab = netsTab;
        StackupTab = stackupTab;
        DrillToolsTab = drillToolsTab;
        PackagesTab = packagesTab;
        PartsTab = partsTab;

        // Subscribe to connection state changes
        _connectionService.StateChanged += OnConnectionStateChanged;
        _crossProbeService.ConnectionChanged += OnViewerConnectionChanged;

        // Subscribe to navigation events
        _navigationService.Navigated += OnNavigated;
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        ConnectionState = state;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsReconnecting));
        
        StatusMessage = state switch
        {
            ConnectionState.Connected => "Connected to server",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Reconnecting => "Reconnecting...",
            ConnectionState.Disconnected => "Disconnected",
            _ => "Unknown state"
        };
    }

    private void OnViewerConnectionChanged(object? sender, bool isConnected)
    {
        IsViewerConnected = isConnected;
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        SelectedTabIndex = e.TabIndex;

        // Handle entity navigation (deep linking) - check if tab has data loaded
        if (!string.IsNullOrEmpty(e.EntityType) && !string.IsNullOrEmpty(e.EntityId))
        {
            switch (e.EntityType.ToLowerInvariant())
            {
                case "component":
                    if (ComponentsTab.TotalCount > 0)
                        ComponentsTab.NavigateToComponent(e.EntityId);
                    break;
                case "net":
                    if (NetsTab.TotalCount > 0)
                        NetsTab.NavigateToNet(e.EntityId);
                    break;
            }
        }
    }

    /// <summary>
    /// Connects to the server.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState == ConnectionState.Connected)
        {
            await DisconnectAsync();
            return;
        }

        IsLoading = true;
        try
        {
            var config = new ServerConnectionConfig();
            var success = await _connectionService.ConnectAsync(config, cancellationToken);
            
            if (success)
            {
                await LoadDesignsAsync(cancellationToken);

                // Try to connect to 3D viewer with user notification
                try
                {
                    await _crossProbeService.ConnectAsync(cancellationToken);
                    StatusMessage = "Connected to server and viewer";
                }
                catch (Exception)
                {
                    // Viewer is optional - notify user but continue
                    StatusMessage = "Connected to server (viewer unavailable)";
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        // Disconnect from services - swallow cross-probe exceptions to ensure cleanup always happens
        try
        {
            await _crossProbeService.DisconnectAsync();
        }
        catch
        {
            // Ignore cross-probe disconnect errors - UI state should still be reset
        }
        
        await _connectionService.DisconnectAsync();
        Designs = [];
        SelectedDesign = null;
    }

    /// <summary>
    /// Refreshes the design data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState != ConnectionState.Connected)
            return;

        IsLoading = true;
        try
        {
            await LoadDesignsAsync(cancellationToken);
            
            if (SelectedDesign != null)
            {
                await LoadTabDataAsync(cancellationToken);
            }
            
            StatusMessage = $"Refreshed at {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDesignsAsync(CancellationToken cancellationToken)
    {
        Designs = await _designService.GetDesignsAsync(cancellationToken);
        if (Designs.Count > 0 && SelectedDesign == null)
        {
            SelectedDesign = Designs[0];
        }
    }

    partial void OnSelectedDesignChanged(Design? value)
    {
        if (value != null)
        {
            StatusMessage = $"Selected design: {value.Name}";
            // Use lazy loading: load data only for the current tab
            _ = LoadCurrentTabDataAsync();
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (SelectedDesign != null)
        {
            _ = LoadCurrentTabDataAsync();
        }
    }

    private async Task LoadTabDataAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedDesign == null) return;

        var designId = SelectedDesign.Id;
        var stepName = SelectedDesign.Steps.FirstOrDefault() ?? "pcb";

        // Load data for all tabs in parallel
        await Task.WhenAll(
            ComponentsTab.LoadAsync(designId, stepName, cancellationToken),
            NetsTab.LoadAsync(designId, stepName, cancellationToken),
            StackupTab.LoadAsync(designId, stepName, cancellationToken),
            DrillToolsTab.LoadAsync(designId, stepName, cancellationToken),
            PackagesTab.LoadAsync(designId, stepName, cancellationToken),
            PartsTab.LoadAsync(designId, stepName, cancellationToken)
        );
    }

    private async Task LoadCurrentTabDataAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedDesign == null) return;

        var designId = SelectedDesign.Id;
        var stepName = SelectedDesign.Steps.FirstOrDefault() ?? "pcb";

        // Load data only for the current tab (lazy loading)
        switch (SelectedTabIndex)
        {
            case 0:
                await ComponentsTab.LoadAsync(designId, stepName, cancellationToken);
                break;
            case 1:
                await NetsTab.LoadAsync(designId, stepName, cancellationToken);
                break;
            case 2:
                await StackupTab.LoadAsync(designId, stepName, cancellationToken);
                break;
            case 3:
                await DrillToolsTab.LoadAsync(designId, stepName, cancellationToken);
                break;
            case 4:
                await PackagesTab.LoadAsync(designId, stepName, cancellationToken);
                break;
            case 5:
                await PartsTab.LoadAsync(designId, stepName, cancellationToken);
                break;
        }
    }

    private bool _disposed;

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from all events to prevent memory leaks
        _connectionService.StateChanged -= OnConnectionStateChanged;
        _crossProbeService.ConnectionChanged -= OnViewerConnectionChanged;
        _navigationService.Navigated -= OnNavigated;

        GC.SuppressFinalize(this);
    }
}
