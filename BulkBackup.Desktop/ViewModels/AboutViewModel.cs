using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BulkBackup.Desktop.Services;

namespace BulkBackup.Desktop.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updateService;

    [ObservableProperty] private string appVersion = "2.5.3 Enterprise";
    [ObservableProperty] private string framework = ".NET 10.0 Desktop Runtime with Avalonia UI 11 & FluentAvalonia";
    [ObservableProperty] private string developerName = "Yash Nalawde";
    [ObservableProperty] private string developerLinkedIn = "https://www.linkedin.com/in/yash-nalawde";
    [ObservableProperty] private string developerGitHub = "https://github.com/Majordean0303";

    // Auto-Update Management
    [ObservableProperty] private bool isCheckingUpdate;
    [ObservableProperty] private bool isDownloadingUpdate;
    [ObservableProperty] private double downloadProgress;
    [ObservableProperty] private string updateStatusText = string.Empty;
    [ObservableProperty] private bool hasUpdateMessage;
    [ObservableProperty] private bool isUpdateAvailable;
    [ObservableProperty] private string latestVersion = string.Empty;
    [ObservableProperty] private string releaseNotes = string.Empty;
    [ObservableProperty] private string customManifestUrl = string.Empty;

    private UpdateInfo? _latestUpdateInfo;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BulkSwitchBackup",
        "Logs"
    );

    public AboutViewModel(UpdateService updateService)
    {
        _updateService = updateService;
    }

    public AboutViewModel() : this(new UpdateService()) { }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdate || IsDownloadingUpdate) return;

        IsCheckingUpdate = true;
        HasUpdateMessage = true;
        UpdateStatusText = "Checking for new releases...";
        IsUpdateAvailable = false;

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(
                string.IsNullOrWhiteSpace(CustomManifestUrl) ? null : CustomManifestUrl.Trim(),
                CancellationToken.None
            );

            _latestUpdateInfo = result;

            if (result.ErrorMessage != null)
            {
                UpdateStatusText = result.ErrorMessage;
                IsUpdateAvailable = false;
            }
            else if (result.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                LatestVersion = result.LatestVersion;
                ReleaseNotes = result.ReleaseNotes;
                UpdateStatusText = $"New version available: v{result.LatestVersion}!";
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateStatusText = $"Bulk Switch Backup Pro is up to date (Version {result.CurrentVersion}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Update check failed: {ex.Message}";
            IsUpdateAvailable = false;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_latestUpdateInfo == null || !_latestUpdateInfo.IsUpdateAvailable || IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0.0;
        UpdateStatusText = "Downloading update package...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                UpdateStatusText = $"Downloading update: {p:P0}";
            });

            string installerPath = await _updateService.DownloadUpdateAsync(_latestUpdateInfo, progress, CancellationToken.None);

            UpdateStatusText = "Launching update package...";
            _updateService.LaunchInstallerAndExit(installerPath);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Download failed: {ex.Message}";
            IsDownloadingUpdate = false;
        }
    }

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


