using Avalonia.Controls;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Desktop.Views;

public partial class CredentialsView : UserControl
{
    public CredentialsView()
    {
        InitializeComponent();
    }

    public CredentialsView(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
