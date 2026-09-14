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
    private readonly ServerConnectionConfig _serverConfig;

    private CancellationTokenSource? _loadCts;
    private ConnectionState _previousState = ConnectionState.Disconnected;

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

    /// <summary>
    /// Gets whether an initial connection attempt is in progress.
    /// </summary>
    public bool IsConnecting => ConnectionState == ConnectionState.Connecting;

    /// <summary>
    /// Gets whether the connection is down (not connected, connecting, or reconnecting).
    /// </summary>
    public bool IsDisconnected => ConnectionState == ConnectionState.Disconnected;

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
        ServerConnectionConfig serverConfig,
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
        _serverConfig = serverConfig;

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

    /// <summary>
    /// Initializes the ViewModel and optionally auto-connects.
    /// </summary>
    public async Task InitializeAsync(bool autoConnect = false)
    {
        if (autoConnect)
        {
            StatusMessage = "Auto-connecting...";
            await ConnectAsync();
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        var recoveredFromReconnect = state == ConnectionState.Connected
            && _previousState == ConnectionState.Reconnecting;
        _previousState = state;

        ConnectionState = state;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsDisconnected));

        StatusMessage = state switch
        {
            ConnectionState.Connected => "Connected to server",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Reconnecting => "Reconnecting...",
            ConnectionState.Disconnected => "Disconnected",
            _ => "Unknown state"
        };

        // Data may have gone stale while the connection was down: invalidate
        // caches and reload so the UI reflects the server's current state.
        if (recoveredFromReconnect)
        {
            _ = ReloadAfterRecoveryAsync();
        }
    }

    private async Task ReloadAfterRecoveryAsync()
    {
        try
        {
            _designService.ClearCache();
            await LoadDesignsAsync();
            _ = await RunTabLoadSafeAsync(forceReload: true);
            StatusMessage = "Connection restored — data reloaded";
        }
        catch (Exception)
        {
            // Status message already reflects connection state; nothing to add
        }
    }

    private void OnViewerConnectionChanged(object? sender, bool isConnected)
    {
        IsViewerConnected = isConnected;
    }

    private async void OnNavigated(object? sender, NavigationEventArgs e)
    {
        SelectedTabIndex = e.TabIndex;

        // Handle entity navigation (deep linking)
        if (string.IsNullOrEmpty(e.EntityType) || string.IsNullOrEmpty(e.EntityId))
        {
            return;
        }

        try
        {
            // Ensure the target tab has data before navigating within it;
            // the load-once guard makes this a no-op when already loaded
            await RunTabLoadSafeAsync(forceReload: false);

            switch (e.EntityType.ToLowerInvariant())
            {
                case "component":
                    ComponentsTab.NavigateToComponent(e.EntityId);
                    break;
                case "net":
                    NetsTab.NavigateToNet(e.EntityId);
                    break;
            }
        }
        catch (Exception)
        {
            // Deep-linking is best-effort; tab error banners show the underlying failure
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
            var success = await _connectionService.ConnectAsync(_serverConfig, cancellationToken);

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
        // Cancel any in-flight tab loads before tearing down
        _loadCts?.Cancel();

        // Disconnect from services - swallow cross-probe exceptions to ensure cleanup always happens
        try
        {
            await _crossProbeService.DisconnectAsync();
        }
        catch (Exception ex)
        {
            // Ignore cross-probe disconnect errors - UI state should still be reset
            System.Diagnostics.Debug.WriteLine($"Cross-probe disconnect error: {ex.Message}");
        }

        await _connectionService.DisconnectAsync();
        _designService.ClearCache();
        Designs = [];
        SelectedDesign = null;
    }

    /// <summary>
    /// Refreshes the design data, force-reloading from the server.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectionState != ConnectionState.Connected)
            return;

        IsLoading = true;
        try
        {
            // Cancel any in-flight loads so a slow fetch can't overwrite fresh data
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            // Refresh means force-reload: bypass caches and load-once guards
            _designService.ClearCache();
            await LoadDesignsAsync(_loadCts.Token);

            if (SelectedDesign != null)
            {
                await LoadTabDataAsync(_loadCts.Token, forceReload: true);
            }

            StatusMessage = $"Refreshed at {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDesignsAsync(CancellationToken cancellationToken = default)
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

            // A design switch invalidates everything fetched for the previous
            // design: cancel in-flight loads, clear server caches, and let the
            // load-once guards re-arm for the new design
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            _designService.ClearCache();

            _ = RunTabLoadSafeAsync(forceReload: false);
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (SelectedDesign != null)
        {
            // The tab's load-once guard makes repeat switches free
            _ = RunTabLoadSafeAsync(forceReload: false);
        }
    }

    /// <summary>
    /// Runs the current tab load as a fire-and-forget task that cannot leak
    /// unobserved exceptions. Returns the token of the load generation, or
    /// an invalid token when no load could start.
    /// </summary>
    private async Task<CancellationToken> RunTabLoadSafeAsync(bool forceReload)
    {
        var cts = _loadCts ??= new CancellationTokenSource();
        var token = cts.Token;
        try
        {
            await LoadCurrentTabDataAsync(token, forceReload);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer design selection or refresh - expected
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading tab data: {ex.Message}";
        }

        return token;
    }

    /// <summary>
    /// Attempts to resolve the current design/step selection.
    /// Surfaces a status message and returns false when the design has no
    /// steps instead of guessing a step name.
    /// </summary>
    private bool TryGetSelectionContext(out string designId, out string stepName)
    {
        designId = string.Empty;
        stepName = string.Empty;

        if (SelectedDesign == null)
        {
            return false;
        }

        var step = SelectedDesign.Steps.FirstOrDefault();
        if (step == null)
        {
            StatusMessage = $"Design '{SelectedDesign.Name}' has no steps - nothing to load";
            return false;
        }

        designId = SelectedDesign.Id;
        stepName = step;
        return true;
    }

    private async Task LoadTabDataAsync(CancellationToken cancellationToken = default, bool forceReload = true)
    {
        if (!TryGetSelectionContext(out var designId, out var stepName))
            return;

        // Load data for all tabs in parallel
        await Task.WhenAll(
            ComponentsTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            NetsTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            StackupTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            DrillToolsTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            PackagesTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            PartsTab.LoadAsync(designId, stepName, cancellationToken, forceReload)
        );
    }

    private async Task LoadCurrentTabDataAsync(CancellationToken cancellationToken = default, bool forceReload = false)
    {
        if (!TryGetSelectionContext(out var designId, out var stepName))
            return;

        // Load data only for the current tab (lazy loading)
        switch (SelectedTabIndex)
        {
            case 0:
                await ComponentsTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 1:
                await NetsTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 2:
                await StackupTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 3:
                await DrillToolsTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 4:
                await PackagesTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 5:
                await PartsTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
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

        _loadCts?.Cancel();
        _loadCts?.Dispose();

        GC.SuppressFinalize(this);
    }
}
