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

    /// <summary>
    /// Initializes a new instance of the MainViewModel.
    /// </summary>
    public MainViewModel(
        IConnectionService connectionService,
        IDesignService designService,
        INavigationService navigationService,
        ICrossProbeService crossProbeService)
    {
        _connectionService = connectionService;
        _designService = designService;
        _navigationService = navigationService;
        _crossProbeService = crossProbeService;

        // Subscribe to connection state changes
        _connectionService.StateChanged += OnConnectionStateChanged;
        _crossProbeService.ConnectionChanged += OnViewerConnectionChanged;
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

    /// <summary>
    /// Connects to the server.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var config = new ServerConnectionConfig();
            var success = await _connectionService.ConnectAsync(config, cancellationToken);
            
            if (success)
            {
                await LoadDesignsAsync(cancellationToken);
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
            // Trigger data loading for the current tab
            _ = OnDesignChangedAsync();
        }
    }

    private async Task OnDesignChangedAsync()
    {
        // This will be called when the selected design changes
        // Each tab ViewModel should subscribe to this and reload its data
        await Task.CompletedTask;
    }
}
