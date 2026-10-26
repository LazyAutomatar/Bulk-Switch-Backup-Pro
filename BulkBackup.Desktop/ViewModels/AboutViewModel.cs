using System;
using System.Collections.ObjectModel;
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
    private readonly IUpdateService _updateService;

    [ObservableProperty] private string appVersion = "3.0.6 Enterprise";
    [ObservableProperty] private string framework = ".NET 10.0 Desktop Runtime with Avalonia UI 11 & FluentAvalonia";
    [ObservableProperty] private string developerName = "Yash Nalawde";
    [ObservableProperty] private string developerLinkedIn = "https://www.linkedin.com/in/yash-nalawde";
    [ObservableProperty] private string developerGitHub = "https://github.com/Majordean0303";

    // Auto-Update Management
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanReviewUpdate))] private bool isCheckingUpdate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanReviewUpdate))] private bool isDownloadingUpdate;
    [ObservableProperty] private double downloadProgress;
    [ObservableProperty] private string updateStatusText = string.Empty;
    [ObservableProperty] private bool hasUpdateMessage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanReviewUpdate))] private bool isUpdateAvailable;
    [ObservableProperty] private string latestVersion = string.Empty;
    [ObservableProperty] private string releaseNotes = string.Empty;
    [ObservableProperty] private string customManifestUrl = string.Empty;
    [ObservableProperty] private bool isReviewingUpdate;
    public bool CanInstallUpdates => _updateService.SupportsInstallation;
    public bool CanReviewUpdate => CanInstallUpdates && IsUpdateAvailable && !IsCheckingUpdate &&
        !IsDownloadingUpdate && !string.IsNullOrWhiteSpace(_latestUpdateInfo?.DownloadUrl);
    public ObservableCollection<ChangelogEntry> ChangelogHistory { get; } = new();

    private UpdateInfo? _latestUpdateInfo;

    private static readonly string LogDirectory = AppLogger.LogDirectory;

    public AboutViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        foreach (var entry in ChangelogService.LoadInstalledHistory()) ChangelogHistory.Add(entry);
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
        IsReviewingUpdate = false;
        _latestUpdateInfo = null;
        LatestVersion = string.Empty;
        ReleaseNotes = string.Empty;

        AppLogger.LogInfo("AboutViewModel", $"Check for updates initiated. CustomManifestUrl: '{CustomManifestUrl}'");

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
                AppLogger.LogWarning("AboutViewModel", $"Update check completed with message: {result.ErrorMessage}");
            }
            else if (result.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                LatestVersion = result.LatestVersion;
                ReleaseNotes = ChangelogService.NotesForVersion(result.LatestVersion, result.ReleaseNotes);
                SyncIncomingVersionToChangelog(result.LatestVersion, ReleaseNotes, result.PublishedAt);
                UpdateStatusText = $"New version available: v{result.LatestVersion}!";
                AppLogger.LogInfo("AboutViewModel", $"Update check: new version available v{result.LatestVersion}");
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateStatusText = $"Bulk Switch Backup Pro is up to date (Version {result.CurrentVersion}).";
                AppLogger.LogInfo("AboutViewModel", $"Update check: up to date (Version {result.CurrentVersion})");
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Update check failed: {ex.Message}";
            IsUpdateAvailable = false;
            AppLogger.LogError("AboutViewModel", "Update check failed in UI", ex);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private void ReviewUpdate()
    {
        if (CanReviewUpdate)
        {
            AppLogger.LogInfo("AboutViewModel", $"User opened Review Update dialog for version v{_latestUpdateInfo?.LatestVersion}");
            IsReviewingUpdate = true;
        }
    }

    [RelayCommand]
    private void CancelUpdateReview()
    {
        AppLogger.LogInfo("AboutViewModel", "User cancelled update review dialog.");
        IsReviewingUpdate = false;
    }

    public void SyncIncomingVersionToChangelog(string version, string notes, DateTime? publishedAt = null)
    {
        string tag = "v" + version.Trim().TrimStart('v', 'V');
        if (tag == "v") return;
        var incoming = new ChangelogEntry(tag, publishedAt?.ToString("yyyy-MM-dd") ?? "Upcoming release",
            ChangelogService.NotesForVersion(version, notes));
        for (int i = 0; i < ChangelogHistory.Count; i++)
        {
            if (!string.Equals(ChangelogHistory[i].VersionTag, tag, StringComparison.OrdinalIgnoreCase)) continue;
            ChangelogHistory[i] = incoming;
            return;
        }
        ChangelogHistory.Insert(0, incoming);
    }

    [RelayCommand]
    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_latestUpdateInfo == null || !_latestUpdateInfo.IsUpdateAvailable || IsDownloadingUpdate ||
            IsCheckingUpdate || !IsReviewingUpdate || !CanInstallUpdates) return;

        IsDownloadingUpdate = true;
        IsReviewingUpdate = false;
        DownloadProgress = 0.0;
        UpdateStatusText = "Downloading update package...";

        AppLogger.LogInfo("AboutViewModel", $"User confirmed Download & Install for version v{_latestUpdateInfo.LatestVersion}");

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                UpdateStatusText = $"Downloading update: {p:P0}";
            });

            string installerPath = await _updateService.DownloadUpdateAsync(_latestUpdateInfo, progress, CancellationToken.None);

            UpdateStatusText = "Launching update package...";
            AppLogger.LogInfo("AboutViewModel", $"Invoking LaunchInstallerAndExit with path '{installerPath}'");
            _updateService.LaunchInstallerAndExit(installerPath);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Download failed: {ex.Message}";
            IsDownloadingUpdate = false;
            AppLogger.LogError("AboutViewModel", "Download & Install update failed", ex);
        }
    }

    [RelayCommand]
    private void OpenReleases()
    {
        try
        {
            AppLogger.LogInfo("AboutViewModel", "Opening releases page in browser.");
            Process.Start(new ProcessStartInfo("https://github.com/" + UpdateService.GitHubRepo + "/releases") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Could not open releases: {ex.Message}";
            AppLogger.LogError("AboutViewModel", "Could not open releases page", ex);
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
            AppLogger.LogInfo("AboutViewModel", $"Opened log directory: {LogDirectory}");
        }
        catch (Exception ex)
        {
            AppLogger.LogError("AboutViewModel", $"Could not open log folder '{LogDirectory}'", ex);
        }
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
