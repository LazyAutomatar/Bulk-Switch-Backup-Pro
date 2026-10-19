using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BulkBackup.Desktop.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    [ObservableProperty] private string appVersion = "2.5.0 Enterprise";
    [ObservableProperty] private string framework = ".NET 8.0 Desktop Runtime with Avalonia UI 11 & FluentAvalonia";
    [ObservableProperty] private string developerName = "Yash Nalawde";
    [ObservableProperty] private string developerLinkedIn = "https://www.linkedin.com/in/yash-nalawde";
    [ObservableProperty] private string developerGitHub = "https://github.com/Majordean0303";

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BulkSwitchBackup",
        "Logs"
    );

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = LogDirectory,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenLinkedIn()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DeveloperLinkedIn,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DeveloperGitHub,
                UseShellExecute = true
            });
        }
        catch { }
    }
}
