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

    public int Id => _layer.Id;
    public string Name => _layer.Name;
    public string Type => _layer.Type;
    public string Polarity => _layer.Polarity;
    public double Thickness => _layer.Thickness;
    public string Material => _layer.Material;

    /// <summary>
    /// Gets a display color based on layer type.
    /// </summary>
    public string TypeColor => _layer.Type switch
    {
        "Signal" => "#4CAF50",
        "Power" => "#F44336",
        "Dielectric" => "#FFC107",
        "Drill" => "#9C27B0",
        "SolderMask" => "#2196F3",
        "SilkScreen" => "#FFFFFF",
        _ => "#808080"
    };

    /// <summary>
    /// Initializes a new instance of LayerRowViewModel.
    /// </summary>
    public LayerRowViewModel(Layer layer)
    {
        _layer = layer;
    }
}
