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
    private readonly INavigationService? _navigationService;
    public ObservableCollection<CommandFailure> CommandFailures { get; } = new();
    public ObservableCollection<CommandFailure> VisibleCommandFailures { get; } = new();
    [ObservableProperty] private CommandFailure? selectedCommandFailure;
    [ObservableProperty] private DeviceModel? selectedDevice;
    [ObservableProperty] private string commandFailureFilter = string.Empty;
    public int CommandFailureCount => CommandFailures.Count;
    public bool HasCommandFailures => CommandFailureCount > 0;
    private CancellationTokenSource? _cts;

    // Application & Developer Details
    [ObservableProperty] private string appVersion = "3.0.1 Enterprise";
    [ObservableProperty] private string framework = ".NET 10.0 Desktop Runtime with Avalonia UI 11 & FluentAvalonia";
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
    [ObservableProperty] private int selectedProfileIndex = 5;
    [ObservableProperty] private string customCommandsText = string.Empty;
    [ObservableProperty] private int commandCount = 56;
    [ObservableProperty] private DevicePlatform selectedPreviewPlatform = DevicePlatforms.All[0];
    private string? _customCommandDraft;
    public bool IsLocalBeta => BuildChannel.IsLocalBeta;
    public IReadOnlyList<string> ProfileNames { get; } = new[]
    {
        "Cisco Switch (IOS / IOS-XE)", "Cisco Nexus Switch (NX-OS)",
        "Cisco Catalyst 9800 WLC (IOS-XE)", "Cisco AireOS WLC (5520 / 8540 / 3504)",
        "Custom CLI Command Script", "Auto-detect (multi-vendor switches / WLC)"
    };
    public IReadOnlyList<DevicePlatform> SupportedPlatforms => DevicePlatforms.All;
    public bool IsAutoProfile => SelectedProfileIndex == 5;
    public bool IsCustomProfile => SelectedProfileIndex == 4;
    public bool IsFixedProfile => !IsAutoProfile;
    public string CommandSummary => IsAutoProfile ? $"Dynamic · {SupportedPlatforms.Count} platforms" : $"{CommandCount} commands";
    public IReadOnlyList<string> PreviewCommands => SelectedPreviewPlatform.Commands
        .Where(command => command != SelectedPreviewPlatform.Paging && !NetworkEngine.IsConfigurationPushCommand(command))
        .Distinct().ToArray();
    public string PreviewCommandsText => string.Join(Environment.NewLine, PreviewCommands);
    public string PreviewCommandSummary => $"{SelectedPreviewPlatform.Kind} · {PreviewCommands.Count} diagnostic commands";
    public string PreviewPaging => $"Paging setup: {SelectedPreviewPlatform.Paging}";
    public string PreviewConfiguration => $"Required configuration capture: {SelectedPreviewPlatform.Configuration}";

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
    private bool _metricsDirty;

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

    public MainViewModel(CredentialStore credStore, IDialogService dialogService, AppState appState, INavigationService? navigationService = null)
    {
        _credStore = credStore;
        _dialogService = dialogService;
        _appState = appState;
        _navigationService = navigationService;

        RefreshStoredCredentials();
        LoadProfileCommands(SelectedProfileIndex);

        _sessionTimer.Interval = TimeSpan.FromSeconds(1);
        _sessionTimer.Tick += (s, e) =>
        {
            if (_metricsDirty) UpdateMetrics();
            if (_sessionStopwatch.IsRunning)
            {
                var ts = _sessionStopwatch.Elapsed;
                SessionElapsedTime = ts.Hours > 0 ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }
        };

        AppendLog("System", $"Bulk Switch Backup Pro {AppVersion} ready. Powered by Avalonia UI 11.");
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

    partial void OnSelectedProfileIndexChanging(int oldValue, int newValue)
    {
        if (oldValue == 4) _customCommandDraft = CustomCommandsText;
    }

    partial void OnSelectedProfileIndexChanged(int value)
    {
        LoadProfileCommands(value);
        OnPropertyChanged(nameof(IsAutoProfile));
        OnPropertyChanged(nameof(IsCustomProfile));
        OnPropertyChanged(nameof(IsFixedProfile));
        OnPropertyChanged(nameof(CommandSummary));
    }

    partial void OnSelectedPreviewPlatformChanged(DevicePlatform value)
    {
        OnPropertyChanged(nameof(PreviewCommandsText));
        OnPropertyChanged(nameof(PreviewCommands));
        OnPropertyChanged(nameof(PreviewCommandSummary));
        OnPropertyChanged(nameof(PreviewPaging));
        OnPropertyChanged(nameof(PreviewConfiguration));
    }

    partial void OnCommandCountChanged(int value) => OnPropertyChanged(nameof(CommandSummary));

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
            case 5:
                CustomCommandsText = "! Auto-detect each device and select its OS-specific backup commands." + Environment.NewLine
                    + "! Supported: " + string.Join(", ", DevicePlatforms.All.Select(p => p.Name)) + Environment.NewLine
                    + "! Select Custom CLI Command Script to run your own commands.";
                break;
            case 4:
                CustomCommandsText = _customCommandDraft ?? string.Join(Environment.NewLine, CommandProfiles.CustomTemplateCommands);
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
        if (!IsCustomProfile) return;
        _customCommandDraft = null;
        LoadProfileCommands(SelectedProfileIndex);
        AppendLog("System", "CLI commands reset to default profile template.");
    }

    [RelayCommand]
    private void ClearCommands()
    {
        if (!IsCustomProfile) return;
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
                           .Distinct(StringComparer.OrdinalIgnoreCase)
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

        if (SelectedProfileIndex != 5 && !commandsList.Any())
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
            5 => "Auto (multi-vendor)",
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
            AppendLog("Discovery", "CDP/LLDP Discovery active. Infrastructure capabilities or recognized models are required; endpoint advertisements are excluded.");
        }

        var engine = new NetworkEngine();
        CommandFailures.Clear();
        VisibleCommandFailures.Clear();
        SelectedCommandFailure = null;
        OnPropertyChanged(nameof(CommandFailureCount));
        OnPropertyChanged(nameof(HasCommandFailures));
        engine.CommandFailed += RecordCommandFailure;
        // All accesses occur on the UI thread; workers only post immutable update values.
        var devicesByIp = Devices.ToDictionary(d => d.IpAddress, StringComparer.OrdinalIgnoreCase);
        var placeholdersByName = new Dictionary<string, DeviceModel>(StringComparer.OrdinalIgnoreCase);

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
                        devicesByIp.TryGetValue(deviceIp, out var dev);
                        if (dev == null && !string.IsNullOrEmpty(hostname))
                            placeholdersByName.TryGetValue(HostKey.Of(hostname), out dev);
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
                            devicesByIp.Remove(dev.IpAddress);
                            placeholdersByName.Remove(HostKey.Of(dev.Hostname));
                            dev.IpAddress = deviceIp;
                        }
                        devicesByIp[deviceIp] = dev;
                        if (dev.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                            placeholdersByName[HostKey.Of(dev.Hostname)] = dev;
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
                        else if (stat.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) || stat.StartsWith("Partial", StringComparison.OrdinalIgnoreCase) || stat.Contains("Auth Failed") || proto == "ERR")
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

                        _metricsDirty = true;
                    }),
                    _cts.Token
                );
            });

            // Process already-posted progress callbacks before calculating the final summary.
            await Dispatcher.UIThread.InvokeAsync(UpdateMetrics, DispatcherPriority.Background);
            LastBackupDirectory = resultDir;
            HasCompletedBackups = Directory.Exists(resultDir) && Directory.EnumerateFileSystemEntries(resultDir).Any();
            RunningStatusSummary = $"Finished. {CompletedDevicesCount} complete backups; {FailedDevicesCount} incomplete or failed. See Command Results.";
            AppendLog("System", "Backup processing finished. Review partial captures and failed commands in Command Results.");
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
            _sessionTimer.Stop();
            // Drain worker callbacks on cancellation/failure too, before clearing row state.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            HasCompletedBackups = Directory.Exists(LastBackupDirectory) && Directory.EnumerateFiles(LastBackupDirectory, "*.txt").Any();
            _cts?.Dispose();
            _cts = null;
            _sessionStopwatch.Stop();
            _sessionTimer.Stop();
            IsRunning = false;
            foreach (var d in Devices)
            {
                d.IsInProgress = false;
            }
            UpdateMetrics();
            _appState.UpdateRunningStatus(false, OverallProgress, RunningStatusSummary);
            if (HasCommandFailures)
            {
                CommandFailureFilter = string.Empty;
                _navigationService?.Navigate<BulkBackup.Desktop.Views.CommandResultsView>();
            }
        }
    }

    public void RecordCommandFailure(CommandFailure failure)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RecordCommandFailure(failure));
            return;
        }
        CommandFailures.Add(failure);
        if (MatchesCommandFilter(failure)) VisibleCommandFailures.Add(failure);
        SelectedCommandFailure ??= VisibleCommandFailures.FirstOrDefault();
        OnPropertyChanged(nameof(CommandFailureCount));
        OnPropertyChanged(nameof(HasCommandFailures));
    }

    private bool MatchesCommandFilter(CommandFailure failure) => string.IsNullOrWhiteSpace(CommandFailureFilter) ||
        $"{failure.IpAddress} {failure.Hostname} {failure.Command} {failure.Error}".Contains(CommandFailureFilter.Trim(), StringComparison.OrdinalIgnoreCase);

    partial void OnCommandFailureFilterChanged(string value)
    {
        VisibleCommandFailures.Clear();
        foreach (var failure in CommandFailures.Where(MatchesCommandFilter)) VisibleCommandFailures.Add(failure);
        SelectedCommandFailure = VisibleCommandFailures.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenCommandFailures()
    {
        CommandFailureFilter = SelectedDevice?.IpAddress ?? string.Empty;
        _navigationService?.Navigate<BulkBackup.Desktop.Views.CommandResultsView>();
    }

    [RelayCommand]
    private async Task ExportCommandFailuresAsync()
    {
        if (!HasCommandFailures) return;
        string? path = await _dialogService.ShowSaveFileDialogAsync("Export failed commands", "failed-commands.json", "json", "JSON report");
        if (path == null) return;
        try
        {
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(CommandFailures.ToArray(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { await _dialogService.ShowMessageAsync("Export failed", ex.Message); }
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
        _metricsDirty = false;
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


