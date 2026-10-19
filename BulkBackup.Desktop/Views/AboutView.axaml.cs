using Avalonia.Controls;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Desktop.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    public AboutView(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
