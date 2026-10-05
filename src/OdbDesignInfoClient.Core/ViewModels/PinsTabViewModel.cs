using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Pins tab: every placed pin across all components of the
/// current design/step, flattened into one grid with the owning component context.
/// </summary>
public partial class PinsTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;
    private readonly ICrossProbeService _crossProbeService;

    [ObservableProperty]
    private ObservableCollection<PinListItemViewModel> _pins = [];

    [ObservableProperty]
    private PinListItemViewModel? _selectedPin;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    private List<PinListItemViewModel> _allPins = [];
    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;
    private CancellationTokenSource? _filterDebounceCts;

    /// <summary>
    /// Initializes a new instance of PinsTabViewModel.
    /// </summary>
    public PinsTabViewModel(
        IDesignService designService,
        INavigationService navigationService,
        ICrossProbeService crossProbeService)
    {
        _designService = designService;
        _navigationService = navigationService;
        _crossProbeService = crossProbeService;
    }

    /// <summary>
    /// Loads all pins for the specified design and step by flattening the
    /// component list (each component contributes its pins with owner context).
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
            var components = await _designService.GetComponentsAsync(designId, stepName, cancellationToken);
            _allPins = components
                .SelectMany(c => c.Pins.Select(p => new PinListItemViewModel(
                    c.RefDes, c.PartName, c.Side, p, _navigationService)))
                .ToList();
            TotalCount = _allPins.Count;
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
            SetError($"Failed to load pins: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the pin data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_currentDesignId != null && _currentStepName != null)
        {
            await LoadAsync(_currentDesignId, _currentStepName, cancellationToken, forceReload: true);
        }
    }

    /// <summary>
    /// Clears the filter immediately, bypassing the input debounce.
    /// </summary>
    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        _filterDebounceCts?.Cancel();
        ApplyFilter();
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

    partial void OnSelectedPinChanged(PinListItemViewModel? value)
    {
        if (value != null)
        {
            _ = SendCrossProbeAsync(value);
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allPins
            : _allPins.Where(p =>
                p.RefDes.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.PinName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.NetName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        Pins = new ObservableCollection<PinListItemViewModel>(filtered);
        FilteredCount = Pins.Count;
    }

    private async Task SendCrossProbeAsync(PinListItemViewModel pin)
    {
        if (_crossProbeService.IsConnected)
        {
            try
            {
                // Pins belong to components in the viewer's entity model
                await _crossProbeService.SelectAsync("component", pin.RefDes);
            }
            catch (Exception ex)
            {
                // Ignore cross-probe errors - shouldn't block UI interaction
                System.Diagnostics.Debug.WriteLine($"Cross-probe error for pin '{pin.RefDes}-{pin.PinName}': {ex.Message}");
            }
        }
    }
}

/// <summary>
/// Row ViewModel for one placed pin with its owning component context.
/// </summary>
public partial class PinListItemViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    /// <summary>Gets the owning component's reference designator.</summary>
    public string RefDes { get; }

    /// <summary>Gets the owning component's part name.</summary>
    public string PartName { get; }

    /// <summary>Gets the owning component's board side (Top/Bottom).</summary>
    public string Side { get; }

    /// <summary>Gets the pin's display name (falls back to its number).</summary>
    public string PinName { get; }

    /// <summary>Gets the pin number.</summary>
    public int PinNumber { get; }

    /// <summary>Gets the connected net's name; empty when unconnected.</summary>
    public string NetName { get; }

    /// <summary>
    /// Gets the pin's component-local X coordinate in millimeters
    /// (relative to the component origin, before the placement transform).
    /// </summary>
    public double X { get; }

    /// <summary>Gets the pin's component-local Y coordinate in millimeters.</summary>
    public double Y { get; }

    /// <summary>
    /// Initializes a new instance of PinListItemViewModel.
    /// </summary>
    public PinListItemViewModel(
        string refDes,
        string partName,
        string side,
        Pin pin,
        INavigationService navigationService)
    {
        RefDes = refDes;
        PartName = partName;
        Side = side;
        PinName = pin.Name;
        PinNumber = pin.Number;
        NetName = pin.NetName;
        X = pin.X;
        Y = pin.Y;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Navigates to the connected net (deep-link to the Nets tab).
    /// </summary>
    [RelayCommand]
    public void NavigateToNet()
    {
        if (!string.IsNullOrEmpty(NetName))
        {
            _navigationService.NavigateToEntity("net", NetName);
        }
    }
}
