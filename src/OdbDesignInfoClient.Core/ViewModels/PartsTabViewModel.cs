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
    private string? _loadedDesignId;
    private string? _loadedStepName;
    private CancellationTokenSource? _filterDebounceCts;

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
            var parts = await _designService.GetPartsAsync(designId, stepName, cancellationToken);
            _allParts.Clear();

            foreach (var part in parts)
            {
                var row = new PartRowViewModel(_navigationService)
                {
                    PartNumber = part.PartNumber,
                    Manufacturer = part.Manufacturer,
                    Description = part.Description,
                    UsageCount = part.UsageCount
                };

                foreach (var usage in part.Usages)
                {
                    row.Usages.Add(new PartUsageRowViewModel(_navigationService)
                    {
                        ComponentRefDes = usage.ComponentRefDes
                    });
                }

                _allParts.Add(row);
            }

            TotalCount = _allParts.Count;
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
            SetError($"Failed to load parts: {ex.Message}");
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
            await LoadAsync(_currentDesignId, _currentStepName, cancellationToken, forceReload: true);
        }
    }

    /// <summary>
    /// Clears the filter immediately, bypassing the input debounce.
    /// </summary>
    [RelayCommand]
    private void ClearFilter()
    {
        // Setting the property reschedules the debounce; cancel it so the
        // cleared filter applies right away instead of 300 ms later.
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
