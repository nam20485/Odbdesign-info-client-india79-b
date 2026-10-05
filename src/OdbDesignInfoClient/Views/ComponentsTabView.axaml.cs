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
/// Code-behind for the Components tab view.
/// Wires the hierarchical tree grid (Component → Pins): per-row-type cell templates,
/// column sort comparisons and two-way selection synchronization with the ViewModel.
/// </summary>
public partial class ComponentsTabView : UserControl
{
    private ComponentsTabViewModel? _viewModel;
    private bool _isSyncingSelection;

    /// <summary>
    /// Initializes a new instance of ComponentsTabView.
    /// </summary>
    public ComponentsTabView()
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

        _viewModel = DataContext as ComponentsTabViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncGridSelection();
        }
    }

    /// <summary>
    /// Pushes a grid row selection into the ViewModel (fires cross-probing).
    /// Pin child rows are ignored: only component rows map to SelectedComponent.
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, TreeDataGridSelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || _viewModel is null)
        {
            return;
        }

        if (e.SelectedItems.FirstOrDefault() is ComponentRowViewModel component &&
            !ReferenceEquals(_viewModel.SelectedComponent, component))
        {
            _isSyncingSelection = true;
            try
            {
                _viewModel.SelectedComponent = component;
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }

    /// <summary>
    /// Pushes a programmatic ViewModel selection (e.g. NavigateToComponent) into the grid,
    /// then scrolls the row into view and flashes it so the eye finds the deep-link target.
    /// Guarded against grid → ViewModel → grid feedback loops.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComponentsTabViewModel.SelectedComponent))
        {
            // User clicks reach here too, but only after the grid already selected the
            // row — SyncGridSelection then early-returns, so only VM-first selections
            // (deep-links) select, scroll and flash.
            SyncGridSelection(flash: true);
        }
    }

    private void SyncGridSelection(bool flash = false)
    {
        if (_isSyncingSelection || _viewModel is null)
        {
            return;
        }

        var target = _viewModel.SelectedComponent;
        var selection = ComponentGrid.RowSelection;
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

        // Rows realize asynchronously; bring the row into view once layout has
        // settled, then (for deep-links) flash it once the scrolled-to container
        // has been realized — one layout pass after the bring-into-view.
        Dispatcher.UIThread.Post(() =>
        {
            BringModelIntoView(target);
            if (flash)
            {
                Dispatcher.UIThread.Post(() => RowFlash.Flash(ComponentGrid, target), DispatcherPriority.Background);
            }
        }, DispatcherPriority.Loaded);
    }

    private int FindModelIndex(ComponentRowViewModel component)
    {
        if (ComponentGrid.ItemsSource is not IList items)
        {
            return -1;
        }

        for (var i = 0; i < items.Count; ++i)
        {
            if (ReferenceEquals(items[i], component))
            {
                return i;
            }
        }

        return -1;
    }

    private void BringModelIntoView(ComponentRowViewModel component)
    {
        if (ComponentGrid.Source?.Rows is not { } rows)
        {
            return;
        }

        for (var i = 0; i < rows.Count; ++i)
        {
            if (ReferenceEquals(rows[i].Model, component))
            {
                ComponentGrid.RowsPresenter?.BringIntoView(i);
                return;
            }
        }
    }

    /// <summary>
    /// Assigns the per-row-type cell templates. The tree grid is object-typed, so a
    /// dispatching FuncDataTemplate picks the component or pin DataTemplate for each row.
    /// </summary>
    private void AssignCellTemplates()
    {
        var columns = ComponentGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var refDesColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var partNameColumn = (TreeDataGridTemplateColumn)columns[1];
        var packageColumn = (TreeDataGridTemplateColumn)columns[2];
        var sideColumn = (TreeDataGridTemplateColumn)columns[3];
        var rotationColumn = (TreeDataGridTemplateColumn)columns[4];
        var xColumn = (TreeDataGridTemplateColumn)columns[5];
        var yColumn = (TreeDataGridTemplateColumn)columns[6];

        refDesColumn.CellTemplate = ComponentPinTemplate("ComponentRefDesCell", "PinRefDesCell");
        partNameColumn.CellTemplate = ComponentPinTemplate("ComponentPartNameCell", "PinNetCell");
        packageColumn.CellTemplate = ComponentPinTemplate("ComponentPackageCell", "PinElectricalTypeCell");
        sideColumn.CellTemplate = ComponentOnlyTemplate("ComponentSideCell");
        rotationColumn.CellTemplate = ComponentOnlyTemplate("ComponentRotationCell");
        xColumn.CellTemplate = ComponentOnlyTemplate("ComponentXCell");
        yColumn.CellTemplate = ComponentOnlyTemplate("ComponentYCell");
    }

    private IDataTemplate ComponentPinTemplate(string componentTemplateKey, string pinTemplateKey)
    {
        var componentTemplate = (IDataTemplate)Resources[componentTemplateKey]!;
        var pinTemplate = (IDataTemplate)Resources[pinTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            (model is PinRowViewModel ? pinTemplate : componentTemplate).Build(model));
    }

    private IDataTemplate ComponentOnlyTemplate(string componentTemplateKey)
    {
        var componentTemplate = (IDataTemplate)Resources[componentTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            model is ComponentRowViewModel component ? componentTemplate.Build(component) : new TextBlock());
    }

    /// <summary>
    /// Assigns sort comparisons per column. Template columns carry no value binding,
    /// so sorting is driven by these type-aware selectors (pin rows sort by their
    /// mapped field or sort to the end for component-only columns).
    /// </summary>
    private void AssignSortComparisons()
    {
        var columns = ComponentGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var refDesColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var partNameColumn = (TreeDataGridTemplateColumn)columns[1];
        var packageColumn = (TreeDataGridTemplateColumn)columns[2];
        var sideColumn = (TreeDataGridTemplateColumn)columns[3];
        var rotationColumn = (TreeDataGridTemplateColumn)columns[4];
        var xColumn = (TreeDataGridTemplateColumn)columns[5];
        var yColumn = (TreeDataGridTemplateColumn)columns[6];

        SetStringSort(refDesColumn, m => m is ComponentRowViewModel c ? c.RefDes : (m as PinRowViewModel)?.Name);
        SetStringSort(partNameColumn, m => m is ComponentRowViewModel c ? c.PartName : (m as PinRowViewModel)?.NetName);
        SetStringSort(packageColumn, m => m is ComponentRowViewModel c ? c.Package : (m as PinRowViewModel)?.ElectricalType);
        SetStringSort(sideColumn, m => (m as ComponentRowViewModel)?.Side);
        SetNumericSort(rotationColumn, m => (m as ComponentRowViewModel)?.Rotation);
        SetNumericSort(xColumn, m => (m as ComponentRowViewModel)?.X);
        SetNumericSort(yColumn, m => (m as ComponentRowViewModel)?.Y);
    }

    private static void SetStringSort(TreeDataGridColumn column, Func<object?, string?> selector)
    {
        column.CompareAscending = (a, b) => string.Compare(selector(a), selector(b), StringComparison.OrdinalIgnoreCase);
        column.CompareDescending = (a, b) => string.Compare(selector(b), selector(a), StringComparison.OrdinalIgnoreCase);
    }

    private static void SetNumericSort(TreeDataGridColumn column, Func<object?, double?> selector)
    {
        column.CompareAscending = (a, b) => Nullable.Compare(selector(a), selector(b));
        column.CompareDescending = (a, b) => Nullable.Compare(selector(b), selector(a));
    }
}
