using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Stackup/Layers tab.
/// </summary>
public partial class StackupTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;

    [ObservableProperty]
    private ObservableCollection<LayerRowViewModel> _layers = [];

    [ObservableProperty]
    private LayerRowViewModel? _selectedLayer;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalLayers;

    private string? _currentDesignId;
    private string? _currentStepName;
    private string? _loadedDesignId;
    private string? _loadedStepName;

    /// <summary>
    /// Initializes a new instance of StackupTabViewModel.
    /// </summary>
    public StackupTabViewModel(IDesignService designService)
    {
        _designService = designService;
    }

    /// <summary>
    /// Loads the layer stackup for the specified design and step.
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
            var layers = await _designService.GetStackupAsync(designId, stepName, cancellationToken);
            Layers.Clear();

            foreach (var layer in layers)
            {
                Layers.Add(new LayerRowViewModel(layer));
            }

            TotalLayers = Layers.Count;
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
            SetError($"Failed to load stackup: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the stackup data.
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
/// Row ViewModel for a layer in the stackup.
/// </summary>
public partial class LayerRowViewModel : ObservableObject
{
    private readonly Layer _layer;

    /// <summary>Gets the layer identifier.</summary>
    public int Id => _layer.Id;

    /// <summary>Gets the 1-based physical stack order.</summary>
    public int StackOrder => _layer.StackOrder;

    /// <summary>Gets the layer name.</summary>
    public string Name => _layer.Name;

    /// <summary>Gets the layer type (server-authoritative).</summary>
    public string Type => _layer.Type;

    /// <summary>Gets the layer polarity, or a placeholder when the server does not report it.</summary>
    public string Polarity => string.IsNullOrEmpty(_layer.Polarity) ? "—" : _layer.Polarity;

    /// <summary>Gets the thickness in millimeters, or a placeholder when unavailable.</summary>
    public string Thickness => _layer.Thickness.HasValue
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3} mm", _layer.Thickness)
        : "—";

    /// <summary>Gets the material name, or a placeholder when unavailable.</summary>
    public string Material => string.IsNullOrEmpty(_layer.Material) ? "—" : _layer.Material;

    /// <summary>Gets the display color (server value or type-based default) as a hex string.</summary>
    public string TypeColor => string.IsNullOrEmpty(_layer.ColorHex) ? "#808080" : _layer.ColorHex;

    /// <summary>Gets the drill span text (e.g. "LAYER-1 → LAYER-8"), or empty for non-drill layers.</summary>
    public string DrillSpan =>
        !string.IsNullOrEmpty(_layer.StartLayer) && !string.IsNullOrEmpty(_layer.EndLayer)
            ? $"{_layer.StartLayer} → {_layer.EndLayer}"
            : string.Empty;

    /// <summary>Gets a value indicating whether this layer has a drill span to display.</summary>
    public bool HasDrillSpan => !string.IsNullOrEmpty(DrillSpan);

    /// <summary>
    /// Initializes a new instance of LayerRowViewModel.
    /// </summary>
    public LayerRowViewModel(Layer layer)
    {
        _layer = layer;
    }
}

