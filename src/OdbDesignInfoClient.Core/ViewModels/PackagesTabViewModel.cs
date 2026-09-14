using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Packages tab.
/// </summary>
public partial class PackagesTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ObservableCollection<PackageRowViewModel> _packages = [];

    [ObservableProperty]
    private PackageRowViewModel? _selectedPackage;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    private List<PackageRowViewModel> _allPackages = [];
    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;
    private CancellationTokenSource? _filterDebounceCts;

    /// <summary>
    /// Initializes a new instance of PackagesTabViewModel.
    /// </summary>
    public PackagesTabViewModel(IDesignService designService, INavigationService navigationService)
    {
        _designService = designService;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Loads packages for the specified design and step.
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
            var packages = await _designService.GetPackagesAsync(designId, stepName, cancellationToken);
            _allPackages.Clear();

            foreach (var package in packages)
            {
                _allPackages.Add(new PackageRowViewModel
                {
                    Name = package.Name,
                    Pitch = package.Pitch,
                    PinCount = package.PinCount,
                    Width = package.Width,
                    Height = package.Height
                });
            }

            TotalCount = _allPackages.Count;
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
            SetError($"Failed to load packages: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the packages data.
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

    private void ApplyFilter()
    {
        Packages.Clear();

        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allPackages
            : _allPackages.Where(p =>
                p.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var package in filtered)
        {
            Packages.Add(package);
        }

        FilteredCount = Packages.Count;
    }
}

/// <summary>
/// Row ViewModel for a package/footprint.
/// </summary>
public partial class PackageRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private double _pitch;

    [ObservableProperty]
    private int _pinCount;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<PackageUsageRowViewModel> _usages = [];

    /// <summary>
    /// Gets the dimensions as a formatted string.
    /// </summary>
    public string Dimensions => $"{Width:F2} x {Height:F2}";
}

/// <summary>
/// Row ViewModel for a package usage (component reference).
/// </summary>
public partial class PackageUsageRowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _componentRefDes = string.Empty;

    [ObservableProperty]
    private string _partName = string.Empty;

    /// <summary>
    /// Initializes a new instance of PackageUsageRowViewModel.
    /// </summary>
    public PackageUsageRowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    /// <summary>
    /// Navigates to the component.
    /// </summary>
    [RelayCommand]
    public void NavigateToComponent()
    {
        if (!string.IsNullOrEmpty(ComponentRefDes))
        {
            _navigationService.NavigateToEntity("component", ComponentRefDes);
        }
    }
}
