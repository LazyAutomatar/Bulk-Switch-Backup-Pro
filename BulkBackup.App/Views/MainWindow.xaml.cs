using BulkBackup.App.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace BulkBackup.App.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (e.PropertyName == nameof(MainViewModel.ActivePassword) && ActivePwdBox.Password != vm.ActivePassword)
            {
                ActivePwdBox.Password = vm.ActivePassword;
            }
            else if (e.PropertyName == nameof(MainViewModel.ActiveSecret) && ActiveSecretBox.Password != vm.ActiveSecret)
            {
                ActiveSecretBox.Password = vm.ActiveSecret;
            }
            else if (e.PropertyName == nameof(MainViewModel.NewCredPassword) && string.IsNullOrEmpty(vm.NewCredPassword))
            {
                NewCredPwdBox.Password = string.Empty;
            }
            else if (e.PropertyName == nameof(MainViewModel.NewCredSecret) && string.IsNullOrEmpty(vm.NewCredSecret))
            {
                NewCredSecretBox.Password = string.Empty;
            }
        }

        private void ActivePwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ActivePassword = ActivePwdBox.Password;
            }
        }

        private void ActiveSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ActiveSecret = ActiveSecretBox.Password;
            }
        }

        private void NewCredPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.NewCredPassword = NewCredPwdBox.Password;
            }
        }

        private void NewCredSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.NewCredSecret = NewCredSecretBox.Password;
            }
        }

        private void ConsoleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConsoleTextBox.ScrollToEnd();
        }
    }
}
