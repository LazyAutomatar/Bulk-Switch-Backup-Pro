using Avalonia.Controls;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Desktop.Views;

public partial class CommandProfilesView : UserControl
{
    public CommandProfilesView()
    {
        InitializeComponent();
    }

    public CommandProfilesView(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
