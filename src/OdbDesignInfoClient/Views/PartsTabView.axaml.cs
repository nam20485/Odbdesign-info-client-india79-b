using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using OdbDesignInfoClient.Core.ViewModels;

namespace OdbDesignInfoClient.Views;

/// <summary>
/// Code-behind for the Parts tab view.
/// Wires the hierarchical tree grid (Part → component usages): per-row-type cell
/// templates, column sort comparisons and two-way selection synchronization with the ViewModel.
/// </summary>
public partial class PartsTabView : UserControl
{
    private PartsTabViewModel? _viewModel;
    private bool _isSyncingSelection;

    /// <summary>
    /// Initializes a new instance of PartsTabView.
    /// </summary>
    public PartsTabView()
    {
        InitializeComponent();
        AssignCellTemplates();
        AssignSortComparisons();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as PartsTabViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncGridSelection();
        }
    }

    /// <summary>
    /// Pushes a grid row selection into the ViewModel. Usage child rows are
    /// ignored: only part rows map to SelectedPart.
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, TreeDataGridSelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || _viewModel is null)
        {
            return;
        }

        if (e.SelectedItems.FirstOrDefault() is PartRowViewModel part &&
            !ReferenceEquals(_viewModel.SelectedPart, part))
        {
            _isSyncingSelection = true;
            try
            {
                _viewModel.SelectedPart = part;
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    /// <summary>
    /// Pushes a programmatic ViewModel selection into the grid, then scrolls the
    /// row into view. Guarded against grid → ViewModel → grid feedback loops.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PartsTabViewModel.SelectedPart))
        {
            SyncGridSelection();
        }
    }

    private void SyncGridSelection()
    {
        if (_isSyncingSelection || _viewModel is null)
        {
            return;
        }

        var target = _viewModel.SelectedPart;
        var selection = PartGrid.RowSelection;
        if (target is null || selection is null || ReferenceEquals(selection.SelectedItem, target))
        {
            return;
        }

        var modelIndex = FindModelIndex(target);
        if (modelIndex < 0)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            selection.SelectedIndex = new IndexPath(modelIndex);
        }
        finally
        {
            _isSyncingSelection = false;
        }

        // Rows realize asynchronously; bring the row into view once layout has settled.
        Dispatcher.UIThread.Post(() => BringModelIntoView(target), DispatcherPriority.Loaded);
    }

    private int FindModelIndex(PartRowViewModel part)
    {
        if (PartGrid.ItemsSource is not IList items)
        {
            return -1;
        }

        for (var i = 0; i < items.Count; ++i)
        {
            if (ReferenceEquals(items[i], part))
            {
                return i;
            }
        }

        return -1;
    }

    private void BringModelIntoView(PartRowViewModel part)
    {
        if (PartGrid.Source?.Rows is not { } rows)
        {
            return;
        }

        for (var i = 0; i < rows.Count; ++i)
        {
            if (ReferenceEquals(rows[i].Model, part))
            {
                PartGrid.RowsPresenter?.BringIntoView(i);
                return;
            }
        }
    }

    /// <summary>
    /// Assigns the per-row-type cell templates. The tree grid is object-typed, so a
    /// dispatching FuncDataTemplate picks the part or usage DataTemplate for each row.
    /// </summary>
    private void AssignCellTemplates()
    {
        var columns = PartGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var partNumberColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var manufacturerColumn = (TreeDataGridTemplateColumn)columns[1];
        var descriptionColumn = (TreeDataGridTemplateColumn)columns[2];
        var usageCountColumn = (TreeDataGridTemplateColumn)columns[3];

        partNumberColumn.CellTemplate = PartUsageTemplate("PartNumberCell", "UsageRefDesCell");
        manufacturerColumn.CellTemplate = PartOnlyTemplate("PartManufacturerCell");
        descriptionColumn.CellTemplate = PartOnlyTemplate("PartDescriptionCell");
        usageCountColumn.CellTemplate = PartOnlyTemplate("PartUsageCountCell");
    }

    private IDataTemplate PartUsageTemplate(string partTemplateKey, string usageTemplateKey)
    {
        var partTemplate = (IDataTemplate)Resources[partTemplateKey]!;
        var usageTemplate = (IDataTemplate)Resources[usageTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            (model is PartUsageRowViewModel ? usageTemplate : partTemplate).Build(model));
    }

    private IDataTemplate PartOnlyTemplate(string partTemplateKey)
    {
        var partTemplate = (IDataTemplate)Resources[partTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            model is PartRowViewModel part ? partTemplate.Build(part) : new TextBlock());
    }

    /// <summary>
    /// Assigns sort comparisons per column. Template columns carry no value binding,
    /// so sorting is driven by these type-aware selectors (usage rows sort to the
    /// end for part-only columns).
    /// </summary>
    private void AssignSortComparisons()
    {
        var columns = PartGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var partNumberColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var manufacturerColumn = (TreeDataGridTemplateColumn)columns[1];
        var descriptionColumn = (TreeDataGridTemplateColumn)columns[2];
        var usageCountColumn = (TreeDataGridTemplateColumn)columns[3];

        SetStringSort(partNumberColumn, m => m is PartRowViewModel p ? p.PartNumber : (m as PartUsageRowViewModel)?.ComponentRefDes);
        SetStringSort(manufacturerColumn, m => (m as PartRowViewModel)?.Manufacturer);
        SetStringSort(descriptionColumn, m => (m as PartRowViewModel)?.Description);
        SetNumericSort(usageCountColumn, m => (m as PartRowViewModel)?.UsageCount);
    }

    private static void SetStringSort(TreeDataGridColumn column, Func<object?, string?> selector)
    {
        column.CompareAscending = (a, b) => string.Compare(selector(a), selector(b), StringComparison.OrdinalIgnoreCase);
        column.CompareDescending = (a, b) => string.Compare(selector(b), selector(a), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetNumericSort(TreeDataGridColumn column, Func<object?, int?> selector)
    {
        column.CompareAscending = (a, b) => Nullable.Compare(selector(a), selector(b));
        column.CompareDescending = (a, b) => Nullable.Compare(selector(b), selector(a));
    }
}
