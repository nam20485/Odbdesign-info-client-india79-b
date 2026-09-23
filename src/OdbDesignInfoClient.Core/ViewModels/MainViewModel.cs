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
    private bool _isSyncingStepFromDesign;

    [ObservableProperty]
    private string _title = "OdbDesignInfo Client";

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private Design? _selectedDesign;

    [ObservableProperty]
    private IReadOnlyList<Design> _designs = [];

    [ObservableProperty]
    private IReadOnlyList<string> _steps = [];

    [ObservableProperty]
    private string? _selectedStep;

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
    public PinsTabViewModel PinsTab { get; }
    public ViasTabViewModel ViasTab { get; }
    public StepHierarchyTabViewModel StepHierarchyTab { get; }
    public SymbolsTabViewModel SymbolsTab { get; }
    public EdaDataTabViewModel EdaDataTab { get; }

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
        PartsTabViewModel partsTab,
        PinsTabViewModel pinsTab,
        ViasTabViewModel viasTab,
        StepHierarchyTabViewModel stepHierarchyTab,
        SymbolsTabViewModel symbolsTab,
        EdaDataTabViewModel edaDataTab)
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
        PinsTab = pinsTab;
        ViasTab = viasTab;
        StepHierarchyTab = stepHierarchyTab;
        SymbolsTab = symbolsTab;
        EdaDataTab = edaDataTab;

        // Subscribe to connection state changes
        _connectionService.StateChanged += OnConnectionStateChanged;
        _crossProbeService.ConnectionChanged += OnViewerConnectionChanged;

        // Subscribe to navigation events
        _navigationService.Navigated += OnNavigated;

        // Step rows in the Step Hierarchy tab can promote a step to the active context
        StepHierarchyTab.StepActivationRequested += OnStepActivationRequested;
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
        var hasEntity = !string.IsNullOrEmpty(e.EntityType) && !string.IsNullOrEmpty(e.EntityId);

        if (hasEntity)
        {
            try
            {
                // Ensure the target tab has data before navigating within it. The load
                // runs against the deep-link's tab (SelectedTabIndex hasn't moved yet)
                // and completes before the switch below, so the switch-triggered load
                // becomes a load-once-guard no-op instead of a second concurrent fetch
                // that could replace the rows right after the deep-link selects one.
                await RunTabLoadSafeAsync(forceReload: false, tabIndex: e.TabIndex);
            }
            catch (Exception)
            {
                // Deep-linking is best-effort; tab error banners show the underlying failure
            }
        }

        SelectedTabIndex = e.TabIndex;

        if (!hasEntity)
        {
            return;
        }

        try
        {
            switch (e.EntityType!.ToLowerInvariant())
            {
                case "component":
                    ComponentsTab.NavigateToComponent(e.EntityId!);
                    break;
                case "net":
                    NetsTab.NavigateToNet(e.EntityId!);
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
        Steps = [];
        SelectedStep = null;
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

            // Repopulate the step selector for the new design. The guard flag keeps
            // the programmatic step change from triggering the step-change reload
            // below — the design-switch reload right after covers the new context.
            _isSyncingStepFromDesign = true;
            try
            {
                Steps = value.Steps;
                SelectedStep = Steps.FirstOrDefault();
            }
            finally
            {
                _isSyncingStepFromDesign = false;
            }

            // A design switch invalidates everything fetched for the previous
            // design: cancel in-flight loads, clear server caches, and let the
            // load-once guards re-arm for the new design
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            _designService.ClearCache();

            _ = RunTabLoadSafeAsync(forceReload: false);
        }
    }

    partial void OnSelectedStepChanged(string? value)
    {
        // Programmatic updates during a design switch are covered by the design
        // change handler; only user-driven step changes reload here.
        if (_isSyncingStepFromDesign || string.IsNullOrEmpty(value) || SelectedDesign == null)
        {
            return;
        }

        StatusMessage = $"Selected step: {value}";

        // A step switch is a context change (Arch §5.1): cancel in-flight loads,
        // invalidate caches, and reload the active tab for the new step. The other
        // tabs reload lazily — their load-once guards key on (design, step), so the
        // new step re-arms them on first activation.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _designService.ClearCache();

        _ = RunTabLoadSafeAsync(forceReload: false);
    }

    /// <summary>
    /// Handles a step activation from the Step Hierarchy tab by promoting that
    /// step to the global step context (same as picking it in the step selector).
    /// </summary>
    private void OnStepActivationRequested(object? sender, string stepName)
    {
        if (!string.IsNullOrEmpty(stepName) && Steps.Contains(stepName))
        {
            SelectedStep = stepName;
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
    /// an invalid token when no load could start. When <paramref name="tabIndex"/>
    /// is given, that tab is loaded instead of the currently selected one.
    /// </summary>
    private async Task<CancellationToken> RunTabLoadSafeAsync(bool forceReload, int? tabIndex = null)
    {
        var cts = _loadCts ??= new CancellationTokenSource();
        var token = cts.Token;
        try
        {
            await LoadCurrentTabDataAsync(token, forceReload, tabIndex);
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

        var step = SelectedStep ?? SelectedDesign.Steps.FirstOrDefault();
        if (string.IsNullOrEmpty(step))
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
            PartsTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            PinsTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            ViasTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            StepHierarchyTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            SymbolsTab.LoadAsync(designId, stepName, cancellationToken, forceReload),
            EdaDataTab.LoadAsync(designId, stepName, cancellationToken, forceReload)
        );
    }

    private async Task LoadCurrentTabDataAsync(CancellationToken cancellationToken = default, bool forceReload = false, int? tabIndex = null)
    {
        if (!TryGetSelectionContext(out var designId, out var stepName))
            return;

        // Load data only for the requested tab (lazy loading)
        switch (tabIndex ?? SelectedTabIndex)
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
            case 6:
                await PinsTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 7:
                await ViasTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 8:
                await StepHierarchyTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 9:
                await SymbolsTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
                break;
            case 10:
                await EdaDataTab.LoadAsync(designId, stepName, cancellationToken, forceReload);
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
        StepHierarchyTab.StepActivationRequested -= OnStepActivationRequested;

        _loadCts?.Cancel();
        _loadCts?.Dispose();

        GC.SuppressFinalize(this);
    }
}
