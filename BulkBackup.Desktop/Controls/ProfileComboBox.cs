using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace BulkBackup.Desktop.Controls;

/// <summary>Give long device profile names room in the dropdown without widening the sidebar.</summary>
public class ProfileComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    public ProfileComboBox()
    {
        DropDownOpened += (_, _) =>
        {
            var popup = this.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
            if (popup == null) return;
            popup.MinWidth = 520;
            popup.Width = Math.Max(520, Bounds.Width);
        };
    }
}
