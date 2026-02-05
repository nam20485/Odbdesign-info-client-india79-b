using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// Main ViewModel for the application shell.
/// </summary>
public partial class MainViewModel : ObservableObject
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

    // Tab ViewModels
    [ObservableProperty]
    private ComponentsTabViewModel _componentsTab;

    [ObservableProperty]
    private NetsTabViewModel _netsTab;

    [ObservableProperty]
    private StackupTabViewModel _stackupTab;

    [ObservableProperty]
    private DrillToolsTabViewModel _drillToolsTab;

    [ObservableProperty]
    private PackagesTabViewModel _packagesTab;

    [ObservableProperty]
    private PartsTabViewModel _partsTab;

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

        // Initialize tab ViewModels
        _componentsTab = componentsTab;
        _netsTab = netsTab;
        _stackupTab = stackupTab;
        _drillToolsTab = drillToolsTab;
        _packagesTab = packagesTab;
        _partsTab = partsTab;

        // Subscribe to connection state changes
        _connectionService.StateChanged += OnConnectionStateChanged;
        _crossProbeService.ConnectionChanged += OnViewerConnectionChanged;

        // Subscribe to navigation events
        _navigationService.Navigated += OnNavigated;
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        ConnectionState = state;
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

        // Handle entity navigation (deep linking)
        if (!string.IsNullOrEmpty(e.EntityType) && !string.IsNullOrEmpty(e.EntityId))
        {
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

                // Try to connect to 3D viewer
                _ = _crossProbeService.ConnectAsync(cancellationToken);
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
        await _connectionService.DisconnectAsync();
        await _crossProbeService.DisconnectAsync();
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
            _ = LoadTabDataAsync();
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
}
