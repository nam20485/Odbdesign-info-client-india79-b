using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Components tab with hierarchical data display.
/// </summary>
public partial class ComponentsTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;
    private readonly ICrossProbeService _crossProbeService;

    [ObservableProperty]
    private ObservableCollection<ComponentRowViewModel> _components = [];

    [ObservableProperty]
    private ComponentRowViewModel? _selectedComponent;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    private IReadOnlyList<Component> _allComponents = [];
    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;
    private CancellationTokenSource? _filterDebounceCts;

    /// <summary>
    /// Initializes a new instance of ComponentsTabViewModel.
    /// </summary>
    public ComponentsTabViewModel(
        IDesignService designService,
        INavigationService navigationService,
        ICrossProbeService crossProbeService)
    {
        _designService = designService;
        _navigationService = navigationService;
        _crossProbeService = crossProbeService;
    }

    /// <summary>
    /// Loads components for the specified design and step.
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
            _allComponents = await _designService.GetComponentsAsync(designId, stepName, cancellationToken);
            TotalCount = _allComponents.Count;
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
            SetError($"Failed to load components: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the component data.
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

    partial void OnSelectedComponentChanged(ComponentRowViewModel? value)
    {
        if (value != null)
        {
            _ = SendCrossProbeAsync(value);
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allComponents
            : _allComponents.Where(c =>
                c.RefDes.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                c.PartName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                c.Package.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        var viewModels = filtered.Select(component => 
            new ComponentRowViewModel(component, _navigationService)).ToList();
        
        Components = new ObservableCollection<ComponentRowViewModel>(viewModels);
        FilteredCount = Components.Count;
    }

    private async Task SendCrossProbeAsync(ComponentRowViewModel component)
    {
        if (_crossProbeService.IsConnected)
        {
            try
            {
                await _crossProbeService.SelectAsync("component", component.RefDes);
            }
            catch (Exception ex)
            {
                // Ignore cross-probe errors - shouldn't block UI interaction
                System.Diagnostics.Debug.WriteLine($"Cross-probe error for component '{component.RefDes}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Navigates to a specific component by RefDes.
    /// </summary>
    public void NavigateToComponent(string refDes)
    {
        // Apply the filter immediately rather than waiting out the debounce
        _filterDebounceCts?.Cancel();
        FilterText = refDes;
        ApplyFilter();

        var component = Components.FirstOrDefault(c => c.RefDes.Equals(refDes, StringComparison.OrdinalIgnoreCase));
        if (component != null)
        {
            SelectedComponent = component;
        }
    }
}

/// <summary>
/// Row ViewModel for a component in the TreeDataGrid.
/// </summary>
public partial class ComponentRowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly Component _component;

    public string RefDes => _component.RefDes;
    public string PartName => _component.PartName;
    public string Package => _component.Package;
    public string Side => _component.Side;
    public double Rotation => _component.Rotation;
    public double X => _component.X;
    public double Y => _component.Y;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<PinRowViewModel> _pins = [];

    /// <summary>
    /// Initializes a new instance of ComponentRowViewModel.
    /// </summary>
    public ComponentRowViewModel(Component component, INavigationService navigationService)
    {
        _component = component;
        _navigationService = navigationService;

        foreach (var pin in component.Pins)
        {
            Pins.Add(new PinRowViewModel(pin, navigationService));
        }
    }
}

/// <summary>
/// Row ViewModel for a pin in the component hierarchy.
/// </summary>
public partial class PinRowViewModel : ObservableObject
{
    private readonly Pin _pin;
    private readonly INavigationService _navigationService;

    public string Name => _pin.Name;
    public int Number => _pin.Number;
    public string NetName => _pin.NetName;
    public string ElectricalType => _pin.ElectricalType;

    /// <summary>
    /// Initializes a new instance of PinRowViewModel.
    /// </summary>
    public PinRowViewModel(Pin pin, INavigationService navigationService)
    {
        _pin = pin;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Navigates to the connected net.
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
