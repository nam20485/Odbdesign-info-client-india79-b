using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OdbDesignInfoClient.Views;

/// <summary>
/// View-layer helper that flashes a grid row after a deep-link navigation
/// (Arch §5.3 step 5): an amber row background fades to transparent over ~1.5 s
/// so the eye finds the target row.
/// The animation itself lives in the app-level styles in App.axaml, keyed by the
/// <see cref="FlashClass"/> style class; this helper attaches that class on demand
/// to the single realized row container. No per-row storyboards are created and
/// virtualized containers never carry the class into a recycled life.
/// </summary>
internal static class RowFlash
{
    /// <summary>
    /// The style class that triggers the flash animation (see App.axaml).
    /// </summary>
    public const string FlashClass = "deep-link-flash";

    // Slightly longer than the 1.5 s animation so the class removal never cuts
    // the fade short; the timer also clears the class if the container is
    // detached (recycled) mid-flash.
    private static readonly TimeSpan FlashHold = TimeSpan.FromSeconds(1.8);

    private static readonly AttachedProperty<DispatcherTimer?> FlashTimerProperty =
        AvaloniaProperty.RegisterAttached<Control, DispatcherTimer?>("FlashTimer", typeof(RowFlash));

    /// <summary>
    /// Flashes the realized TreeDataGrid row presenting <paramref name="model"/>, if any.
    /// Best-effort: rows realize asynchronously after scrolling, so the container
    /// may not exist yet — call after the bring-into-view has settled.
    /// </summary>
    public static void Flash(TreeDataGrid? grid, object? model)
    {
        if (grid?.RowsPresenter is null || model is null)
        {
            return;
        }

        foreach (var element in grid.RowsPresenter.GetRealizedElements())
        {
            if (element is TreeDataGridRow { Model: { } rowModel } && ReferenceEquals(rowModel, model))
            {
                Flash(element);
                return;
            }
        }
    }

    /// <summary>
    /// Scrolls <paramref name="item"/> into a flat DataGrid and flashes its row, if any.
    /// Best-effort: the container must be realized after the scroll.
    /// </summary>
    public static void Flash(DataGrid? grid, object? item)
    {
        if (grid is null || item is null)
        {
            return;
        }

        grid.ScrollIntoView(item, null);

        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            if (ReferenceEquals(row.DataContext, item))
            {
                Flash(row);
                return;
            }
        }
    }

    private static void Flash(Control row)
    {
        // Restart cleanly when the same container flashes again before the
        // previous fade has finished: stop the pending class removal so it
        // cannot cut the new flash short.
        if (row.GetValue(FlashTimerProperty) is { } previous)
        {
            previous.Stop();
        }

        row.DetachedFromVisualTree -= OnRowDetached;
        row.Classes.Remove(FlashClass);

        var timer = new DispatcherTimer { Interval = FlashHold };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            row.SetValue(FlashTimerProperty, null);
            row.DetachedFromVisualTree -= OnRowDetached;
            row.Classes.Remove(FlashClass);
        };
        row.SetValue(FlashTimerProperty, timer);
        row.DetachedFromVisualTree += OnRowDetached;

        row.Classes.Add(FlashClass);
        timer.Start();
    }

    /// <summary>
    /// Virtualized grids recycle row containers: never let the flash class (or a
    /// pending removal timer) outlive a container that now presents another row.
    /// </summary>
    private static void OnRowDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control row)
        {
            return;
        }

        row.DetachedFromVisualTree -= OnRowDetached;
        if (row.GetValue(FlashTimerProperty) is { } timer)
        {
            timer.Stop();
            row.SetValue(FlashTimerProperty, null);
        }

        row.Classes.Remove(FlashClass);
    }
}
