using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the EDA Data tab: a read-only summary of the step's eda/data
/// file — header fields plus one row per EDA net record with subnet and
/// attribute counts, useful for debugging CAD metadata import issues.
/// </summary>
public partial class EdaDataTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;

    [ObservableProperty]
    private ObservableCollection<EdaNetRowViewModel> _nets = [];

    [ObservableProperty]
    private EdaNetRowViewModel? _selectedNet;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>Gets the eda/data file's units; empty when unavailable.</summary>
    [ObservableProperty]
    private string _units = string.Empty;

    /// <summary>Gets the EDA source tool; empty when unavailable.</summary>
    [ObservableProperty]
    private string _source = string.Empty;

    /// <summary>Gets the server-side path of the eda/data file; empty when unavailable.</summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>Gets the number of layers the eda/data file references.</summary>
    [ObservableProperty]
    private int _layerCount;

    /// <summary>Gets the number of net records in the file.</summary>
    [ObservableProperty]
    private int _totalNets;

    /// <summary>
    /// Gets the honest empty-state message shown when the server exposes no
    /// eda_data for the design/step.
    /// </summary>
    public string EmptyMessage => "No EDA data available for this design/step "
        + "(the server returned no eda/data file).";

    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;

    /// <summary>
    /// Initializes a new instance of EdaDataTabViewModel.
    /// </summary>
    public EdaDataTabViewModel(IDesignService designService)
    {
        _designService = designService;
    }

    /// <summary>
    /// Loads the EDA data summary for the specified design and step.
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
            var summary = await _designService.GetEdaDataSummaryAsync(designId, stepName, cancellationToken);
            Units = summary?.Units ?? string.Empty;
            Source = summary?.Source ?? string.Empty;
            Path = summary?.Path ?? string.Empty;
            LayerCount = summary?.LayerCount ?? 0;
            Nets = summary is null
                ? []
                : new ObservableCollection<EdaNetRowViewModel>(
                    summary.Nets.Select(n => new EdaNetRowViewModel(n)));
            TotalNets = Nets.Count;
            IsEmpty = Nets.Count == 0;
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
            SetError($"Failed to load EDA data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the EDA data.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_currentDesignId != null && _currentStepName != null)
        {
            await LoadAsync(_currentDesignId, _currentStepName, cancellationToken, forceReload: true);
        }
    }
}

/// <summary>
/// Row ViewModel for one EDA net record summary.
/// </summary>
public partial class EdaNetRowViewModel : ObservableObject
{
    private readonly EdaNetSummary _summary;

    /// <summary>Gets the net name (raw record name, "#Index" fallback for unnamed nets).</summary>
    public string Name => _summary.Name;

    /// <summary>Gets the net's index field.</summary>
    public uint Index => _summary.Index;

    /// <summary>Gets the number of TOEPRINT subnets (component pin connections).</summary>
    public int ToeprintCount => _summary.ToeprintCount;

    /// <summary>Gets the number of TRACE subnets.</summary>
    public int TraceCount => _summary.TraceCount;

    /// <summary>Gets the number of VIA subnets.</summary>
    public int ViaCount => _summary.ViaCount;

    /// <summary>Gets the number of PLANE subnets.</summary>
    public int PlaneCount => _summary.PlaneCount;

    /// <summary>Gets the number of attributes on the net record.</summary>
    public int AttributeCount => _summary.AttributeCount;

    /// <summary>
    /// Initializes a new instance of EdaNetRowViewModel.
    /// </summary>
    public EdaNetRowViewModel(EdaNetSummary summary)
    {
        _summary = summary;
    }
}
