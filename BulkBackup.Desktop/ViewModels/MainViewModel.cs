using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;
using BulkBackup.Desktop.Services;

namespace BulkBackup.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CredentialStore _credStore;
    private readonly IDialogService _dialogService;
    private readonly AppState _appState;
    private CancellationTokenSource? _cts;

    // Application & Developer Details
    [ObservableProperty] private string appVersion = "2.5.0 Enterprise";
    [ObservableProperty] private string framework = ".NET 8.0 Desktop Runtime with Avalonia UI 11 & FluentAvalonia";
    [ObservableProperty] private string developerName = "Yash Nalawde";
    [ObservableProperty] private string developerLinkedIn = "https://www.linkedin.com/in/yash-nalawde";
    [ObservableProperty] private string developerGitHub = "https://github.com/Majordean0303";

    // Active Connection / Direct SSH inputs
    [ObservableProperty] private string activeUsername = string.Empty;
    [ObservableProperty] private string activePassword = string.Empty;
    [ObservableProperty] private string activeSecret = string.Empty;
    [ObservableProperty] private bool useVaultAsFallback = true;

    // Target IPs
    [ObservableProperty] private string targetIps = string.Empty;
    [ObservableProperty] private int targetIpsCount = 0;

    // Discovery & Traversal
    [ObservableProperty] private bool enableCdp = false;
    [ObservableProperty] private int filterTypeIndex = 0; // 0=Subnet, 1=VLAN ID
    [ObservableProperty] private string filterValue = string.Empty;
    [ObservableProperty] private bool includeAps = false;

    // Execution Tuning
    [ObservableProperty] private int maxConcurrency = Math.Clamp(Environment.ProcessorCount * 2, 6, 16);
    [ObservableProperty] private bool writeConfig = true;
    [ObservableProperty] private string backupDirectory = "Temporary Session Staging";

    // Profiles & Commands
    [ObservableProperty] private int selectedProfileIndex = 0;
    [ObservableProperty] private string customCommandsText = string.Empty;
    [ObservableProperty] private int commandCount = 56;

    // Status & Metrics
    [ObservableProperty] private bool isRunning = false;
    [ObservableProperty] private string runningStatusSummary = "Ready";
    [ObservableProperty] private bool hasCompletedBackups = false;
    [ObservableProperty] private double overallProgress = 0;
    [ObservableProperty] private int totalDevicesCount = 0;
    [ObservableProperty] private int inProgressDevicesCount = 0;
    [ObservableProperty] private int completedDevicesCount = 0;
    [ObservableProperty] private int failedDevicesCount = 0;

    [ObservableProperty] private string lastBackupDirectory = string.Empty;

    // Session Telemetry & Runtime Elapsed Tracking
    [ObservableProperty] private bool showSessionBanner = false;
    [ObservableProperty] private string sessionElapsedTime = "00:00";
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly DispatcherTimer _sessionTimer = new();

    // Collection Properties
    public ObservableCollection<DeviceModel> Devices { get; } = new();
    public ObservableCollection<CredentialEntry> StoredCredentials { get; } = new();
    [ObservableProperty] private bool hasStoredCredentials = false;

    // Form Inputs for Adding Credentials
    [ObservableProperty] private string newCredProfileName = string.Empty;
    [ObservableProperty] private string newCredUsername = string.Empty;
    [ObservableProperty] private string newCredPassword = string.Empty;
    [ObservableProperty] private string newCredSecret = string.Empty;

    // Live Execution Console Stream
    [ObservableProperty] private string logs = string.Empty;

    public MainViewModel(CredentialStore credStore, IDialogService dialogService, AppState appState)
    {
        _credStore = credStore;
        _dialogService = dialogService;
        _appState = appState;

        RefreshStoredCredentials();
        LoadProfileCommands(0);

        _sessionTimer.Interval = TimeSpan.FromSeconds(1);
        _sessionTimer.Tick += (s, e) =>
        {
            if (_sessionStopwatch.IsRunning)
            {
                var ts = _sessionStopwatch.Elapsed;
                SessionElapsedTime = ts.Hours > 0 ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        };

        AppendLog("System", "Bulk Switch Backup Pro v2.5.0 Enterprise ready. Powered by Avalonia UI 11.");
    }

    public void RefreshStoredCredentials()
    {
        StoredCredentials.Clear();
        foreach (var cred in _credStore.LoadAllCredentials())
        {
            StoredCredentials.Add(cred);
        }
        HasStoredCredentials = StoredCredentials.Count > 0;
    }

    partial void OnTargetIpsChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            TargetIpsCount = 0;
            return;
        }
        var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.Trim())
                         .Where(l => !string.IsNullOrEmpty(l))
                         .Distinct()
                         .ToList();
        TargetIpsCount = lines.Count;
    }

    partial void OnSelectedProfileIndexChanged(int value)
    {
        LoadProfileCommands(value);
    }

    partial void OnCustomCommandsTextChanged(string value)
    {
        UpdateCommandCount();
    }

    private void LoadProfileCommands(int profileIndex)
    {
        switch (profileIndex)
        {
            case 0:
                CustomCommandsText = string.Join(Environment.NewLine, CommandProfiles.CiscoIosCommands);
                break;
            case 1:
                CustomCommandsText = string.Join(Environment.NewLine, CommandProfiles.CiscoNexusCommands);
                break;
            case 2:
                CustomCommandsText = string.Join(Environment.NewLine, CommandProfiles.CiscoWlcCommands);
                break;
            case 3:
                CustomCommandsText = string.Join(Environment.NewLine, CommandProfiles.CiscoAireOsWlcCommands);
                break;
            case 4:
                CustomCommandsText = string.Join(Environment.NewLine, CommandProfiles.CustomTemplateCommands);
                break;
        }
        UpdateCommandCount();
    }

    private void UpdateCommandCount()
    {
        if (string.IsNullOrWhiteSpace(CustomCommandsText))
        {
            CommandCount = 0;
            return;
        }

        var lines = CustomCommandsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(l => l.Trim())
                                      .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("!"))
                                      .ToList();
        CommandCount = lines.Count;
    }

    [RelayCommand]
    private void ResetProfileCommands()
    {
        LoadProfileCommands(SelectedProfileIndex);
        AppendLog("System", "CLI commands reset to default profile template.");
    }

    [RelayCommand]
    private void ClearCommands()
    {
        CustomCommandsText = "! Enter custom commands, one per line" + Environment.NewLine;
        UpdateCommandCount();
        AppendLog("System", "Custom CLI commands cleared.");
    }

    [RelayCommand]
    private async Task SaveNewCredentialAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCredProfileName) || string.IsNullOrWhiteSpace(NewCredUsername) || string.IsNullOrWhiteSpace(NewCredPassword))
        {
            await _dialogService.ShowMessageAsync("Validation Warning", "Please enter Profile Name, Username, and Password.");
            return;
        }

        try
        {
            _credStore.SaveCredential(NewCredProfileName.Trim(), NewCredUsername.Trim(), NewCredPassword, NewCredSecret.Trim());
            RefreshStoredCredentials();
            AppendLog("Security", $"Saved credential profile '{NewCredProfileName}'.");

            NewCredProfileName = string.Empty;
            NewCredUsername = string.Empty;
            NewCredPassword = string.Empty;
            NewCredSecret = string.Empty;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to save credential: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteStoredCredential(CredentialEntry? entry)
    {
        if (entry == null) return;
        try
        {
            _credStore.DeleteCredential(entry.Name);
            RefreshStoredCredentials();
            AppendLog("Security", $"Deleted credential profile '{entry.Name}'.");
        }
        catch (Exception ex)
        {
            AppendLog("Error", $"Failed to delete credential: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ApplyCredentialToActive(CredentialEntry? entry)
    {
        if (entry == null) return;
        ActiveUsername = entry.Username;
        ActivePassword = entry.Password;
        ActiveSecret = entry.Secret;
        AppendLog("Security", $"Loaded credential profile '{entry.Name}' into session.");
    }

    [RelayCommand]
    private async Task ImportDevicesAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync("Select Target Devices File", "Text & CSV Files", "txt", "csv");
        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                string content = await File.ReadAllTextAsync(filePath);
                var matches = Regex.Matches(content, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
                var ips = matches.Select(m => m.Value).Distinct().ToList();

                if (ips.Any())
                {
                    TargetIps = string.Join(Environment.NewLine, ips);
                    AppendLog("System", $"Loaded {ips.Count} IP addresses from '{Path.GetFileName(filePath)}'.");
                }
                else
                {
                    AppendLog("Error", "No valid IP addresses found in the selected file.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Error", $"Failed to read file: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task BrowseBackupFolderAsync()
    {
        var folder = await _dialogService.ShowOpenFolderDialogAsync("Select Backup Storage Directory");
        if (!string.IsNullOrEmpty(folder))
        {
            BackupDirectory = folder;
            AppendLog("System", $"Backup destination directory updated: {BackupDirectory}");
        }
    }

    [RelayCommand]
    private async Task StartBackupAsync()
    {
        if (IsRunning) return;

        var candidateCreds = new List<CredentialEntry>();

        if (!string.IsNullOrWhiteSpace(ActiveUsername) && !string.IsNullOrEmpty(ActivePassword))
        {
            candidateCreds.Add(new CredentialEntry
            {
                Name = "Session Credentials",
                Username = ActiveUsername.Trim(),
                Password = ActivePassword,
                Secret = ActiveSecret.Trim()
            });
        }

        if (UseVaultAsFallback)
        {
            foreach (var stored in StoredCredentials)
            {
                if (!candidateCreds.Any(c => c.Username.Equals(stored.Username, StringComparison.OrdinalIgnoreCase) && c.Password == stored.Password))
                {
                    candidateCreds.Add(stored);
                }
            }
        }

        if (candidateCreds.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Missing Credentials", "Please enter an SSH Username and Password to proceed.");
            AppendLog("Security", "Execution halted: No SSH credentials provided.");
            return;
        }

        var ips = TargetIps.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(ip => ip.Trim())
                           .Where(ip => !string.IsNullOrEmpty(ip))
                           .Distinct()
                           .ToList();

        if (!ips.Any())
        {
            await _dialogService.ShowMessageAsync("Validation Warning", "Please enter at least one target device IP address.");
            AppendLog("System", "Execution halted: No target IPs entered.");
            return;
        }

        var commandsList = CustomCommandsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(c => c.Trim())
                                             .Where(c => !string.IsNullOrEmpty(c) && !c.StartsWith("!"))
                                             .ToList();

        if (!commandsList.Any())
        {
            await _dialogService.ShowMessageAsync("Validation Warning", "The command list is empty. Please configure at least one CLI command.");
            AppendLog("System", "Execution halted: No CLI commands configured.");
            return;
        }

        string sessionDir = TempSessionManager.CreateNewSession();
        BackupDirectory = sessionDir;
        LastBackupDirectory = sessionDir;

        string profileTitle = SelectedProfileIndex switch
        {
            0 => "Cisco Switch (IOS / IOS-XE)",
            1 => "Cisco Nexus Switch (NX-OS)",
            2 => "Cisco Catalyst 9800 WLC (IOS-XE)",
            3 => "Cisco AireOS WLC (5520 / 8540 / 3504)",
            _ => "Custom Profile"
        };

        Devices.Clear();
        foreach (var ip in ips)
        {
            Devices.Add(new DeviceModel
            {
                IpAddress = ip,
                Hostname = ip,
                Status = "Queued",
                Protocol = "Waiting...",
                ProtocolColor = "#888888",
                StatusColor = "#2D2D32",
                IsInProgress = false,
                IsCompleted = false,
                IsFailed = false
            });
        }

        UpdateMetrics();
        IsRunning = true;
        ShowSessionBanner = true;
        _sessionStopwatch.Restart();
        _sessionTimer.Start();
        SessionElapsedTime = "00:00";
        RunningStatusSummary = $"Starting backup across {ips.Count} target(s)...";
        _appState.UpdateRunningStatus(true, OverallProgress, RunningStatusSummary);
        _cts = new CancellationTokenSource();

        AppendLog("System", $"Starting bulk backup with profile '{profileTitle}' across {ips.Count} target(s) ({commandsList.Count} commands each)...");
        AppendLog("System", $"Isolated temporary session staging: {BackupDirectory}");
        if (candidateCreds.Count > 1)
        {
            AppendLog("Security", $"Credential failover pool: {candidateCreds.Count} candidate credential(s) active.");
        }
        if (EnableCdp)
        {
            AppendLog("Discovery", "CDP/LLDP Discovery active. Cisco IP Phones and ATAs are automatically excluded.");
        }

        var engine = new NetworkEngine();

        try
        {
            string resultDir = await Task.Run(async () =>
            {
                return await engine.StartEngineAsync(
                    ips,
                    candidateCreds,
                    EnableCdp,
                    FilterTypeIndex,
                    FilterValue,
                    IncludeAps,
                    MaxConcurrency,
                    commandsList,
                    profileTitle,
                    WriteConfig,
                    BackupDirectory,
                    (source, message) => Dispatcher.UIThread.Post(() => AppendLog(source, message)),
                    (deviceIp, pct, stat, proto, hostname, isAp, uplink, port) => Dispatcher.UIThread.Post(() =>
                    {
                        var dev = Devices.FirstOrDefault(d => d.IpAddress == deviceIp || (!string.IsNullOrEmpty(hostname) && HostKey.Same(d.Hostname, hostname)));
                        if (dev == null)
                        {
                            dev = new DeviceModel
                            {
                                IpAddress = deviceIp,
                                Hostname = hostname ?? deviceIp,
                                DeviceType = isAp ? "Access Point" : "Switch",
                                UplinkDevice = uplink ?? string.Empty,
                                Port = port ?? string.Empty
                            };
                            Devices.Add(dev);
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(uplink) && string.IsNullOrEmpty(dev.UplinkDevice)) dev.UplinkDevice = uplink;
                            if (!string.IsNullOrEmpty(port) && string.IsNullOrEmpty(dev.Port)) dev.Port = port;
                        }

                        if (!string.IsNullOrEmpty(hostname) && (dev.Hostname == dev.IpAddress || dev.Hostname.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase)))
                        {
                            dev.Hostname = hostname;
                        }
                        if (!string.IsNullOrEmpty(deviceIp) && !deviceIp.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase) && dev.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                        {
                            dev.IpAddress = deviceIp;
                        }
                        dev.DeviceType = isAp ? "Access Point" : "Switch";
                        dev.Progress = pct;
                        dev.Status = stat;
                        dev.Protocol = proto;

                        if (stat.StartsWith("Completed", StringComparison.OrdinalIgnoreCase))
                        {
                            dev.IsCompleted = true;
                            dev.IsFailed = false;
                            dev.IsInProgress = false;
                            dev.StatusColor = "#107C41";
                            dev.ProtocolColor = "#2D7D9A";
                        }
                        else if (stat.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) || stat.Contains("Auth Failed") || proto == "ERR")
                        {
                            dev.IsFailed = true;
                            dev.IsCompleted = false;
                            dev.IsInProgress = false;
                            dev.StatusColor = "#C42B1C";
                            dev.ProtocolColor = "#E04343";
                        }
                        else if (stat.StartsWith("Skipped", StringComparison.OrdinalIgnoreCase))
                        {
                            dev.IsFailed = false;
                            dev.IsCompleted = false;
                            dev.IsInProgress = false;
                            dev.StatusColor = "#D97706";
                            dev.ProtocolColor = "#F59E0B";
                        }
                        else
                        {
                            dev.IsInProgress = true;
                            dev.IsCompleted = false;
                            dev.IsFailed = false;
                            dev.StatusColor = "#0078D4";
                            dev.ProtocolColor = "#4CC2FF";
                        }

                        UpdateMetrics();
                    }),
                    _cts.Token
                );
            });

            LastBackupDirectory = resultDir;
            HasCompletedBackups = Directory.Exists(resultDir) && Directory.EnumerateFileSystemEntries(resultDir).Any();
            RunningStatusSummary = $"Completed. {CompletedDevicesCount} backed up, {FailedDevicesCount} failed.";
            AppendLog("System", "All backup tasks successfully finalized.");
            AppendLog("System", "Backups staged in temporary session workspace. Use 'Save to Folder...' or 'Export ZIP...' to save your files.");
        }
        catch (OperationCanceledException)
        {
            RunningStatusSummary = "Canceled by user";
            AppendLog("Warning", "Backup run canceled by user.");
        }
        catch (Exception ex)
        {
            RunningStatusSummary = "Error occurred";
            AppendLog("Error", $"Engine execution encountered an error: {ex.Message}");
        }
        finally
        {
            _sessionStopwatch.Stop();
            _sessionTimer.Stop();
            IsRunning = false;
            foreach (var d in Devices)
            {
                d.IsInProgress = false;
            }
            UpdateMetrics();
            _appState.UpdateRunningStatus(false, OverallProgress, RunningStatusSummary);
        }
    }

    [RelayCommand]
    private void CancelBackup()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            RunningStatusSummary = "Cancelling operations...";
            _appState.UpdateRunningStatus(true, OverallProgress, RunningStatusSummary);
            AppendLog("System", "Cancellation signal sent. Stopping workers...");
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        string dirToOpen = !string.IsNullOrWhiteSpace(LastBackupDirectory) && Directory.Exists(LastBackupDirectory)
            ? LastBackupDirectory
            : BackupDirectory;

        if (string.IsNullOrWhiteSpace(dirToOpen) || !Directory.Exists(dirToOpen))
        {
            AppendLog("Warning", "No active temporary session workspace exists yet.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dirToOpen,
                UseShellExecute = true,
                Verb = "open"
            });
            AppendLog("System", $"Opened temporary session directory: {dirToOpen}");
        }
        catch (Exception ex)
        {
            AppendLog("Error", $"Could not open folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveToFolderAsync()
    {
        string sourceDir = !string.IsNullOrWhiteSpace(LastBackupDirectory) && Directory.Exists(LastBackupDirectory)
            ? LastBackupDirectory
            : BackupDirectory;

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir) || !Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            await _dialogService.ShowMessageAsync("Save to Folder", "No backup files are currently available to save. Please run a backup first.");
            return;
        }

        var selectedFolder = await _dialogService.ShowOpenFolderDialogAsync("Select Destination Folder to Save Backups");
        if (string.IsNullOrWhiteSpace(selectedFolder))
        {
            return;
        }

        try
        {
            await Task.Run(() => CopyDirectoryContents(sourceDir, selectedFolder));
            AppendLog("System", $"All backup files successfully saved to: {selectedFolder}");
            await _dialogService.ShowMessageAsync("Save Complete", $"Successfully saved backup files to:\n{selectedFolder}");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = selectedFolder,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch { }
        }
        catch (Exception ex)
        {
            AppendLog("Error", $"Failed to save backup files to folder: {ex.Message}");
            await _dialogService.ShowMessageAsync("Save Error", $"Failed to save backup files: {ex.Message}");
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, targetFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
            CopyDirectoryContents(dir, targetSubDir);
        }
    }

    [RelayCommand]
    private async Task ExportZipAsync()
    {
        string sourceDir = !string.IsNullOrWhiteSpace(LastBackupDirectory) && Directory.Exists(LastBackupDirectory)
            ? LastBackupDirectory
            : BackupDirectory;

        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir) || !Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            await _dialogService.ShowMessageAsync("Export ZIP", "No backup files are currently available to export. Please run a backup first.");
            return;
        }

        string defaultZipName = $"Switch_Backups_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var savePath = await _dialogService.ShowSaveFileDialogAsync("Export Backups As ZIP Archive", defaultZipName, "zip", "ZIP Archives");

        if (!string.IsNullOrEmpty(savePath))
        {
            try
            {
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }

                await Task.Run(() => ZipFile.CreateFromDirectory(sourceDir, savePath));
                AppendLog("System", $"Exported backups ZIP successfully to: {savePath}");
                await _dialogService.ShowMessageAsync("Export Complete", $"Successfully exported backup archive to:\n{savePath}");
            }
            catch (Exception ex)
            {
                AppendLog("Error", $"Failed to export ZIP archive: {ex.Message}");
                await _dialogService.ShowMessageAsync("Export Error", $"Failed to export ZIP archive: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs = string.Empty;
    }

    [RelayCommand]
    private async Task CopyLogsAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(Logs))
            {
                await _dialogService.SetClipboardTextAsync(Logs);
                AppendLog("System", "Console logs copied to clipboard.");
            }
        }
        catch { }
    }

    private void UpdateMetrics()
    {
        TotalDevicesCount = Devices.Count;
        CompletedDevicesCount = Devices.Count(d => d.IsCompleted);
        FailedDevicesCount = Devices.Count(d => d.IsFailed);
        InProgressDevicesCount = Devices.Count(d => d.IsInProgress);

        if (TotalDevicesCount > 0)
        {
            OverallProgress = (int)Math.Round((double)(CompletedDevicesCount + FailedDevicesCount) / TotalDevicesCount * 100.0);
            if (IsRunning)
            {
                RunningStatusSummary = $"Processing: {CompletedDevicesCount + FailedDevicesCount} of {TotalDevicesCount} devices completed ({OverallProgress}%)";
            }
        }
        else
        {
            OverallProgress = 0;
        }

        _appState.UpdateRunningStatus(IsRunning, OverallProgress, RunningStatusSummary);
    }

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BulkSwitchBackup",
        "Logs"
    );
    private static readonly string LogFilePath = Path.Combine(LogDirectory, "bulkswitchbackup.log");

    public void AppendLog(string source, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        string entry = $"[{timestamp}] [{source}] {message}{Environment.NewLine}";
        if (Logs.Length > 250_000)
        {
            Logs = Logs.Substring(Logs.Length - 150_000) + entry;
        }
        else
        {
            Logs += entry;
        }

        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
            File.AppendAllText(LogFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {message}{Environment.NewLine}");
        }
        catch { }
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
            AppendLog("System", $"Opened application logs folder: {LogDirectory}");
        }
        catch (Exception ex)
        {
            AppendLog("Error", $"Could not open logs folder: {ex.Message}");
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
            AppendLog("System", "Navigating to Yash Nalawde's LinkedIn profile...");
        }
        catch (Exception ex)
        {
            AppendLog("Error", $"Could not open LinkedIn: {ex.Message}");
        }
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
            AppendLog("System", "Navigating to Yash Nalawde's GitHub profile...");
        }
        catch (Exception ex)
        {
            AppendLog("Error", $"Could not open GitHub: {ex.Message}");
        }
    }
}
