using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using OdbDesignInfoClient.Core.ViewModels;

namespace OdbDesignInfoClient.Views;

/// <summary>
/// Code-behind for the Packages tab view.
/// Wires the hierarchical tree grid (Package → component usages): per-row-type cell
/// templates, column sort comparisons and two-way selection synchronization with the ViewModel.
/// </summary>
public partial class PackagesTabView : UserControl
{
    private PackagesTabViewModel? _viewModel;
    private bool _isSyncingSelection;

    /// <summary>
    /// Initializes a new instance of PackagesTabView.
    /// </summary>
    public PackagesTabView()
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

        _viewModel = DataContext as PackagesTabViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncGridSelection();
        }
    }

    /// <summary>
    /// Pushes a grid row selection into the ViewModel. Usage child rows are
    /// ignored: only package rows map to SelectedPackage.
    /// </summary>
    private void OnTreeSelectionChanged(object? sender, TreeDataGridSelectionChangedEventArgs e)
    {
        if (_isSyncingSelection || _viewModel is null)
        {
            return;
        }

        if (e.SelectedItems.FirstOrDefault() is PackageRowViewModel package &&
            !ReferenceEquals(_viewModel.SelectedPackage, package))
        {
            _isSyncingSelection = true;
            try
            {
                _viewModel.SelectedPackage = package;
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
        if (e.PropertyName == nameof(PackagesTabViewModel.SelectedPackage))
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

        var target = _viewModel.SelectedPackage;
        var selection = PackageGrid.RowSelection;
        if (target is null || selection is null || ReferenceEquals(selection.SelectedItem, target))
        {
            return;
        }

        // SelectedIndex addresses the grid's flat row list (roots plus expanded
        // children), not the root ItemsSource — the two spaces only coincide while
        // no row above the target is expanded, so resolve the index via Source.Rows.
        var rowIndex = FindRowIndex(target);
        if (rowIndex < 0)
        {
            return;
        }

        _isSyncingSelection = true;
        try
        {
            selection.SelectedIndex = new IndexPath(rowIndex);
        }
        finally
        {
            _isSyncingSelection = false;
        }

        // Rows realize asynchronously; bring the row into view once layout has settled.
        Dispatcher.UIThread.Post(() => PackageGrid.RowsPresenter?.BringIntoView(rowIndex), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Finds the target row's index in the flat row list (roots plus expanded children)
    /// — the index space <see cref="TreeDataGridRowSelection.SelectedIndex"/> and
    /// <see cref="TreeDataGrid.RowsPresenter"/> address.
    /// </summary>
    private int FindRowIndex(PackageRowViewModel package)
    {
        if (PackageGrid.Source?.Rows is not { } rows)
        {
            return -1;
        }

        for (var i = 0; i < rows.Count; ++i)
        {
            if (ReferenceEquals(rows[i].Model, package))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Assigns the per-row-type cell templates. The tree grid is object-typed, so a
    /// dispatching FuncDataTemplate picks the package or usage DataTemplate for each row.
    /// </summary>
    private void AssignCellTemplates()
    {
        var columns = PackageGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var nameColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var pitchColumn = (TreeDataGridTemplateColumn)columns[1];
        var pinCountColumn = (TreeDataGridTemplateColumn)columns[2];
        var dimensionsColumn = (TreeDataGridTemplateColumn)columns[3];

        nameColumn.CellTemplate = PackageUsageTemplate("PackageNameCell", "UsageRefDesCell");
        pitchColumn.CellTemplate = PackageUsageTemplate("PackagePitchCell", "UsagePartNameCell");
        pinCountColumn.CellTemplate = PackageOnlyTemplate("PackagePinCountCell");
        dimensionsColumn.CellTemplate = PackageOnlyTemplate("PackageDimensionsCell");
    }

    private IDataTemplate PackageUsageTemplate(string packageTemplateKey, string usageTemplateKey)
    {
        var packageTemplate = (IDataTemplate)Resources[packageTemplateKey]!;
        var usageTemplate = (IDataTemplate)Resources[usageTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            (model is PackageUsageRowViewModel ? usageTemplate : packageTemplate).Build(model));
    }

    private IDataTemplate PackageOnlyTemplate(string packageTemplateKey)
    {
        var packageTemplate = (IDataTemplate)Resources[packageTemplateKey]!;
        return new FuncDataTemplate<object>((model, _) =>
            model is PackageRowViewModel package ? packageTemplate.Build(package) : new TextBlock());
    }

    /// <summary>
    /// Assigns sort comparisons per column. Template columns carry no value binding,
    /// so sorting is driven by these type-aware selectors (usage rows sort by their
    /// mapped field or sort to the end for package-only columns).
    /// </summary>
    private void AssignSortComparisons()
    {
        var columns = PackageGrid.ColumnDefinitions;
        var expander = (TreeDataGridHierarchicalExpanderColumn)columns[0];
        var nameColumn = (TreeDataGridTemplateColumn)expander.InnerColumn!;
        var pitchColumn = (TreeDataGridTemplateColumn)columns[1];
        var pinCountColumn = (TreeDataGridTemplateColumn)columns[2];
        var dimensionsColumn = (TreeDataGridTemplateColumn)columns[3];

        SetStringSort(nameColumn, m => m is PackageRowViewModel p ? p.Name : (m as PackageUsageRowViewModel)?.ComponentRefDes);
        // Pitch compares numerically for package rows (formatted text would order
        // 10.000 before 2.000); usage child rows keep their PartName comparison.
        pitchColumn.CompareAscending = (a, b) => ComparePitch(a, b);
        pitchColumn.CompareDescending = (a, b) => ComparePitch(b, a);
        SetNumericSort(pinCountColumn, m => (m as PackageRowViewModel)?.PinCount);
        SetStringSort(dimensionsColumn, m => (m as PackageRowViewModel)?.Dimensions);
    }

    private static int ComparePitch(object? a, object? b) => (a, b) switch
    {
        (PackageRowViewModel x, PackageRowViewModel y) => Nullable.Compare(x.Pitch, y.Pitch),
        (PackageUsageRowViewModel x, PackageUsageRowViewModel y) =>
            string.Compare(x.PartName, y.PartName, StringComparison.OrdinalIgnoreCase),
        (PackageRowViewModel, PackageUsageRowViewModel) => -1,
        (PackageUsageRowViewModel, PackageRowViewModel) => 1,
        _ => 0,
    };

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
