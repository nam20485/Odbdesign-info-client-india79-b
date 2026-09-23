using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Vias tab: per-net via counts derived from the EDA net
/// records of the gRPC product model.
/// </summary>
public partial class ViasTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private ObservableCollection<ViaRowViewModel> _vias = [];

    [ObservableProperty]
    private ViaRowViewModel? _selectedVia;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _netsWithVias;

    [ObservableProperty]
    private int _totalViaCount;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Gets the honest empty-state message shown when the design/step has no
    /// via rows (either no VIA subnets exist, or the gRPC product model that
    /// carries via information is unavailable).
    /// </summary>
    public string EmptyMessage => "No via rows for this design/step. Via data is derived from the EDA net "
        + "records of the gRPC product model; a design with no VIA subnets — or a server "
        + "without gRPC — shows nothing here rather than fabricated counts.";

    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;

    /// <summary>
    /// Initializes a new instance of ViasTabViewModel.
    /// </summary>
    public ViasTabViewModel(
        IDesignService designService,
        INavigationService navigationService)
    {
        _designService = designService;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Loads via summaries for the specified design and step.
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
            var summaries = await _designService.GetViaSummariesAsync(designId, stepName, cancellationToken);
            Vias = new ObservableCollection<ViaRowViewModel>(
                summaries.Select(v => new ViaRowViewModel(v, _navigationService)));
            NetsWithVias = Vias.Count;
            TotalViaCount = summaries.Sum(v => v.ViaCount);
            IsEmpty = Vias.Count == 0;
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
            SetError($"Failed to load vias: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the via data.
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
/// Row ViewModel for one net's via summary.
/// </summary>
public partial class ViaRowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly ViaSummary _summary;

    /// <summary>Gets the net name.</summary>
    public string NetName => _summary.NetName;

    /// <summary>Gets the net's index in the EDA net list.</summary>
    public uint NetIndex => _summary.NetIndex;

    /// <summary>Gets the number of VIA subnets on the net.</summary>
    public int ViaCount => _summary.ViaCount;

    /// <summary>Gets the number of component-pin (toeprint) connections on the net.</summary>
    public int PinCount => _summary.PinCount;

    /// <summary>
    /// Initializes a new instance of ViaRowViewModel.
    /// </summary>
    public ViaRowViewModel(ViaSummary summary, INavigationService navigationService)
    {
        _summary = summary;
        _navigationService = navigationService;
    }

    /// <summary>
    /// Navigates to the net (deep-link to the Nets tab).
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
