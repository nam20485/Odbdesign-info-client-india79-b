using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Nets tab with hierarchical data display.
/// </summary>
public partial class NetsTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;
    private readonly ICrossProbeService _crossProbeService;

    [ObservableProperty]
    private ObservableCollection<NetRowViewModel> _nets = [];

    [ObservableProperty]
    private NetRowViewModel? _selectedNet;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    private IReadOnlyList<Net> _allNets = [];
    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;
    private CancellationTokenSource? _filterDebounceCts;

    /// <summary>
    /// Initializes a new instance of NetsTabViewModel.
    /// </summary>
    public NetsTabViewModel(
        IDesignService designService,
        INavigationService navigationService,
        ICrossProbeService crossProbeService)
    {
        _designService = designService;
        _navigationService = navigationService;
        _crossProbeService = crossProbeService;
    }

    /// <summary>
    /// Loads nets for the specified design and step.
    /// Loads once per design/step; repeat activations reuse the in-memory data
    /// unless <paramref name="forceReload"/> is set (Refresh command).
    /// </summary>
    public async Task LoadAsync(string designId, string stepName, CancellationToken cancellationToken = default, bool forceReload = false)
    {
        if (string.IsNullOrEmpty(designId) || string.IsNullOrEmpty(stepName))
            return;

        if (!forceReload && _loadedDesignId == designId && _loadedStepName == stepName)
            return;

        _currentDesignId = designId;
        _currentStepName = stepName;

        IsLoading = true;
        ClearError();
        try
        {
            _allNets = await _designService.GetNetsAsync(designId, stepName, cancellationToken);
            TotalCount = _allNets.Count;
            ApplyFilter();
            _loadedDesignId = designId;
            _loadedStepName = stepName;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            SetError("Authentication failed (401). Set ODBDESIGN_REST_USERNAME / ODBDESIGN_REST_PASSWORD and restart.");
        }
        catch (Exception ex)
        {
            SetError($"Failed to load nets: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the net data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_currentDesignId != null && _currentStepName != null)
        {
            await LoadAsync(_currentDesignId, _currentStepName, cancellationToken, forceReload: true);
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        // Debounced: don't rebuild the row collection on every keystroke
        _filterDebounceCts?.Cancel();
        _filterDebounceCts?.Dispose();
        _filterDebounceCts = new CancellationTokenSource();
        _ = DebouncedApplyFilterAsync(_filterDebounceCts.Token);
    }

    private async Task DebouncedApplyFilterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke
        }
    }

    partial void OnSelectedNetChanged(NetRowViewModel? value)
    {
        if (value != null)
        {
            _ = SendCrossProbeAsync(value);
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allNets
            : _allNets.Where(n =>
                n.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        var viewModels = filtered.Select(net => 
            new NetRowViewModel(net, _navigationService)).ToList();
        
        Nets = new ObservableCollection<NetRowViewModel>(viewModels);
        FilteredCount = Nets.Count;
    }

    private async Task SendCrossProbeAsync(NetRowViewModel net)
    {
        if (_crossProbeService.IsConnected)
        {
            try
            {
                await _crossProbeService.HighlightNetAsync(net.Name);
            }
            catch (Exception ex)
            {
                // Ignore cross-probe errors - shouldn't block UI interaction
                System.Diagnostics.Debug.WriteLine($"Cross-probe error for net '{net.Name}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Navigates to a specific net by name.
    /// </summary>
    public void NavigateToNet(string netName)
    {
        // Apply the filter immediately rather than waiting out the debounce
        _filterDebounceCts?.Cancel();
        FilterText = netName;
        ApplyFilter();

        var net = Nets.FirstOrDefault(n => n.Name.Equals(netName, StringComparison.OrdinalIgnoreCase));
        if (net != null)
        {
            SelectedNet = net;
            net.IsExpanded = true;
        }
    }
}

/// <summary>
/// Row ViewModel for a net in the TreeDataGrid.
/// </summary>
public partial class NetRowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly Net _net;

    public string Name => _net.Name;
    public int PinCount => _net.PinCount;
    public int ViaCount => _net.ViaCount;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<NetFeatureRowViewModel> _features = [];

    /// <summary>
    /// Initializes a new instance of NetRowViewModel.
    /// </summary>
    public NetRowViewModel(Net net, INavigationService navigationService)
    {
        _net = net;
        _navigationService = navigationService;

        foreach (var feature in net.Features)
        {
            Features.Add(new NetFeatureRowViewModel(feature, navigationService));
        }
    }
}

/// <summary>
/// Row ViewModel for a net feature (pin, via, etc.) in the net hierarchy.
/// </summary>
public partial class NetFeatureRowViewModel : ObservableObject
{
    private readonly NetFeature _feature;
    private readonly INavigationService _navigationService;

    public string FeatureType => _feature.FeatureType;
    public string Id => _feature.Id;
    public string ComponentRef => _feature.ComponentRef;

    /// <summary>
    /// Initializes a new instance of NetFeatureRowViewModel.
    /// </summary>
    public NetFeatureRowViewModel(NetFeature feature, INavigationService navigationService)
    {
        _feature = feature;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Navigates to the connected component.
    /// </summary>
    [RelayCommand]
    public void NavigateToComponent()
    {
        if (!string.IsNullOrEmpty(ComponentRef))
        {
            _navigationService.NavigateToEntity("component", ComponentRef);
        }
    }
}
