using BulkSwitchBackupPro.Models;
using BulkSwitchBackupPro.Services;
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
using System.Windows.Media;

namespace BulkSwitchBackupPro.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private static readonly string[] CiscoIosCommands = new[]
        {
            "terminal length 0",
            "show clock",
            "show version",
            "show inventory",
            "show switch",
            "show switch stack-ring speed",
            "show switch virtual",
            "show environment",
            "show environment all",
            "show environment temperature",
            "show env power",
            "show env fan",
            "show power inline",
            "show post",
            "show processes cpu | include CPU",
            "show processes memory",
            "show ip interface brief",
            "show interfaces description",
            "show interfaces status",
            "show interfaces counters errors",
            "show interfaces trunk",
            "show vlan",
            "show vlan brief",
            "show mac address-table",
            "show ip arp",
            "show ip route",
            "show ip route summary",
            "show ip protocols",
            "show spanning-tree",
            "show spanning-tree active",
            "show spanning-tree root",
            "show spanning-tree summary",
            "show vtp status",
            "show etherchannel summary",
            "show cdp neighbors",
            "show cdp neighbors detail",
            "show lldp neighbors",
            "show lldp neighbors detail",
            "show port-security",
            "show port-security address",
            "show ip dhcp snooping",
            "show ip source binding",
            "show access-lists",
            "show logging",
            "show log | include Interface",
            "show license",
            "show license usage",
            "show standby brief",
            "show standby all",
            "show vrrp brief",
            "show vrrp all",
            "show ip sla summary",
            "show ip sla statistics",
            "show ntp status",
            "show ntp associations",
            "show running-config",
            "show startup-config"
        };

        private static readonly string[] CiscoNexusCommands = new[]
        {
            "terminal length 0",
            "show clock",
            "show version",
            "show inventory",
            "show license usage",
            "show system resources",
            "show processes cpu",
            "show processes memory",
            "show environment",
            "show environment temperature",
            "show environment power",
            "show environment fan",
            "show interface status",
            "show interface counters errors",
            "show interface description",
            "show vlan brief",
            "show mac address-table",
            "show ip arp",
            "show ip route",
            "show ip route summary",
            "show ip protocols",
            "show spanning-tree",
            "show spanning-tree root",
            "show port-channel summary",
            "show port-channel usage",
            "show cdp neighbors detail",
            "show lldp neighbors detail",
            "show hsrp brief",
            "show hsrp all",
            "show vrrp brief",
            "show vrrp all",
            "show ip sla summary",
            "show ip sla statistics",
            "show logging",
            "show logging logfile",
            "show ntp peers",
            "show ntp status",
            "show running-config",
            "show startup-config"
        };

        private static readonly string[] CustomTemplateCommands = new[]
        {
            "! Custom Backup Script",
            "! Enter your CLI commands, one per line:",
            "terminal length 0",
            "show version",
            "show ip interface brief",
            "show running-config"
        };

        private readonly CredentialStore _credStore;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string targetIps = string.Empty;

        [ObservableProperty]
        private bool enableCdp = false;

        [ObservableProperty]
        private int filterTypeIndex = 0; // 0 = Subnet, 1 = VLAN ID

        [ObservableProperty]
        private string filterValue = string.Empty;

        [ObservableProperty]
        private bool includeAps = false;

        [ObservableProperty]
        private int maxConcurrency = 100;

        [ObservableProperty]
        private bool writeConfig = true;

        [ObservableProperty]
        private int selectedProfileIndex = 0; // 0=IOS, 1=Nexus, 2=Custom

        [ObservableProperty]
        private string customCommandsText = string.Empty;

        [ObservableProperty]
        private int commandCount = 56;

        [ObservableProperty]
        private bool isRunning = false;

        [ObservableProperty]
        private string lastBackupDirectory = string.Empty;

        [ObservableProperty]
        private bool hasCompletedBackups = false;

        [ObservableProperty]
        private string logs = string.Empty;

        // Metrics for summary strip
        [ObservableProperty]
        private int totalDevicesCount = 0;

        [ObservableProperty]
        private int completedDevicesCount = 0;

        [ObservableProperty]
        private int inProgressDevicesCount = 0;

        [ObservableProperty]
        private int failedDevicesCount = 0;

        // Credential Vault Inputs
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

        private CancellationTokenSource? _cts;

        public MainViewModel()
        {
            _credStore = new CredentialStore();
            RefreshStoredCredentials();
            LoadProfileCommands(0);
        }

        public void RefreshStoredCredentials()
        {
            StoredCredentials.Clear();
            foreach (var cred in _credStore.LoadAllCredentials())
            {
                StoredCredentials.Add(cred);
            }
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
                    CustomCommandsText = string.Join(Environment.NewLine, CiscoIosCommands);
                    break;
                case 1:
                    CustomCommandsText = string.Join(Environment.NewLine, CiscoNexusCommands);
                    break;
                case 2:
                    CustomCommandsText = string.Join(Environment.NewLine, CustomTemplateCommands);
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
            AppendLog("System", "Commands reset to default profile template.");
        }

        [RelayCommand]
        private void ClearCommands()
        {
            CustomCommandsText = "! Enter custom commands, one per line" + Environment.NewLine;
            UpdateCommandCount();
            AppendLog("System", "Custom commands cleared.");
        }

        [RelayCommand]
        private void SaveNewCredential()
        {
            if (string.IsNullOrWhiteSpace(NewCredProfileName) || string.IsNullOrWhiteSpace(NewCredUsername) || string.IsNullOrWhiteSpace(NewCredPassword))
            {
                MessageBox.Show("Please enter Profile Name, Username, and Password.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _credStore.SaveCredential(NewCredProfileName.Trim(), NewCredUsername.Trim(), NewCredPassword, NewCredSecret.Trim());
                RefreshStoredCredentials();
                AppendLog("Security", $"Saved credential profile '{NewCredProfileName}' to Windows DPAPI.");

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
        private void ImportDevices()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Select Devices File"
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
                        AppendLog("System", $"Loaded {ips.Count} IP addresses from {Path.GetFileName(dialog.FileName)}");
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
        private async Task StartBackupAsync()
        {
            if (IsRunning) return;

            string pass = ((MainWindow)Application.Current.MainWindow).GetPassword();
            string secret = ((MainWindow)Application.Current.MainWindow).GetSecret();

            // Build list of candidate credentials
            var candidateCreds = new List<CredentialEntry>();

            if (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrEmpty(pass))
            {
                candidateCreds.Add(new CredentialEntry
                {
                    Name = "Active UI Credential",
                    Username = Username.Trim(),
                    Password = pass,
                    Secret = secret.Trim()
                });
            }

            // Append stored credentials from DPAPI vault as fallbacks
            foreach (var stored in StoredCredentials)
            {
                if (!candidateCreds.Any(c => c.Username.Equals(stored.Username, StringComparison.OrdinalIgnoreCase) && c.Password == stored.Password))
                {
                    candidateCreds.Add(stored);
                }
            }

            if (candidateCreds.Count == 0)
            {
                MessageBox.Show("Please enter an SSH Username and Password, or add credentials in the Credentials Vault.", "No Credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("System", "Error: No credentials available to connect to devices.");
                return;
            }

            var ips = TargetIps.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(ip => ip.Trim())
                               .Where(ip => !string.IsNullOrEmpty(ip))
                               .Distinct()
                               .ToList();

            if (!ips.Any())
            {
                MessageBox.Show("Please provide at least one target device or seed IP address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("System", "No Target IPs provided.");
                return;
            }

            var commandsList = CustomCommandsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(c => c.Trim())
                                                 .Where(c => !string.IsNullOrEmpty(c) && !c.StartsWith("!"))
                                                 .ToList();

            if (!commandsList.Any())
            {
                MessageBox.Show("The command list is empty. Please configure at least one CLI command.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("System", "No valid CLI commands to execute.");
                return;
            }

            string profileTitle = SelectedProfileIndex switch
            {
                0 => "Cisco Switch (IOS / IOS-XE)",
                1 => "Cisco Nexus Switch (NX-OS)",
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
                    ProtocolColor = new SolidColorBrush(Colors.LightGray),
                    StatusColor = new SolidColorBrush(Color.FromRgb(45, 45, 48))
                });
            }

            UpdateMetrics();
            IsRunning = true;
            _cts = new CancellationTokenSource();

            AppendLog("System", $"Starting bulk backup with profile '{profileTitle}' across {ips.Count} initial targets ({commandsList.Count} commands each)...");
            AppendLog("Security", $"Using pool of {candidateCreds.Count} credential(s) with automatic failover.");

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
                        (source, message) => App.Current.Dispatcher.Invoke(() => AppendLog(source, message)),
                        (deviceIp, pct, stat, proto, hostname, isAp) => App.Current.Dispatcher.Invoke(() =>
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
                                dev.StatusColor = new SolidColorBrush(Color.FromRgb(22, 130, 60)); // Green
                                dev.ProtocolColor = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                            }
                            else if (stat.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) || stat.Contains("Auth Failed") || proto == "ERR")
                            {
                                dev.IsFailed = true;
                                dev.IsCompleted = false;
                                dev.StatusColor = new SolidColorBrush(Color.FromRgb(168, 42, 42)); // Red
                                dev.ProtocolColor = new SolidColorBrush(Color.FromRgb(244, 135, 113));
                            }
                            else
                            {
                                dev.StatusColor = new SolidColorBrush(Color.FromRgb(14, 99, 156)); // Blue
                                dev.ProtocolColor = new SolidColorBrush(Color.FromRgb(86, 156, 214));
                            }

                            UpdateMetrics();
                        }),
                        _cts.Token
                    );
                });

                LastBackupDirectory = resultDir;
                HasCompletedBackups = Directory.Exists(resultDir);
                AppendLog("System", "All backup tasks successfully finalized.");
            }
            catch (OperationCanceledException)
            {
                AppendLog("Warning", "Backup run was canceled by user.");
            }
            catch (Exception ex)
            {
                AppendLog("Error", $"Engine execution encountered an error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                UpdateMetrics();
            }
        }

        [RelayCommand]
        private void CancelBackup()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                AppendLog("System", "Cancelling backup operations...");
            }
        }

        [RelayCommand]
        private void OpenBackupFolder()
        {
            string dirToOpen = LastBackupDirectory;
            if (string.IsNullOrWhiteSpace(dirToOpen) || !Directory.Exists(dirToOpen))
            {
                dirToOpen = Path.Combine(Environment.CurrentDirectory, "Switch_Backups");
            }

            if (!Directory.Exists(dirToOpen))
            {
                Directory.CreateDirectory(dirToOpen);
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
            string sourceDir = LastBackupDirectory;
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                MessageBox.Show("No active backup directory found to export.", "Export ZIP", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    AppendLog("System", "Logs copied to clipboard.");
                }
            }
            catch { }
        }

        private void UpdateMetrics()
        {
            TotalDevicesCount = Devices.Count;
            CompletedDevicesCount = Devices.Count(d => d.IsCompleted);
            FailedDevicesCount = Devices.Count(d => d.IsFailed);
            InProgressDevicesCount = Devices.Count(d => !d.IsCompleted && !d.IsFailed && d.Progress > 0);
        }

        public void AppendLog(string source, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Logs += $"[{timestamp}] [{source}] {message}{Environment.NewLine}";
        }
    }
}
