using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Drill Tools tab.
/// </summary>
public partial class DrillToolsTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;

    [ObservableProperty]
    private ObservableCollection<DrillToolRowViewModel> _drillTools = [];

    [ObservableProperty]
    private DrillToolRowViewModel? _selectedTool;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalTools;

    private string? _currentDesignId;
    private string? _currentStepName;

    /// <summary>
    /// Initializes a new instance of DrillToolsTabViewModel.
    /// </summary>
    public DrillToolsTabViewModel(IDesignService designService)
    {
        _designService = designService;
    }

    /// <summary>
    /// Loads drill tools for the specified design and step.
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
            // TODO: Implement when drill tools API is available
            DrillTools.Clear();
            TotalTools = 0;
            await Task.CompletedTask;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the drill tools data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_currentDesignId != null && _currentStepName != null)
        {
            await LoadAsync(_currentDesignId, _currentStepName, cancellationToken);
        }
    }
}

/// <summary>
/// Row ViewModel for a drill tool.
/// </summary>
public partial class DrillToolRowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _toolNumber;

    [ObservableProperty]
    private double _diameter;

    [ObservableProperty]
    private string _shape = "Round";

    [ObservableProperty]
    private bool _isPlated;

    [ObservableProperty]
    private int _hitCount;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<DrillHitRowViewModel> _hits = [];

    /// <summary>
    /// Gets the plating status as a string.
    /// </summary>
    public string PlatingStatus => IsPlated ? "Plated" : "Non-Plated";
}

/// <summary>
/// Row ViewModel for a drill hit location.
/// </summary>
public partial class DrillHitRowViewModel : ObservableObject
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private string _layer = string.Empty;
}
