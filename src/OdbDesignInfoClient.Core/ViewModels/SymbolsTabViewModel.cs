using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OdbDesignInfoClient.Core.Models;
using OdbDesignInfoClient.Core.Services.Interfaces;

namespace OdbDesignInfoClient.Core.ViewModels;

/// <summary>
/// ViewModel for the Symbols Library tab: the symbol names of the current
/// design's symbols library. The server projection carries names only.
/// </summary>
public partial class SymbolsTabViewModel : ViewModelBase
{
    private readonly IDesignService _designService;

    [ObservableProperty]
    private ObservableCollection<SymbolRowViewModel> _symbols = [];

    [ObservableProperty]
    private SymbolRowViewModel? _selectedSymbol;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private bool _isEmpty = true;

    /// <summary>
    /// Gets the honest empty-state message shown when the design has no symbols.
    /// </summary>
    public string EmptyMessage => "No symbols found for this design. The server's symbols "
        + "projection lists symbol names only — no dimensions or usage data is available.";

    private List<SymbolRowViewModel> _allSymbols = [];
    private string? _currentDesignId;
    private string? _loadedDesignId;
    private CancellationTokenSource? _filterDebounceCts;

    /// <summary>
    /// Initializes a new instance of SymbolsTabViewModel.
    /// </summary>
    public SymbolsTabViewModel(IDesignService designService)
    {
        _designService = designService;
    }

    /// <summary>
    /// Loads symbols for the specified design. Symbol data is design-scoped
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
            var symbols = await _designService.GetSymbolsAsync(designId, cancellationToken);
            _allSymbols = symbols.Select(s => new SymbolRowViewModel(s)).ToList();
            TotalCount = _allSymbols.Count;
            ApplyFilter();
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
            SetError($"Failed to load symbols: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the symbols data.
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
    /// Clears the filter immediately, bypassing the input debounce.
    /// </summary>
    [RelayCommand]
    private void ClearFilter()
    {
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
        var filtered = string.IsNullOrWhiteSpace(FilterText)
            ? _allSymbols
            : _allSymbols.Where(s => s.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase)).ToList();

        Symbols = new ObservableCollection<SymbolRowViewModel>(filtered);
        FilteredCount = Symbols.Count;
        IsEmpty = _allSymbols.Count == 0;
    }
}

/// <summary>
/// Row ViewModel for one library symbol.
/// </summary>
public partial class SymbolRowViewModel : ObservableObject
{
    private readonly SymbolSummary _symbol;

    /// <summary>Gets the symbol name.</summary>
    public string Name => _symbol.Name;

    /// <summary>
    /// Initializes a new instance of SymbolRowViewModel.
    /// </summary>
    public SymbolRowViewModel(SymbolSummary symbol)
    {
        _symbol = symbol;
    }
}
