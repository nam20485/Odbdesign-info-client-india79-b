using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Step Hierarchy tab: the steps of the current design with the
/// cheaply-available step-header metadata. The server exposes a flat step list
/// (no nested job/panel hierarchy), so one row per step is the real structure.
/// </summary>
public partial class StepHierarchyTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;

    [ObservableProperty]
    private ObservableCollection<StepRowViewModel> _steps = [];

    [ObservableProperty]
    private StepRowViewModel? _selectedStep;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalSteps;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Gets the honest empty-state message shown when the design has no steps.
    /// </summary>
    public string EmptyMessage => "No steps found for this design.";

    private string? _currentDesignId;
    private string? _loadedDesignId;

    /// <summary>
    /// Raised when the user activates a step row (nice-to-have affordance):
    /// the main view model handles this by switching the global step context.
    /// </summary>
    public event EventHandler<string>? StepActivationRequested;

    /// <summary>
    /// Initializes a new instance of StepHierarchyTabViewModel.
    /// </summary>
    public StepHierarchyTabViewModel(IDesignService designService)
    {
        _designService = designService;
    }

    /// <summary>
    /// Loads step summaries for the specified design. Step data is design-scoped
    /// (the load-once guard keys on the design only).
    /// Loads once per design; repeat activations reuse the in-memory data
    /// unless <paramref name="forceReload"/> is set (Refresh command).
    /// </summary>
    public async Task LoadAsync(string designId, string stepName, CancellationToken cancellationToken = default, bool forceReload = false)
    {
        if (string.IsNullOrEmpty(designId))
            return;

        if (!forceReload && _loadedDesignId == designId)
            return;

        _currentDesignId = designId;

        IsLoading = true;
        ClearError();
        try
        {
            var summaries = await _designService.GetStepSummariesAsync(designId, cancellationToken);
            Steps = new ObservableCollection<StepRowViewModel>(
                summaries.Select(s => new StepRowViewModel(s, this)));
            TotalSteps = Steps.Count;
            IsEmpty = Steps.Count == 0;
            _loadedDesignId = designId;
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
            SetError($"Failed to load steps: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the step data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_currentDesignId != null)
        {
            await LoadAsync(_currentDesignId, stepName: string.Empty, cancellationToken, forceReload: true);
        }
    }

    /// <summary>
    /// Raises <see cref="StepActivationRequested"/> for a step row activation.
    /// </summary>
    internal void OnActivateStep(string stepName)
    {
        StepActivationRequested?.Invoke(this, stepName);
    }
}

/// <summary>
/// Row ViewModel for one step of the design.
/// </summary>
public partial class StepRowViewModel : ObservableObject
{
    private readonly StepHierarchyTabViewModel _parent;
    private readonly StepSummary _summary;

    /// <summary>Gets the step name (the server's step key).</summary>
    public string Name => _summary.Name;

    /// <summary>Gets the step ID from the header, or "—" when unavailable.</summary>
    public string Id => _summary.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Gets the X origin from the header, or "—" when unavailable.</summary>
    public string XOrigin => _summary.XOrigin?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Gets the Y origin from the header, or "—" when unavailable.</summary>
    public string YOrigin => _summary.YOrigin?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Gets the X datum from the header, or "—" when unavailable.</summary>
    public string XDatum => _summary.XDatum?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Gets the Y datum from the header, or "—" when unavailable.</summary>
    public string YDatum => _summary.YDatum?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Gets the number of step-repeat records in the header.</summary>
    public int RepeatCount => _summary.RepeatCount;

    /// <summary>
    /// Initializes a new instance of StepRowViewModel.
    /// </summary>
    public StepRowViewModel(StepSummary summary, StepHierarchyTabViewModel parent)
    {
        _summary = summary;
        _parent = parent;
    }

    /// <summary>
    /// Requests making this step the active step (updates the global step selector).
    /// </summary>
    [RelayCommand]
    private void SetActive()
    {
        _parent.OnActivateStep(Name);
    }
}
