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
/// Code-behind for the Nets tab view.
/// Wires the hierarchical tree grid (Net → Features): per-row-type cell templates,
/// column sort comparisons and two-way selection synchronization with the ViewModel.
/// </summary>
public partial class NetsTabView : UserControl
{
    private NetsTabViewModel? _viewModel;
    private bool _isSyncingSelection;

    /// <summary>
    /// Initializes a new instance of NetsTabView.
    /// </summary>
    public NetsTabView()
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

        _viewModel = DataContext as NetsTabViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncGridSelection();
        }
    }

    /// <summary>
    /// Pushes a grid row selection into the ViewModel (fires cross-probing).
    /// Feature child rows are ignored: only net rows map to SelectedNet.
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, TreeDataGridSelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || _viewModel is null)
        {
            return;
        }

        if (e.SelectedItems.FirstOrDefault() is NetRowViewModel net &&
            !ReferenceEquals(_viewModel.SelectedNet, net))
        {
            _isSyncingSelection = true;
            try
            {
                _viewModel.SelectedNet = net;
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    /// <summary>
    /// Pushes a programmatic ViewModel selection (e.g. NavigateToNet) into the grid,
    /// then scrolls the row into view. Guarded against grid → ViewModel → grid feedback loops.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetsTabViewModel.SelectedNet))
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

        var target = _viewModel.SelectedNet;
        var selection = NetGrid.RowSelection;
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

    private int FindModelIndex(NetRowViewModel net)
    {
        if (NetGrid.ItemsSource is not IList items)
        {
            return -1;
        }

        for (var i = 0; i < items.Count; ++i)
        {
            if (ReferenceEquals(items[i], net))
            {
                return i;
            }
        }

        return -1;
    }

    private void BringModelIntoView(NetRowViewModel net)
    {
        if (NetGrid.Source?.Rows is not { } rows)
        {
            return;
        }

        for (var i = 0; i < rows.Count; ++i)
        {
            if (ReferenceEquals(rows[i].Model, net))
            {
                NetGrid.RowsPresenter?.BringIntoView(i);
                return;
            }
        }
    }

    /// <summary>
    /// Assigns the per-row-type cell templates. The tree grid is object-typed, so a
    /// dispatching FuncDataTemplate picks the net or feature DataTemplate for each row.
    /// </summary>
    private void AssignCellTemplates()
    {
        var columns = NetGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var nameColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var pinCountColumn = (TreeDataGridTemplateColumn)columns[1];
        var viaCountColumn = (TreeDataGridTemplateColumn)columns[2];

        nameColumn.CellTemplate = NetFeatureTemplate("NetNameCell", "FeatureIdCell");
        pinCountColumn.CellTemplate = NetFeatureTemplate("NetPinCountCell", "FeatureTypeCell");
        viaCountColumn.CellTemplate = NetFeatureTemplate("NetViaCountCell", "FeatureComponentCell");
    }

    private IDataTemplate NetFeatureTemplate(string netTemplateKey, string featureTemplateKey)
    {
        var netTemplate = (IDataTemplate)Resources[netTemplateKey]!;
        var featureTemplate = (IDataTemplate)Resources[featureTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            (model is NetFeatureRowViewModel ? featureTemplate : netTemplate).Build(model));
    }

    /// <summary>
    /// Assigns sort comparisons per column. Template columns carry no value binding,
    /// so sorting is driven by these type-aware selectors (feature rows sort by their
    /// mapped field or sort to the end for net-only numeric columns).
    /// </summary>
    private void AssignSortComparisons()
    {
        var columns = NetGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var nameColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var pinCountColumn = (TreeDataGridTemplateColumn)columns[1];
        var viaCountColumn = (TreeDataGridTemplateColumn)columns[2];

        SetStringSort(nameColumn, m => m is NetRowViewModel n ? n.Name : (m as NetFeatureRowViewModel)?.Id);
        SetNumericSort(pinCountColumn, m => (m as NetRowViewModel)?.PinCount);
        SetStringSort(viaCountColumn, m =>
            m is NetRowViewModel n ? n.ViaCount.ToString() : (m as NetFeatureRowViewModel)?.ComponentRef);
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
