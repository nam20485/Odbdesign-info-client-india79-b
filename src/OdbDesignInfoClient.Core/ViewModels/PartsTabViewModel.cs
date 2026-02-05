using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Parts tab.
/// </summary>
public partial class PartsTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ObservableCollection<PartRowViewModel> _parts = [];

    [ObservableProperty]
    private PartRowViewModel? _selectedPart;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    private List<PartRowViewModel> _allParts = [];
    private string? _currentDesignId;
    private string? _currentStepName;

    /// <summary>
    /// Initializes a new instance of PartsTabViewModel.
    /// </summary>
    public PartsTabViewModel(IDesignService designService, INavigationService navigationService)
    {
        _designService = designService;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Loads parts for the specified design and step.
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
            // TODO: Implement when parts API is available
            _allParts.Clear();
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
    /// Refreshes the parts data.
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
        Parts.Clear();

        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allParts
            : _allParts.Where(p =>
                p.PartNumber.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var part in filtered)
        {
            Parts.Add(part);
        }

        FilteredCount = Parts.Count;
    }
}

/// <summary>
/// Row ViewModel for a part definition.
/// </summary>
public partial class PartRowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _partNumber = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private int _usageCount;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<PartUsageRowViewModel> _usages = [];

    /// <summary>
    /// Initializes a new instance of PartRowViewModel.
    /// </summary>
    public PartRowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }
    
    /// <summary>
    /// Navigates to the part.
    /// </summary>
    [RelayCommand]
    public void NavigateToPart()
    {
        if (!string.IsNullOrEmpty(PartNumber))
        {
            _navigationService.NavigateToEntity("part", PartNumber);
        }
    }
}

/// <summary>
/// Row ViewModel for a part usage (component reference).
/// </summary>
public partial class PartUsageRowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _componentRefDes = string.Empty;

    /// <summary>
    /// Initializes a new instance of PartUsageRowViewModel.
    /// </summary>
    public PartUsageRowViewModel(INavigationService navigationService)
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
