using Avalonia.Controls;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Desktop.Views;

public partial class CommandResultsView : UserControl
{
    public CommandResultsView() => InitializeComponent();
    public CommandResultsView(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
