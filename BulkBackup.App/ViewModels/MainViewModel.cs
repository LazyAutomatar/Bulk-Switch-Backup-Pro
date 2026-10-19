using BulkBackup.Core.Models;
using BulkBackup.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace BulkBackup.App.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly CredentialStore _credStore;
        private CancellationTokenSource? _cts;

        // Theme properties matching Network Health Reporter
        [ObservableProperty]
        private SymbolRegular themeSymbol = SymbolRegular.WeatherMoon24;

        [ObservableProperty]
        private string themeButtonText = "Dark Mode";

        [ObservableProperty]
        private bool isDark = true;

        [ObservableProperty]
        private int selectedNavigationIndex = 0; // 0=Run & Monitor, 1=Saved Credentials, 2=Command Profiles, 3=About & System

        [ObservableProperty]
        private int topNavIndex = 0;

        [ObservableProperty]
        private int bottomNavIndex = -1;

        partial void OnTopNavIndexChanged(int value)
        {
            if (value >= 0)
            {
                if (BottomNavIndex != -1) BottomNavIndex = -1;
                if (SelectedNavigationIndex != value) SelectedNavigationIndex = value;
            }
        }

        partial void OnBottomNavIndexChanged(int value)
        {
            if (value == 0)
            {
                if (TopNavIndex != -1) TopNavIndex = -1;
                if (SelectedNavigationIndex != 3) SelectedNavigationIndex = 3;
            }
        }

        partial void OnSelectedNavigationIndexChanged(int value)
        {
            if (value >= 0 && value <= 2)
            {
                if (TopNavIndex != value) TopNavIndex = value;
                if (BottomNavIndex != -1) BottomNavIndex = -1;
            }
            else if (value == 3)
            {
                if (TopNavIndex != -1) TopNavIndex = -1;
                if (BottomNavIndex != 0) BottomNavIndex = 0;
            }
        }

        // Application & Developer Details
        [ObservableProperty]
        private string appVersion = "2.5.0 Enterprise";

        [ObservableProperty]
        private string framework = ".NET 8.0 Desktop Runtime with WPF-UI Fluent Design";

        [ObservableProperty]
        private string developerName = "Yash Nalawde";

        [ObservableProperty]
        private string developerLinkedIn = "https://www.linkedin.com/in/yash-nalawde";

        [ObservableProperty]
        private string developerGitHub = "https://github.com/Majordean0303";

        // Active Connection / Direct SSH inputs
        [ObservableProperty]
        private string activeUsername = string.Empty;

        [ObservableProperty]
        private string activePassword = string.Empty;

        [ObservableProperty]
        private string activeSecret = string.Empty;

        [ObservableProperty]
        private bool useVaultAsFallback = true;

        // Target IPs
        [ObservableProperty]
        private string targetIps = string.Empty;

        [ObservableProperty]
        private int targetIpsCount = 0;

        // Discovery & Traversal
        [ObservableProperty]
        private bool enableCdp = false;

        [ObservableProperty]
        private int filterTypeIndex = 0; // 0=Subnet, 1=VLAN ID

        [ObservableProperty]
        private string filterValue = string.Empty;

        [ObservableProperty]
        private bool includeAps = false;

        // Execution Tuning
        [ObservableProperty]
        private int maxConcurrency = Math.Clamp(Environment.ProcessorCount * 2, 6, 16);

        [ObservableProperty]
        private bool writeConfig = true;

        [ObservableProperty]
        private string backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Switch_Backups");

        // Profiles & Commands
        [ObservableProperty]
        private int selectedProfileIndex = 0; // 0=IOS, 1=Nexus, 2=Custom

        [ObservableProperty]
        private string customCommandsText = string.Empty;

        [ObservableProperty]
        private int commandCount = 56;

        // Status & Metrics
        [ObservableProperty]
        private bool isRunning = false;

        [ObservableProperty]
        private string runningStatusSummary = "Ready";

        [ObservableProperty]
        private bool hasCompletedBackups = false;

        [ObservableProperty]
        private string lastBackupDirectory = string.Empty;

        [ObservableProperty]
        private string logs = string.Empty;

        [ObservableProperty]
        private int totalDevicesCount = 0;

        [ObservableProperty]
        private int completedDevicesCount = 0;

        [ObservableProperty]
        private int inProgressDevicesCount = 0;

        [ObservableProperty]
        private int failedDevicesCount = 0;

        [ObservableProperty]
        private int overallProgress = 0;

        // Credential Vault Inputs (Optional Preset Store)
        [ObservableProperty]
        private string newCredProfileName = string.Empty;

        [ObservableProperty]
        private string newCredUsername = string.Empty;

        [ObservableProperty]
        private string newCredPassword = string.Empty;

        [ObservableProperty]
        private string newCredSecret = string.Empty;

        public ObservableCollection<DeviceModel> Devices { get; } = new();
        public ObservableCollection<CredentialEntry> StoredCredentials { get; } = new();

        public MainViewModel()
        {
            _credStore = new CredentialStore();
            RefreshStoredCredentials();
            LoadProfileCommands(0);

            // Initialize System Default Theme matching NetHealth
            try
            {
                ApplicationThemeManager.ApplySystemTheme();
                var currentTheme = ApplicationThemeManager.GetAppTheme();
                IsDark = currentTheme == ApplicationTheme.Dark;
                ThemeSymbol = IsDark ? SymbolRegular.WeatherMoon24 : SymbolRegular.WeatherSunny24;
                ThemeButtonText = IsDark ? "Dark Mode" : "Light Mode";
            }
            catch
            {
                IsDark = true;
                ThemeSymbol = SymbolRegular.WeatherMoon24;
                ThemeButtonText = "Dark Mode";
            }

            if (!Directory.Exists(BackupDirectory))
            {
                try { Directory.CreateDirectory(BackupDirectory); } catch { }
            }

            AppendLog("System", "Bulk Switch Backup Pro v2.5.0 Enterprise ready. Engineered by Yash Nalawde.");
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            if (IsDark)
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                ThemeSymbol = SymbolRegular.WeatherSunny24;
                ThemeButtonText = "Light Mode";
                IsDark = false;
            }
            else
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                ThemeSymbol = SymbolRegular.WeatherMoon24;
                ThemeButtonText = "Dark Mode";
                IsDark = true;
            }
        }

        public void RefreshStoredCredentials()
        {
            StoredCredentials.Clear();
            foreach (var cred in _credStore.LoadAllCredentials())
            {
                StoredCredentials.Add(cred);
            }
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
        private void SaveNewCredential()
        {
            if (string.IsNullOrWhiteSpace(NewCredProfileName) || string.IsNullOrWhiteSpace(NewCredUsername) || string.IsNullOrWhiteSpace(NewCredPassword))
            {
                MessageBox.Show("Please enter Profile Name, Username, and Password.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show($"Failed to save credential: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        private void ImportDevices()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text & CSV Files (*.txt;*.csv)|*.txt;*.csv|All Files (*.*)|*.*",
                Title = "Select Target Devices File"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(dialog.FileName);
                    var matches = Regex.Matches(content, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
                    var ips = matches.Select(m => m.Value).Distinct().ToList();

                    if (ips.Any())
                    {
                        TargetIps = string.Join(Environment.NewLine, ips);
                        AppendLog("System", $"Loaded {ips.Count} IP addresses from '{Path.GetFileName(dialog.FileName)}'.");
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
        private void BrowseBackupFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Backup Storage Directory",
                InitialDirectory = BackupDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                BackupDirectory = dialog.FolderName;
                AppendLog("System", $"Backup destination directory updated: {BackupDirectory}");
            }
        }

        [RelayCommand]
        private async Task StartBackupAsync()
        {
            if (IsRunning) return;

            // Build candidate credentials list
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

            // Append stored credentials from vault if enabled
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
                MessageBox.Show("Please enter an SSH Username and Password to proceed.", "Missing Credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show("Please enter at least one target device IP address.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("System", "Execution halted: No target IPs entered.");
                return;
            }

            var commandsList = CustomCommandsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(c => c.Trim())
                                                 .Where(c => !string.IsNullOrEmpty(c) && !c.StartsWith("!"))
                                                 .ToList();

            if (!commandsList.Any())
            {
                MessageBox.Show("The command list is empty. Please configure at least one CLI command.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("System", "Execution halted: No CLI commands configured.");
                return;
            }

            // Directly prompt user for destination directory, matching ZIP export UX
            var folderDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Destination Folder for Switch Backups",
                InitialDirectory = Directory.Exists(BackupDirectory) ? BackupDirectory : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (folderDialog.ShowDialog() != true)
            {
                AppendLog("System", "Backup canceled: No destination folder selected.");
                return;
            }

            BackupDirectory = folderDialog.FolderName;

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
            RunningStatusSummary = $"Starting backup across {ips.Count} target(s)...";
            _cts = new CancellationTokenSource();

            AppendLog("System", $"Starting bulk backup with profile '{profileTitle}' across {ips.Count} target(s) ({commandsList.Count} commands each)...");
            AppendLog("System", $"Target destination: {BackupDirectory}");
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
                        (source, message) => App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => AppendLog(source, message)),
                        (deviceIp, pct, stat, proto, hostname, isAp) => App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                        {
                            var dev = Devices.FirstOrDefault(d => d.IpAddress == deviceIp);
                            if (dev == null)
                            {
                                dev = new DeviceModel
                                {
                                    IpAddress = deviceIp,
                                    Hostname = hostname ?? deviceIp,
                                    DeviceType = isAp ? "Access Point" : "Switch"
                                };
                                Devices.Add(dev);
                            }

                            if (!string.IsNullOrEmpty(hostname) && dev.Hostname == dev.IpAddress)
                            {
                                dev.Hostname = hostname;
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
                                dev.StatusColor = "#107C41"; // Enterprise Green
                                dev.ProtocolColor = "#2D7D9A";
                            }
                            else if (stat.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) || stat.Contains("Auth Failed") || proto == "ERR")
                            {
                                dev.IsFailed = true;
                                dev.IsCompleted = false;
                                dev.IsInProgress = false;
                                dev.StatusColor = "#C42B1C"; // High-contrast Red
                                dev.ProtocolColor = "#E04343";
                            }
                            else
                            {
                                dev.IsInProgress = true;
                                dev.IsCompleted = false;
                                dev.IsFailed = false;
                                dev.StatusColor = "#0078D4"; // Fluent Blue
                                dev.ProtocolColor = "#4CC2FF";
                            }

                            UpdateMetrics();
                        }),
                        _cts.Token
                    );
                });

                LastBackupDirectory = resultDir;
                HasCompletedBackups = Directory.Exists(resultDir);
                RunningStatusSummary = $"Completed. {CompletedDevicesCount} backed up, {FailedDevicesCount} failed.";
                AppendLog("System", "All backup tasks successfully finalized.");
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
                IsRunning = false;
                foreach (var d in Devices)
                {
                    d.IsInProgress = false;
                }
                UpdateMetrics();
            }
        }

        [RelayCommand]
        private void CancelBackup()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                RunningStatusSummary = "Cancelling operations...";
                AppendLog("System", "Cancellation signal sent. Stopping workers...");
            }
        }

        [RelayCommand]
        private void OpenBackupFolder()
        {
            string dirToOpen = !string.IsNullOrWhiteSpace(LastBackupDirectory) && Directory.Exists(LastBackupDirectory)
                ? LastBackupDirectory
                : BackupDirectory;

            if (!Directory.Exists(dirToOpen))
            {
                try { Directory.CreateDirectory(dirToOpen); } catch { }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dirToOpen,
                    UseShellExecute = true,
                    Verb = "open"
                });
                AppendLog("System", $"Opened backups directory: {dirToOpen}");
            }
            catch (Exception ex)
            {
                AppendLog("Error", $"Could not open folder: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ExportZip()
        {
            string sourceDir = !string.IsNullOrWhiteSpace(LastBackupDirectory) && Directory.Exists(LastBackupDirectory)
                ? LastBackupDirectory
                : BackupDirectory;

            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                MessageBox.Show("No backup directory found to export.", "Export ZIP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Backups As ZIP Archive",
                Filter = "ZIP Archives (*.zip)|*.zip",
                FileName = $"Switch_Backups_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (File.Exists(dialog.FileName))
                    {
                        File.Delete(dialog.FileName);
                    }

                    ZipFile.CreateFromDirectory(sourceDir, dialog.FileName);
                    AppendLog("System", $"Exported backups ZIP successfully to: {dialog.FileName}");
                    MessageBox.Show($"Successfully exported backup archive to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AppendLog("Error", $"Failed to export ZIP archive: {ex.Message}");
                    MessageBox.Show($"Failed to export ZIP archive: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        private void ClearLogs()
        {
            Logs = string.Empty;
        }

        [RelayCommand]
        private void CopyLogs()
        {
            try
            {
                if (!string.IsNullOrEmpty(Logs))
                {
                    Clipboard.SetText(Logs);
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

        [RelayCommand]
        private void NavigateToAbout()
        {
            SelectedNavigationIndex = 3;
        }
    }
}
