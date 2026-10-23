using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace BulkBackup.Desktop.Controls;

public class ResultsDataGrid : DataGrid
{
    protected override Type StyleKeyOverride => typeof(DataGrid);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        bool overHorizontalBar = e.Source is Visual source && source.GetSelfAndVisualAncestors()
            .OfType<ScrollBar>().Any(bar => bar.Orientation == Orientation.Horizontal);
        if (e.Delta.X == 0 && e.Delta.Y != 0 && (overHorizontalBar || e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            var horizontal = new PointerWheelEventArgs(this, e.Pointer, this, e.GetPosition(this), e.Timestamp,
                e.GetCurrentPoint(this).Properties, e.KeyModifiers & ~KeyModifiers.Shift, new Vector(e.Delta.Y, 0));
            base.OnPointerWheelChanged(horizontal);
            e.Handled = horizontal.Handled;
            return;
        }
        base.OnPointerWheelChanged(e);
    }
}
