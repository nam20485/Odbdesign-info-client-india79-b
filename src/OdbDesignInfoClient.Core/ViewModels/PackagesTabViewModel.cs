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
    /// </summary>
    public async Task LoadAsync(string designId, string stepName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(designId) || string.IsNullOrEmpty(stepName))
            return;

        _currentDesignId = designId;
        _currentStepName = stepName;

        IsLoading = true;
        try
        {
            // TODO: Implement when packages API is available
            _allPackages.Clear();
            TotalCount = 0;
            ApplyFilter();
            await Task.CompletedTask;
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
            await LoadAsync(_currentDesignId, _currentStepName, cancellationToken);
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
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
