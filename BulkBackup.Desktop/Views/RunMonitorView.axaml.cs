using Avalonia.Controls;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Desktop.Views;

public partial class RunMonitorView : UserControl
{
    public RunMonitorView()
    {
        InitializeComponent();
    }

    public RunMonitorView(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
