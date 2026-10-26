using BulkBackup.Core.Models;
using Renci.SshNet.Common;
using System.Diagnostics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace BulkBackup.Core.Services
{
    public class NetworkEngine
    {
        private readonly IBackupTransport _transport;
        public event Action<CommandFailure>? CommandFailed;

        public NetworkEngine(IBackupTransport? transport = null)
        {
            _transport = new LoginBackupTransport(transport ?? new FallbackBackupTransport());
        }

        public delegate void LogAction(string source, string message);
        public delegate void ProgressAction(string ip, int percentage, string status, string protocol, string? hostname = null, bool isAp = false, string? uplink = null, string? port = null);

        private static readonly Regex PromptRegex = new(@"(?:^|[\r\n])\s*(?:<[\w.\-]+>|\[[\w.@:/ \-]+\](?:\s*[>#])?|(?:\([\w.\-\s]+\)|[* ]*[\w.@:/\-]+)(?:\([^)]*\))?\s*[>#])\s*$", RegexOptions.Compiled);
        private static readonly Regex PasswordRegex = new(@"[Pp]assword:\s*$", RegexOptions.Compiled);
        private static readonly Regex MoreRegex = new(@"--\s*More\s*--|----\s*More\s*----|Press any key to continue|Press Enter to continue|Would you like to display[^\r\n]*\(y/n\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DeviceIdRegex = new(@"Device[ -]ID:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlatformRegex = new(@"Platform:\s*([^,\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CapabilitiesRegex = new(@"Capabilities:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CdpIpRegex = new(@"(?:Entry address\(es\):|Management address\(es\):)[\s\S]*?(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FallbackIpRegex = new(@"\b(?:IP|IPv4)(?:\s+address\s*:|\s*:|\s+)\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PrimaryManagementIpRegex = new(@"(?im)^[ \t]*Primary Management Address:\s*(?:IP|IPv4)\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)\b", RegexOptions.Compiled);

        public static readonly HashSet<string> InvalidHostKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "NAME:", "NAME", "PID", "PID:", "SN", "SN:", "DESCR", "DESCR:", "VID", "VID:",
            "Power", "Supply", "PS1", "PS2", "PS3", "Status", "States", "Model", "Type",
            "Capacity", "Fan", "Total", "System", "Module", "Voltage", "Current", "mV",
            "Threshold", "Temperature", "Sensor", "SUCCEEDED", "UCCEEDED", "FAILED", "ERROR", "WARNING",
            "INFO", "Configured", "Console", "Line", "Vlan", "VLAN", "Interface", "IP", "MAC",
            "Address", "Speed", "Duplex", "Unknown", "na", "n/a", "none", "switch", "core"
        };

        public static bool IsValidCiscoDeviceId(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            string d = deviceId.Trim();
            if (d.Length < 2 || d.Length > 64) return false;
            if (d.EndsWith(':') || d.Contains('"') || d.Contains('\'') || d.Contains('=')) return false;
            if (int.TryParse(d, out _)) return false;
            if (InvalidHostKeywords.Contains(d)) return false;
            if (d.Contains("SUCCEED", StringComparison.OrdinalIgnoreCase) || d.Contains("CCEEDED", StringComparison.OrdinalIgnoreCase)) return false;
            if (d.Contains("CONFIG", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static bool IsValidCiscoInterface(string port)
        {
            if (string.IsNullOrWhiteSpace(port)) return false;
            string p = port.Trim();
            if (p.Length < 2) return false;
            if (p.EndsWith(':') || p.Contains('"') || p.Contains('\'') || p.Contains('=')) return false;

            return p.StartsWith("Gi", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Fa", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Te", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Tw", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Fo", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Hu", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Eth", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Po", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("mgmt", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Lo", StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith("Port", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> StartEngineAsync(
            List<string> targetIps,
            List<CredentialEntry> candidateCredentials,
            bool enableCdp,
            int filterType, // 0=Subnet, 1=VLAN
            string filterValue,
            bool includeAps,
            int maxConcurrency,
            List<string> customCommands,
            string profileName,
            bool writeConfig,
            string destinationDirectory,
            LogAction logCallback,
            ProgressAction progressCallback,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(targetIps);
            ArgumentNullException.ThrowIfNull(customCommands);
            if (candidateCredentials == null || candidateCredentials.Count == 0)
            {
                throw new ArgumentException("At least one credential must be provided.");
            }

            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                destinationDirectory = Path.Combine(Environment.CurrentDirectory, "Switch_Backups");
            }
            Directory.CreateDirectory(destinationDirectory);

            int workerCount = Math.Clamp(maxConcurrency, 1, 64);
            logCallback("System", $"Starting Bulk Switch Backup Pro (Profile: {profileName}, Workers: {workerCount}, Credentials: {candidateCredentials.Count})...");

            var devicesToBackup = new DiscoveryTargets();
            var captures = new ConcurrentDictionary<string, DeviceCapture>(StringComparer.OrdinalIgnoreCase);
            var manifest = new CaptureManifest { SelectedProfile = profileName, RequestedTargets = targetIps.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            DeviceCapture CaptureFor(DiscoveredTarget target) => captures.GetOrAdd(target.IpAddress, _ => new DeviceCapture
            {
                IpAddress = target.IpAddress, Hostname = target.Hostname, Profile = profileName, SaveRequested = writeConfig,
                Commands = customCommands.Where(c => !string.IsNullOrWhiteSpace(c) && !c.TrimStart().StartsWith('!') &&
                    !IsConfigurationPushCommand(c) && !DevicePlatforms.All.Any(p => p.Paging == c.Trim()))
                    .Select(c => new CommandCapture { Command = c.Trim() }).ToList()
            });
            foreach (string seed in manifest.RequestedTargets) CaptureFor(new DiscoveredTarget { IpAddress = seed, Hostname = seed });
            try
            {

            if (enableCdp)
            {
                logCallback("System", "Initiating CDP/LLDP Discovery Phase (positive infrastructure evidence required; endpoints excluded)...");
                foreach (var ip in targetIps.Distinct(StringComparer.OrdinalIgnoreCase))
                    devicesToBackup.AddOrMerge(new DiscoveredTarget { IpAddress = ip, Hostname = ip });
                await RunDiscoveryWorkersAsync(targetIps, workerCount, async (ip, parent, enqueue, ct) =>
                    await PerformDiscoveryAsync(ip, candidateCredentials, filterType, filterValue, includeAps,
                        devicesToBackup, enqueue, logCallback, progressCallback, ct, parent), token);
                logCallback("System", $"CDP/LLDP Discovery Phase complete. Discovered {devicesToBackup.Count} total valid targets.");
            }
            else
            {
                foreach (var ip in targetIps)
                {
                    devicesToBackup.AddOrMerge(new DiscoveredTarget { IpAddress = ip, Hostname = ip, IsAp = false });
                }
            }

            logCallback("System", $"Initiating Parallel Backup Phase across {devicesToBackup.Count} targets (Up to {workerCount} concurrent workers)...");

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = workerCount,
                CancellationToken = token
            };

            await Parallel.ForEachAsync(devicesToBackup.Snapshot(), parallelOptions, async (target, ct) =>
            {
                var capture = CaptureFor(target);
                if (string.IsNullOrWhiteSpace(target.IpAddress) ||
                    target.IpAddress.StartsWith("0.0.0.") ||
                    target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                {
                    logCallback("Discovery", $"Device '{target.Hostname}' on {target.UplinkDevice} ({target.Port}) has no advertised IP address. Skipping SSH backup.");
                    progressCallback(target.IpAddress, 0, "Skipped (No IP)", "L2", target.Hostname, target.IsAp, target.UplinkDevice, target.Port);
                    capture.Status = "skipped";
                    capture.Reason = "No usable management IP address.";
                    return;
                }

                var cmds = target.IsAp ? CommandProfiles.CiscoApCommands.ToList() : customCommands;
                try
                {
                    await PerformBackupWithFallbackAsync(target.IpAddress, candidateCredentials, target.IsAp, destinationDirectory, cmds, profileName, writeConfig, logCallback, progressCallback, ct, target.Hostname, target.UplinkDevice, target.Port, capture);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    capture.Status = "failure";
                    capture.Reason = ex.Message;
                    progressCallback(target.IpAddress, 0, $"Failed: {ex.Message}", "ERR", target.Hostname, target.IsAp, target.UplinkDevice, target.Port);
                }
            });

            logCallback("System", $"Backup processing finished. See per-device results. Output directory: {destinationDirectory}");
            return destinationDirectory;
            }
            finally
            {
                foreach (var capture in captures.Values)
                {
                    if (capture.Status == "notAttempted") capture.Reason = token.IsCancellationRequested ? "Capture cancelled before completion." : "Device was not captured.";
                    foreach (var command in capture.Commands.Where(c => c.Status == "notAttempted"))
                        command.Reason ??= capture.Reason ?? "Command was not attempted.";
                }
                manifest.CompletedAtUtc = DateTimeOffset.UtcNow;
                manifest.Devices = captures.Values.OrderBy(c => c.IpAddress, StringComparer.OrdinalIgnoreCase).ToList();
                await BackupArchive.WriteManifestAsync(destinationDirectory, manifest);
            }
        }

        public static Task<bool> IsSshPortReachableAsync(string host, int port = 22, int timeoutMs = 2500, CancellationToken token = default) =>
            SshNetBackupTransport.IsPortReachableAsync(host, port, timeoutMs, token);

        private async Task PerformDiscoveryAsync(
            string ip,
            List<CredentialEntry> candidateCredentials,
            int filterType,
            string filterValue,
            bool includeAps,
            DiscoveryTargets backupQueue,
            Action<string, string?> enqueue,
            LogAction log,
            ProgressAction progress,
            CancellationToken token,
            string? parentHost = null)
        {

            string protocol = "SSH/Telnet";
            progress(ip, 5, "Probing SSH (22) and Telnet (23)...", protocol, null, false, parentHost, null);
            bool reachable = await _transport.IsReachableAsync(ip, token);
            if (!reachable)
            {
                progress(ip, 0, "Unreachable / Ports 22 and 23 Closed", "ERR", null, false, parentHost, null);
                log("Error", $"Seed {ip} is unreachable on ports 22 and 23 (timeout or connection refused). Skipping CDP discovery.");
                return;
            }

            progress(ip, 15, "Discovering CDP/LLDP neighbors...", protocol, null, false, parentHost, null);

            IBackupSession? client = null;
            CredentialEntry? activeCred = null;
            string? connectionFailure = null;

            foreach (var cred in candidateCredentials)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var testClient = await _transport.ConnectAsync(ip, cred, token);

                    client = testClient;
                    protocol = client.Protocol;
                    if (protocol == "Telnet") log("Transport", "SSH unavailable; using Telnet (unencrypted connection).");
                    activeCred = cred;
                    break;
                }
                catch (SshAuthenticationException)
                {
                    log("Auth", $"Credential '{cred.Username}' rejected by {ip}. Trying next stored credential...");
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    connectionFailure = ex.Message;
                    log("Error", $"Connection error to {ip} with '{cred.Username}': {ex.Message}");
                    break;
                }
            }

            if (client == null || activeCred == null)
            {
                progress(ip, 0, connectionFailure == null ? "Discovery Auth Failed" : $"Discovery failed: {connectionFailure}", "ERR", null, false, parentHost, null);
                log("Error", $"All credentials failed on seed {ip}.");
                return;
            }

            try
            {
                using (client)
                {
                    using var stream = client.OpenShell();
                    string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 8000, token);

                    string prompt = await ElevatePrivilegesAsync(stream, initial, activeCred.Secret, token);

                    string currentHost = ip;
                    var hostMatch = Regex.Match(prompt, @"(?:^|[\r\n])\s*(?:\(([\w\.\-\s]+?)\)|([\w\.\-]+?))(?:\([^\)]+\))?[>#]");
                    if (hostMatch.Success)
                    {
                        string candidate = !string.IsNullOrWhiteSpace(hostMatch.Groups[1].Value) ? hostMatch.Groups[1].Value : hostMatch.Groups[2].Value;
                        candidate = candidate.Trim();
                        if (!string.IsNullOrEmpty(candidate) &&
                            !candidate.Equals("Switch", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Equals("Router", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Equals("Cisco Controller", StringComparison.OrdinalIgnoreCase))
                        {
                            currentHost = candidate;
                        }
                    }

                    // Update self hostname in queue
                    backupQueue.UpdateHostname(ip, currentHost);

                    // Small Business switches use datadump instead of IOS terminal length.
                    string discoveryIdentity = await ExecuteRawCommandAsync(stream, "show version", token);
                    var discoveryPlatform = DevicePlatforms.Recognize(DevicePlatforms.Body(discoveryIdentity, "show version"));
                    if (discoveryPlatform == null)
                    {
                        string systemIdentity = await ExecuteRawCommandAsync(stream, "show system", token);
                        discoveryPlatform = DevicePlatforms.Recognize(DevicePlatforms.Body(systemIdentity, "show system"));
                    }
                    if (discoveryPlatform == null)
                    {
                        string inventoryIdentity = await ExecuteRawCommandAsync(stream, "show inventory", token);
                        discoveryPlatform = DevicePlatforms.Recognize(DevicePlatforms.Body(inventoryIdentity, "show inventory"));
                    }
                    await ExecuteRawCommandAsync(stream, discoveryPlatform?.Paging ?? "terminal length 0", token);

                    // 1. Protocols table: map Device ID -> IPv4 address fast and reliably
                    string cdpProtocolsOutput = string.Empty;
                    try
                    {
                        string command = discoveryPlatform?.Id == "cisco-sb" ? "show cdp entry * protocol" : "show cdp entry * protocols";
                        cdpProtocolsOutput = await ExecuteRawCommandAsync(stream, command, token, timeoutMs: 12000);
                        if (DevicePlatforms.IsRejected(cdpProtocolsOutput)) log("CDP", $"{ip}: '{command}' rejected; continuing with detailed CDP/LLDP.");
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        log("CDP", $"Optional 'show cdp entry * protocols' skipped on {ip}: {ex.Message}");
                    }

                    // 2. Detailed CDP
                    string cdpDetailOutput = string.Empty;
                    try
                    {
                        cdpDetailOutput = await ExecuteRawCommandAsync(stream, "show cdp neighbors detail", token, timeoutMs: 25000);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        log("CDP", $"Error running 'show cdp neighbors detail' on {ip}: {ex.Message}");
                    }
                    if (discoveryPlatform?.Id == "cisco-sb" && !DeviceIdRegex.IsMatch(cdpDetailOutput))
                    {
                        try
                        {
                            log("CDP", $"{ip}: no detailed neighbor records; trying 'show cdp entry *'.");
                            cdpDetailOutput = await ExecuteRawCommandAsync(stream, "show cdp entry *", token, timeoutMs: 25000);
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            log("CDP", $"Optional 'show cdp entry *' skipped on {ip}: {ex.Message}");
                        }
                    }

                    // 3. Tabular CDP (captures ports across 25G Twe, 10G Ten, 1G Gig, Fo 40G, Hu 100G)
                    string cdpTableOutput = string.Empty;
                    try
                    {
                        cdpTableOutput = await ExecuteRawCommandAsync(stream, "show cdp neighbors", token, timeoutMs: 20000);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        log("CDP", $"Error running 'show cdp neighbors' on {ip}: {ex.Message}");
                    }

                    // 4. LLDP Detail
                    string lldpDetailOutput = string.Empty;
                    try
                    {
                        lldpDetailOutput = await ExecuteRawCommandAsync(stream, "show lldp neighbors detail", token, timeoutMs: 15000);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        log("LLDP", $"Optional LLDP discovery on {ip}: {ex.Message}");
                    }

                    client.Disconnect();

                    progress(ip, 100, "Discovery complete", protocol, currentHost, false, parentHost, null);

                    var protocolsIpMap = ParseCdpProtocols(cdpProtocolsOutput);
                    var detailNeighbors = ParseCdpNeighborsDetail(cdpDetailOutput, currentHost, ip, protocolsIpMap);
                    var tableNeighbors = ParseCdpNeighborsTable(cdpTableOutput, currentHost, ip, protocolsIpMap);
                    var lldpNeighbors = ParseLldpNeighborsDetail(lldpDetailOutput, currentHost, ip);

                    var combinedNeighbors = CombineDiscoveredNeighbors(detailNeighbors, tableNeighbors, lldpNeighbors);
                    log("Discovery", $"{currentHost}: parsed {detailNeighbors.Count} CDP detail, {tableNeighbors.Count} CDP table and {lldpNeighbors.Count} LLDP neighbors.");

                    foreach (var neighbor in combinedNeighbors)
                    {
                        if (string.IsNullOrWhiteSpace(neighbor.DeviceName)) continue;

                        // CRITICAL: NEVER ATTEMPT SSH ON IP PHONES!
                        if (neighbor.IsPhone)
                        {
                            log("CDP", $"Found IP Phone: {neighbor.DeviceName} ({neighbor.IpAddress}) [{neighbor.Platform}] on {currentHost} ({neighbor.LocalPort} -> {neighbor.RemotePort}) - Skipped (No SSH).");
                            continue;
                        }

                        if (neighbor.IsEndpoint || !neighbor.IsNetworkDevice)
                        {
                            log("Discovery", $"Skipped {neighbor.DeviceName}: endpoint or no positive infrastructure evidence (capabilities: '{neighbor.Capabilities}').");
                            continue;
                        }
                        bool isAp = neighbor.IsAp;
                        if (isAp && !includeAps) continue;
                        if (!IsUsableDiscoveryAddress(neighbor.IpAddress)) neighbor.IpAddress = "";
                        bool validTarget = filterType != 0 || string.IsNullOrEmpty(neighbor.IpAddress) ||
                            IsIpInSubnetFilter(neighbor.IpAddress, filterValue);

                        if (validTarget)
                        {
                            var target = new DiscoveredTarget
                            {
                                IpAddress = neighbor.IpAddress,
                                Hostname = neighbor.DeviceName,
                                IsAp = isAp,
                                UplinkDevice = currentHost,
                                UplinkIp = ip,
                                LocalPort = neighbor.LocalPort,
                                RemotePort = neighbor.RemotePort
                            };

                            bool isNew = backupQueue.AddOrMerge(target);
                            if (isNew)
                            {
                                log("Discovery", $"Discovered {neighbor.DeviceName} ({target.IpAddress}) [{(isAp ? "AP" : "Switch")}] via {currentHost} ({target.Port})");
                                progress(target.IpAddress, 0, "Discovered via CDP/LLDP", "Waiting", neighbor.DeviceName, isAp, currentHost, target.Port);
                            }
                            // Scheduling claims are independent of inventory merges/promotions.
                            if (!isAp && IsUsableDiscoveryAddress(target.IpAddress))
                                enqueue(target.IpAddress, currentHost);
                            else if (!isAp)
                                log("Discovery", $"Cannot traverse {neighbor.DeviceName} via {currentHost}: no usable management IPv4 address in CDP/LLDP; retained in inventory.");
                        }
                    }

                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                progress(ip, 0, "Discovery Failed", "ERR", null, false, parentHost, null);
                log("Error", $"CDP/LLDP Discovery failed on {ip}: {ex.Message}");
            }
        }

        // Fixed task count, dynamically growing frontier. A node remains outstanding until
        // it has queued all children, so an empty queue cannot prematurely end discovery.
        private static async Task RunDiscoveryWorkersAsync(IEnumerable<string> seeds, int workers,
            Func<string, string?, Action<string, string?>, CancellationToken, Task> discover, CancellationToken token)
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            var queue = Channel.CreateUnbounded<(string Ip, string? Parent)>(new UnboundedChannelOptions
            { AllowSynchronousContinuations = false });
            var claimed = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            int outstanding = 0;
            void Enqueue(string ip, string? parent)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (!claimed.TryAdd(ip, 0)) return;
                Interlocked.Increment(ref outstanding);
                if (!queue.Writer.TryWrite((ip, parent)))
                {
                    Interlocked.Decrement(ref outstanding);
                    stop.Token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Discovery queue closed before all workers completed.");
                }
            }
            foreach (string seed in seeds) Enqueue(seed, null);
            if (outstanding == 0) queue.Writer.TryComplete();
            async Task Worker()
            {
                await Task.Yield();
                try
                {
                    await foreach (var item in queue.Reader.ReadAllAsync(stop.Token))
                    {
                        try { await discover(item.Ip, item.Parent, Enqueue, stop.Token); }
                        finally
                        {
                            if (Interlocked.Decrement(ref outstanding) == 0) queue.Writer.TryComplete();
                        }
                    }
                }
                catch
                {
                    stop.Cancel();
                    queue.Writer.TryComplete();
                    throw;
                }
            }
            await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Worker()));
        }

        public static bool IsUsableDiscoveryAddress(string address)
        {
            if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;
            var bytes = ip.GetAddressBytes();
            return bytes[0] is > 0 and < 224 && bytes[0] != 127 && !(bytes[0] == 169 && bytes[1] == 254);
        }

        private static List<CdpNeighbor> CombineDiscoveredNeighbors(
            List<CdpNeighbor> detail,
            List<CdpNeighbor> table,
            List<CdpNeighbor> lldp)
        {
            var list = new List<CdpNeighbor>();

            void MergeIntoList(CdpNeighbor n)
            {
                if (string.IsNullOrWhiteSpace(n.DeviceName)) return;

                var existing = list.FirstOrDefault(x =>
                    (HostKey.Same(x.DeviceName, n.DeviceName) &&
                        (!IsUsableDiscoveryAddress(x.IpAddress) || !IsUsableDiscoveryAddress(n.IpAddress) || x.IpAddress == n.IpAddress)) ||
                    (!string.IsNullOrEmpty(n.IpAddress) && !n.IpAddress.StartsWith("0.0.0.") && !n.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase) && x.IpAddress == n.IpAddress));

                if (existing == null)
                {
                    list.Add(n);
                }
                else
                {
                    if ((string.IsNullOrEmpty(existing.IpAddress) || existing.IpAddress.StartsWith("0.0.0.") || existing.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrEmpty(n.IpAddress) && !n.IpAddress.StartsWith("0.0.0.") && !n.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.IpAddress = n.IpAddress;
                    }

                    if ((string.IsNullOrEmpty(existing.Platform) || existing.Platform.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                        (DevicePlatforms.Recognize(existing.Platform) == null && DevicePlatforms.Recognize(n.Platform) != null)) &&
                        !string.IsNullOrEmpty(n.Platform) && !n.Platform.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Platform = n.Platform;
                    }

                    if (string.IsNullOrEmpty(existing.LocalPort) && !string.IsNullOrEmpty(n.LocalPort)) existing.LocalPort = n.LocalPort;
                    if (string.IsNullOrEmpty(existing.RemotePort) && !string.IsNullOrEmpty(n.RemotePort)) existing.RemotePort = n.RemotePort;

                    if (string.IsNullOrEmpty(existing.UplinkDevice) && !string.IsNullOrEmpty(n.UplinkDevice))
                    {
                        existing.UplinkDevice = n.UplinkDevice;
                        existing.UplinkIp = n.UplinkIp;
                    }

                    existing.IsPhone |= n.IsPhone;
                    existing.IsEndpoint |= n.IsEndpoint;
                    existing.IsNetworkDevice |= n.IsNetworkDevice;
                    existing.IsAp = !existing.IsEndpoint && (existing.IsAp || n.IsAp);
                    if (DevicePlatforms.Recognize(existing.Platform)?.Kind == "Switch") existing.IsAp = false;
                }
            }

            foreach (var n in detail) MergeIntoList(n);
            foreach (var n in table) MergeIntoList(n);
            foreach (var n in lldp) MergeIntoList(n);

            return list;
        }

        private static Dictionary<string, string> ParseCdpProtocols(string protocolsOutput)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(protocolsOutput)) return map;

            var blocks = Regex.Split(protocolsOutput, @"(?im)^\s*-{15,}\s*$|(?=^[ \t]*(?:Device[ -]ID:|Protocol information for ))");
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;
                var devMatch = DeviceIdRegex.Match(block);
                if (!devMatch.Success) devMatch = Regex.Match(block, @"(?im)^\s*Protocol information for\s+([^:\r\n]+):");
                if (!devMatch.Success) continue;
                string dev = devMatch.Groups[1].Value.Trim();

                var ipMatch = FallbackIpRegex.Match(block);
                if (ipMatch.Success)
                {
                    string ip = ipMatch.Groups[1].Value.Trim();
                    string key = HostKey.Of(dev);
                    if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                    {
                        map[key] = ip;
                    }
                }
            }
            return map;
        }

        private static List<CdpNeighbor> ParseCdpNeighborsDetail(string cdpOutput, string currentHost, string currentIp, Dictionary<string, string> protocolsIpMap)
        {
            var list = new List<CdpNeighbor>();
            if (string.IsNullOrWhiteSpace(cdpOutput)) return list;

            var blocks = Regex.Split(cdpOutput, @"(?im)^\s*-{15,}\s*$|(?=^[ \t]*Device[ -]ID:)");
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;

                var n = new CdpNeighbor
                {
                    UplinkDevice = currentHost,
                    UplinkIp = currentIp
                };

                var devMatch = DeviceIdRegex.Match(block);
                if (devMatch.Success) n.DeviceName = devMatch.Groups[1].Value.Trim().Split('.')[0];
                if (string.IsNullOrWhiteSpace(n.DeviceName)) continue;

                var ipMatch = PrimaryManagementIpRegex.Match(block);
                if (!ipMatch.Success) ipMatch = CdpIpRegex.Match(block);
                if (ipMatch.Success)
                {
                    n.IpAddress = ipMatch.Groups[1].Value.Trim();
                }
                else
                {
                    var fallback = FallbackIpRegex.Match(block);
                    if (fallback.Success) n.IpAddress = fallback.Groups[1].Value.Trim();
                }

                if (string.IsNullOrEmpty(n.IpAddress) && !string.IsNullOrEmpty(n.DeviceName))
                {
                    if (protocolsIpMap.TryGetValue(HostKey.Of(n.DeviceName), out var protIp))
                    {
                        n.IpAddress = protIp;
                    }
                }

                var platMatch = PlatformRegex.Match(block);
                if (platMatch.Success) n.Platform = platMatch.Groups[1].Value.Trim();

                var capMatch = CapabilitiesRegex.Match(block);
                if (capMatch.Success) n.Capabilities = capMatch.Groups[1].Value.Trim();

                var localMatch = Regex.Match(block, @"Interface:\s*([^,\r\n]+)", RegexOptions.IgnoreCase);
                if (localMatch.Success) n.LocalPort = localMatch.Groups[1].Value.Trim();

                var remoteMatch = Regex.Match(block, @"Port ID \(outgoing port\):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (remoteMatch.Success) n.RemotePort = remoteMatch.Groups[1].Value.Trim();

                ClassifyNeighbor(n);
                list.Add(n);
            }

            return list;
        }

        private static List<CdpNeighbor> ParseCdpNeighborsTable(string rawText, string currentHost, string currentIp, Dictionary<string, string> protocolsIpMap)
        {
            if (Regex.IsMatch(rawText, @"(?im)^\s*Device ID\s+Local\s+Adv\s+Time To\s+Capability"))
                return ParseSmallBusinessCdpTable(rawText, currentHost, currentIp, protocolsIpMap);
            var list = new List<CdpNeighbor>();
            if (string.IsNullOrWhiteSpace(rawText)) return list;

            string? pendingDeviceId = null;
            bool inTable = false;
            var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("Device ID", StringComparison.OrdinalIgnoreCase) &&
                    (line.Contains("Local Intrfce", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Local Interface", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Holdtme", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("Hldtme", StringComparison.OrdinalIgnoreCase)))
                {
                    inTable = true;
                    pendingDeviceId = null;
                    continue;
                }

                if (!inTable) continue;

                if (Regex.IsMatch(line, @"^[A-Za-z0-9_\-\.\(\)]+[\#\>]") ||
                    line.StartsWith("Total cdp entries", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("===") ||
                    line.StartsWith("---") ||
                    line.StartsWith("%") ||
                    line.StartsWith("show ", StringComparison.OrdinalIgnoreCase))
                {
                    inTable = false;
                    pendingDeviceId = null;
                    continue;
                }

                if (line.StartsWith("Capability Codes", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Holdtime", StringComparison.OrdinalIgnoreCase))
                {
                    pendingDeviceId = null;
                    continue;
                }

                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) continue;

                string deviceId;
                int tokenStartIndex;

                // Handle wrapped line in Cisco CLI where Device ID is on its own line
                if (tokens.Length == 1 && !char.IsDigit(tokens[0][0]))
                {
                    if (IsValidCiscoDeviceId(tokens[0]))
                    {
                        pendingDeviceId = tokens[0];
                    }
                    continue;
                }

                if (pendingDeviceId != null)
                {
                    deviceId = pendingDeviceId;
                    pendingDeviceId = null;
                    tokenStartIndex = 0;
                }
                else
                {
                    deviceId = tokens[0];
                    tokenStartIndex = 1;
                }

                if (!IsValidCiscoDeviceId(deviceId))
                {
                    continue;
                }

                // Find holdtime: first purely numeric token after tokenStartIndex within valid range 10..255
                int holdtimeIndex = -1;
                for (int i = tokenStartIndex; i < tokens.Length; i++)
                {
                    if (int.TryParse(tokens[i], out int ht) && ht >= 10 && ht <= 255)
                    {
                        holdtimeIndex = i;
                        break;
                    }
                }

                if (holdtimeIndex <= tokenStartIndex || holdtimeIndex >= tokens.Length - 1)
                {
                    continue;
                }

                string localIntf = string.Join(" ", tokens.Skip(tokenStartIndex).Take(holdtimeIndex - tokenStartIndex));
                if (!IsValidCiscoInterface(localIntf))
                {
                    continue;
                }

                var afterHold = tokens.Skip(holdtimeIndex + 1).ToArray();
                if (afterHold.Length < 2) continue;

                // Universal Multi-Token Port Parser: Recognize two-token interfaces for 25G Twe, 10G Ten, 1G Gig, Fo 40G, Hu 100G
                int portTokenCount = 1;
                if (afterHold.Length >= 3)
                {
                    string twoTokenPort = $"{afterHold[^2]} {afterHold[^1]}";
                    if (IsValidCiscoInterface(twoTokenPort) || (IsValidCiscoInterface(afterHold[^2]) && (char.IsDigit(afterHold[^1][0]) || afterHold[^1].Contains('/'))))
                    {
                        portTokenCount = 2;
                    }
                }

                string portId = string.Join(" ", afterHold.TakeLast(portTokenCount));
                if (!IsValidCiscoInterface(portId))
                {
                    continue;
                }

                var beforePort = afterHold.SkipLast(portTokenCount).ToArray();
                string platform = beforePort.Length >= 1 ? beforePort[^1] : "Unknown";
                string capabilities = beforePort.Length >= 2 ? string.Join(" ", beforePort.SkipLast(1)) : string.Empty;

                string hostKey = HostKey.Of(deviceId);
                string ipAddress = string.Empty;
                if (protocolsIpMap.TryGetValue(hostKey, out var protIp))
                {
                    ipAddress = protIp;
                }

                var n = new CdpNeighbor
                {
                    DeviceName = deviceId,
                    IpAddress = ipAddress,
                    Platform = platform,
                    Capabilities = capabilities,
                    LocalPort = localIntf,
                    RemotePort = portId,
                    UplinkDevice = currentHost,
                    UplinkIp = currentIp
                };

                ClassifyNeighbor(n);
                list.Add(n);
            }

            return list;
        }

        private static List<CdpNeighbor> ParseSmallBusinessCdpTable(string output, string currentHost, string currentIp,
            Dictionary<string, string> protocolsIpMap)
        {
            var neighbors = new List<CdpNeighbor>();
            Match[]? columns = null;
            string[]? cells = null;
            bool platformWrapped = false;
            void Finish()
            {
                if (cells == null || !IsValidCiscoDeviceId(cells[0])) return;
                var neighbor = new CdpNeighbor
                {
                    DeviceName = cells[0], LocalPort = cells[1], Capabilities = cells[4],
                    Platform = cells[5], RemotePort = cells[6], UplinkDevice = currentHost, UplinkIp = currentIp,
                    IpAddress = protocolsIpMap.GetValueOrDefault(HostKey.Of(cells[0]), "")
                };
                ClassifyNeighbor(neighbor);
                neighbors.Add(neighbor);
            }
            foreach (string line in output.Replace("\r", "").Split('\n'))
            {
                if (columns == null)
                {
                    // The separator supplies column positions even when headings span two lines.
                    if (Regex.IsMatch(line, @"^\s*-{3,}(?:\s+-{3,}){6}\s*$"))
                        columns = Regex.Matches(line, @"-{3,}").Cast<Match>().ToArray();
                    continue;
                }
                if (DevicePlatforms.IsPrompt(line.Trim()) || line.TrimStart().StartsWith("Total", StringComparison.OrdinalIgnoreCase)) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = columns.Select(c => line.Length <= c.Index ? "" :
                    line.Substring(c.Index, Math.Min(c.Length, line.Length - c.Index)).Trim()).ToArray();
                bool startsRow = IsValidCiscoInterface(parts[1]) && int.TryParse(parts[2], out _) && int.TryParse(parts[3], out _);
                if (startsRow)
                {
                    Finish();
                    cells = parts;
                    platformWrapped = parts[5].Length == columns[5].Length;
                }
                else if (cells != null && parts[1].Length == 0 && parts[2].Length == 0 && parts[3].Length == 0)
                {
                    cells[0] += parts[0];
                    cells[6] += parts[6];
                    if (parts[5].Length > 0)
                    {
                        cells[5] += (platformWrapped || cells[5].Length == 0 ? "" : " ") + parts[5];
                        platformWrapped = parts[5].Length == columns[5].Length;
                    }
                }
            }
            Finish();
            return neighbors;
        }

        private static List<CdpNeighbor> ParseLldpNeighborsDetail(string output, string currentHost, string currentIp)
        {
            var list = new List<CdpNeighbor>();
            if (string.IsNullOrWhiteSpace(output) || DevicePlatforms.IsRejected(output)) return list;
            // Cisco IOS uses dashed separators and Local Intf; NX-OS uses Local Port id.
            // Some releases omit separators, so local-interface headers also delimit records.
            var blocks = Regex.Split(output,
                @"(?im)^-\s*-{9,}.*$|(?=^[ \t]*(?:Local (?:Intf|Interface|Port id))[ \t]*:)");
            foreach (string block in blocks)
            {
                string Field(string label) => Regex.Match(block,
                    @"(?im)^[ \t]*(?:" + label + @")[ \t]*:[ \t]*([^\r\n]*)").Groups[1].Value.Trim();
                string name = Field("System Name");
                if (string.IsNullOrWhiteSpace(name) || name.Equals("not advertised", StringComparison.OrdinalIgnoreCase)) name = Field("Chassis id");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var enabled = Regex.Match(block, @"(?im)^[ \t]*Enabled Capabilities[ \t]*:[ \t]*([^\r\n]*)");
                var description = Regex.Match(block,
                    @"(?ims)^[ \t]*System Description[ \t]*:[ \t]*(.*?)(?=^[ \t]*(?:Time remaining|System Capabilities|Enabled Capabilities|Management Address|Auto Negotiation|Physical media|Vlan ID|Port id|Port Description|System Name)[^\r\n]*:|\z)");
                var management = Regex.Match(block,
                    @"(?im)^[ \t]*Management Address(?:es|\(es\))?[ \t]*:[ \t]*(?:\r?\n[ \t]*)?(?:(?:IP|IPv4)[ \t]*:[ \t]*)?([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                string ip = management.Groups[1].Value;
                var n = new CdpNeighbor
                {
                    DeviceName = name,
                    IpAddress = IsUsableDiscoveryAddress(ip) ? IPAddress.Parse(ip).ToString() : "",
                    Platform = description.Groups[1].Value.Trim(),
                    Capabilities = enabled.Success ? enabled.Groups[1].Value.Trim() : Field("System Capabilities"),
                    HasCapabilities = enabled.Success || Regex.IsMatch(block, @"(?im)^[ \t]*System Capabilities[ \t]*:"),
                    IsLldp = true,
                    LocalPort = Field("Local Intf|Local Interface|Local Port id"),
                    RemotePort = Field("Port id"),
                    UplinkDevice = currentHost,
                    UplinkIp = currentIp
                };
                ClassifyNeighbor(n);
                list.Add(n);
            }
            return list;
        }

        private static void ClassifyNeighbor(CdpNeighbor n)
        {
            bool Cap(string pattern) => Regex.IsMatch(n.Capabilities, pattern, RegexOptions.IgnoreCase);
            bool Model(string pattern) => Regex.IsMatch(n.Platform, pattern, RegexOptions.IgnoreCase);
            // LLDP: B=bridge, R=router, W=WLAN AP, T=telephone, S=station.
            // CDP: S=switch, R=router, T=transparent bridge, P=phone, H=host.
            bool phone = n.IsLldp ? Cap(@"\b(?:T|Telephone|Phone)\b") : Cap(@"\b(?:P|Phone)\b");
            n.IsPhone = phone || n.DeviceName.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) ||
                Model(@"\b(?:IP Phone|Telephone|ATA\d*|CP-\d+)\b");
            bool endpoint = n.IsLldp ? Cap(@"\b(?:S|Station(?:\s+Only)?|Host)\b") : Cap(@"\b(?:H|Host)\b");
            n.IsEndpoint = n.IsPhone || endpoint || Model(@"\b(?:Windows|VMware|ESXi|Hyper-V|Printer|Print Server|IP Camera)\b|^Linux\b");
            if (n.IsEndpoint) { n.IsAp = false; n.IsNetworkDevice = false; return; }

            bool switchOrRouter = n.IsLldp ? Cap(@"\b(?:B|R|Bridge|Router)\b") : Cap(@"\b(?:S|R|Switch|Router)\b");
            bool ap = n.IsLldp ? Cap(@"\b(?:W|WLAN|WLAN Access Point|Access[- ]Point)\b")
                : Cap(@"\b(?:W|T|Access[- ]Point|Trans[- ]?Bridge)\b");
            bool controller = Model(@"\b(?:AIR-CT[\w-]*|C9800[\w-]*|Wireless Controller)\b");
            bool apModel = Model(@"\b(?:AIR-(?:C|S)?AP[\w-]*|C91[\w-]*|CW91[\w-]*|AP[123456]G[\w-]*|Aironet|CAPWAP)\b");
            bool networkModel = controller || apModel || DevicePlatforms.Recognize(n.Platform) != null ||
                Model(@"\b(?:C(?:29|35|36|37|38|45|65|92|93|94|95|96)\d{2}[\w-]*|WS-C[\w-]+|N[3579]K-[\w-]+)\b");
            // Explicit enabled/system capabilities take precedence over model hints.
            // With no capability TLV, a recognized OS/model is the conservative fallback.
            bool capabilitiesPresent = n.HasCapabilities || !string.IsNullOrWhiteSpace(n.Capabilities);
            bool knownSwitch = DevicePlatforms.Recognize(n.Platform)?.Kind == "Switch";
            n.IsAp = !controller && !knownSwitch && (ap || (!capabilitiesPresent && apModel));
            n.IsNetworkDevice = ap || switchOrRouter || (!capabilitiesPresent && networkModel);
        }

        private static bool IsBuiltInTemplate(List<string> commands)
        {
            static IEnumerable<string> Normalize(IEnumerable<string> items) => items.Select(c => c.Trim())
                .Where(c => c.Length > 0 && !c.StartsWith('!'));
            return DevicePlatforms.All.Any(p => Normalize(p.Commands).SequenceEqual(Normalize(commands), StringComparer.OrdinalIgnoreCase));
        }

        private async Task PerformBackupWithFallbackAsync(
            string ip,
            List<CredentialEntry> candidateCredentials,
            bool isAp,
            string backupDir,
            List<string> commands,
            string profileName,
            bool writeConfig,
            LogAction log,
            ProgressAction progress,
            CancellationToken token,
            string? knownHostname = null,
            string? uplinkDevice = null,
            string? port = null,
            DeviceCapture? capture = null)
        {
            capture ??= new DeviceCapture { IpAddress = ip, Hostname = knownHostname ?? ip, Profile = profileName, SaveRequested = writeConfig };
            capture.CapturedAtUtc = DateTimeOffset.UtcNow;
            string protocol = "SSH/Telnet";
            progress(ip, 5, "Probing SSH (22) and Telnet (23)...", protocol, knownHostname, isAp, uplinkDevice, port);

            bool reachable = await _transport.IsReachableAsync(ip, token);
            if (!reachable)
            {
                capture.Status = "unreachable";
                capture.Reason = "TCP ports 22 and 23 timed out or refused.";
                progress(ip, 0, "Unreachable / Ports 22 and 23 Closed", "ERR", knownHostname, isAp, uplinkDevice, port);
                log("Error", $"Device {ip} is unreachable (TCP ports 22 and 23 timed out or refused).");
                return;
            }

            IBackupSession? client = null;
            CredentialEntry? activeCred = null;
            string? connectionFailure = null;

            foreach (var cred in candidateCredentials)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    progress(ip, 10, $"Authenticating ({cred.Username})...", protocol, knownHostname, isAp, uplinkDevice, port);
                    var testClient = await _transport.ConnectAsync(ip, cred, token);

                    client = testClient;
                    protocol = client.Protocol;
                    if (protocol == "Telnet") log("Transport", "SSH unavailable; using Telnet (unencrypted connection).");
                    activeCred = cred;
                    log("Auth", $"Device {ip} successfully authenticated with user '{cred.Username}'.");
                    break;
                }
                catch (SshAuthenticationException)
                {
                    log("Auth", $"Credential '{cred.Username}' failed for {ip}. Attempting fallback credential...");
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    connectionFailure = ex.Message;
                    log("Error", $"Connection error to {ip} with '{cred.Username}': {ex.Message}");
                    if (ex.Message.Contains("timed out") || ex.Message.Contains("No route") || ex.Message.Contains("refused"))
                    {
                        break;
                    }
                }
            }

            if (client == null || activeCred == null)
            {
                capture.Status = "failure";
                capture.Reason = connectionFailure ?? "All candidate credentials were rejected.";
                progress(ip, 0, connectionFailure == null ? "Auth Failed (All Creds)" : $"Connection failed: {connectionFailure}", "ERR", knownHostname, isAp, uplinkDevice, port);
                log("Error", $"All candidate credentials failed on {ip}.");
                return;
            }

            StringBuilder? diagnosticCapture = null;
            int capturedSuccessfulCommands = 0;
            string captureHostname = knownHostname ?? ip;
            IBackupSession? recoveryClient = null;
            IBackupShell? recoveryStream = null;
            try
            {
                using (client)
                {
                    progress(ip, 20, "Connected", protocol, knownHostname, isAp, uplinkDevice, port);
                    using var stream = client.OpenShell();

                    string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 10000, token);
                    progress(ip, 22, "Identifying device OS...", protocol, knownHostname, isAp, uplinkDevice, port);
                    DevicePlatform? platform = null;
                    var identityEvidence = new StringBuilder();
                    foreach (string probe in DevicePlatforms.Probes)
                    {
                        var probeCapture = new CommandCapture { Command = probe, Stage = "identity" };
                        capture.Commands.Add(probeCapture);
                        string response;
                        try { response = await ExecuteRawCommandAsync(stream, probe, token); }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            RecordAttempt(probeCapture, probe, null, ex);
                            throw;
                        }
                        string body = DevicePlatforms.Body(response, probe);
                        RecordAttempt(probeCapture, probe, body);
                        if (DevicePlatforms.IsRejected(body)) continue;
                        identityEvidence.AppendLine(body);
                        platform = DevicePlatforms.Recognize(body);
                        if (platform != null) break;
                    }
                    bool automatic = profileName.StartsWith("Auto", StringComparison.OrdinalIgnoreCase);
                    bool builtIn = automatic || (!profileName.Contains("Custom", StringComparison.OrdinalIgnoreCase) && IsBuiltInTemplate(commands));
                    if (platform == null && builtIn)
                        throw new InvalidOperationException("Device OS was not recognized; no automatic backup commands were sent. Use a reviewed custom script for this device.");
                    if (platform == null)
                        log("Warning", $"Device {ip} OS is unrecognized; executing the explicit custom script with response validation.");
                    else
                    {
                        capture.Platform = platform.Id;
                        capture.DeviceType = platform.Kind;
                        capture.Profile = builtIn ? platform.Name : profileName;
                        isAp = platform.Kind == "AP";
                        log("Detection", $"Device {ip}: {platform.Name} ({platform.Kind}).");
                        if (builtIn) commands = platform.Commands.ToList();
                    }
                    string prompt = platform?.UsesEnable == true
                        ? await ElevatePrivilegesAsync(stream, initial, activeCred.Secret, token) : initial;

                    string hostname = !string.IsNullOrWhiteSpace(knownHostname) ? knownHostname : ip;
                    var hostMatch = Regex.Match(prompt, @"(?:^|[\r\n])\s*(?:\(([\w\.\-\s]+?)\)|([\w\.\-]+?))(?:\([^\)]+\))?[>#]");
                    if (hostMatch.Success)
                    {
                        string candidate = !string.IsNullOrWhiteSpace(hostMatch.Groups[1].Value) ? hostMatch.Groups[1].Value : hostMatch.Groups[2].Value;
                        candidate = candidate.Trim();
                        if (!string.IsNullOrEmpty(candidate) &&
                            !candidate.Equals("Switch", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Equals("Router", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Equals("Cisco Controller", StringComparison.OrdinalIgnoreCase))
                        {
                            hostname = candidate;
                        }
                    }

                    progress(ip, 25, "Session Ready", protocol, hostname, isAp, uplinkDevice, port);
                    capture.Hostname = hostname;
                    capture.Protocol = protocol;
                    capture.IdentityEvidence = identityEvidence.ToString().Trim();
                    if (!string.IsNullOrEmpty(platform?.Paging))
                    {
                        var pagingCapture = new CommandCapture { Command = platform.Paging, Stage = "setup" };
                        capture.Commands.Add(pagingCapture);
                        string pagingOutput;
                        try { pagingOutput = await ExecuteRawCommandAsync(stream, platform.Paging, token); }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            RecordAttempt(pagingCapture, platform.Paging, null, ex);
                            ReportCommandFailure(ip, hostname, platform.Paging, ex.GetType().Name, ex.Message);
                            throw;
                        }
                        if (DevicePlatforms.IsRejected(DevicePlatforms.Body(pagingOutput, platform.Paging)))
                        {
                            ReportCommandFailure(ip, hostname, platform.Paging, "Paging rejected", DevicePlatforms.Body(pagingOutput, platform.Paging));
                            log("Warning", $"Paging command rejected on {ip}; interactive pager handling remains enabled.");
                        }
                        RecordAttempt(pagingCapture, platform.Paging, DevicePlatforms.Body(pagingOutput, platform.Paging));
                    }
                    string deviceTypeLabel = platform == null ? "Unrecognized (custom script)" : $"{platform.Name} {platform.Kind}";

                    var sb = new StringBuilder();
                    diagnosticCapture = sb;
                    captureHostname = hostname;
                    sb.AppendLine("==================================================");
                    sb.AppendLine($"Backup Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Device IP   : {ip}");
                    sb.AppendLine($"Hostname    : {hostname}");
                    sb.AppendLine($"Device Type : {deviceTypeLabel}");
                    if (!string.IsNullOrWhiteSpace(uplinkDevice))
                    {
                        sb.AppendLine($"Uplink Dev  : {uplinkDevice}");
                    }
                    if (!string.IsNullOrWhiteSpace(port))
                    {
                        sb.AppendLine($"Port Link   : {port}");
                    }
                    sb.AppendLine($"Profile     : {(builtIn ? platform?.Name : profileName)}");
                    sb.AppendLine($"Detected OS : {platform?.Name ?? "Unknown"}");
                    sb.AppendLine($"Selection   : {(builtIn ? "Automatic platform template" : "Explicit custom commands")}");
                    sb.AppendLine("Identity evidence:");
                    sb.AppendLine(identityEvidence.ToString().TrimEnd());
                    sb.AppendLine($"Auth User   : {activeCred.Username}");
                    sb.AppendLine($"Protocol    : {protocol} (Interactive Shell)");
                    sb.AppendLine("==================================================\n");

                    var validCommands = new List<string>();
                    foreach (var c in commands)
                    {
                        var trimmed = c.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("!"))
                            continue;

                        // Configuration push is suppressed until further notice
                        if (IsConfigurationPushCommand(trimmed))
                        {
                            log("Safety", $"[Suppressed] Configuration command '{trimmed}' blocked on {ip}. Configuration push is disabled until further notice.");
                            continue;
                        }

                        if (builtIn && DevicePlatforms.All.Any(p => trimmed == p.Paging)) continue; // Platform paging was applied after identification.
                        if (!builtIn || !validCommands.Contains(trimmed)) validCommands.Add(trimmed);
                    }

                    if (validCommands.Count == 0)
                    {
                        if (platform == null) throw new InvalidOperationException("No permitted commands for an unidentified device.");
                        validCommands.Add(platform.Configuration);
                    }
                    int rejected = 0;
                    int successful = 0;
                    bool configurationCaptured = false;
                    capture.Commands.RemoveAll(c => c.Stage == "capture");
                    var planned = validCommands.Select(c => new CommandCapture { Command = c }).ToList();
                    capture.Commands.AddRange(planned);
                    bool needsRecovery = false;
                    async Task<string> ExecuteCaptureAsync(string command, int timeoutMs)
                    {
                        if (needsRecovery)
                        {
                            recoveryStream?.Dispose();
                            recoveryStream = null;
                            recoveryClient?.Dispose();
                            recoveryClient = null;
                            recoveryClient = await _transport.ConnectAsync(ip, activeCred, token);
                            recoveryStream = recoveryClient.OpenShell();
                            string recoveryPrompt = await ReadUntilPromptOrPatternAsync(recoveryStream, PromptRegex, 10000, token);
                            if (platform?.UsesEnable == true)
                                await ElevatePrivilegesAsync(recoveryStream, recoveryPrompt, activeCred.Secret, token);
                            if (!string.IsNullOrEmpty(platform?.Paging))
                                await ExecuteRawCommandAsync(recoveryStream, platform.Paging, token);
                            needsRecovery = false;
                            log("Transport", $"Opened a fresh session on {ip} to continue after an incomplete command response.");
                        }
                        return await ExecuteRawCommandAsync(recoveryStream ?? stream, command, token, timeoutMs);
                    }

                    for (int i = 0; i < validCommands.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string cmd = validCommands[i];

                        int pct = 25 + (int)(((double)(i + 1) / validCommands.Count) * 65);
                        progress(ip, pct, $"Running: {cmd}", protocol, hostname, isAp, uplinkDevice, port);

                        bool accepted = false;
                        foreach (string attempt in DevicePlatforms.Alternatives(platform, cmd))
                        {
                            bool configurationCommand = cmd == platform?.Configuration || cmd is "show run" or "show running-config" or "show run-config" or "show run-config commands";
                            string output;
                            try
                            {
                                output = await ExecuteCaptureAsync(attempt, configurationCommand ? 120000 : 20000);
                            }
                            catch (Exception ex) when (!token.IsCancellationRequested)
                            {
                                ReportCommandFailure(ip, hostname, attempt, ex.GetType().Name, ex.Message);
                                sb.AppendLine($"Command: {attempt}\nResult: Failed\n{ex.Message}\n");
                                RecordAttempt(planned[i], attempt, null, ex);
                                needsRecovery = true;
                                log("Warning", $"Command '{attempt}' failed on {ip}; later commands will use a fresh session.");
                                break;
                            }
                            string body = DevicePlatforms.Body(output, attempt);
                            accepted = !DevicePlatforms.IsRejected(body) && !string.IsNullOrWhiteSpace(body);
                            RecordAttempt(planned[i], attempt, body);
                            sb.AppendLine("==================================================");
                            sb.AppendLine($"Command: {attempt}");
                            sb.AppendLine($"Result: {(accepted ? "Accepted" : "Rejected or empty")}");
                            sb.AppendLine("==================================================");
                            sb.AppendLine(output.TrimEnd());
                            sb.AppendLine();
                            if (accepted) break;
                            ReportCommandFailure(ip, hostname, attempt, string.IsNullOrWhiteSpace(body) ? "Empty response" : "Rejected", body);
                            log("Warning", $"Command '{attempt}' rejected or empty on {ip}.");
                        }
                        if (accepted)
                        {
                            successful++;
                            capturedSuccessfulCommands = successful;
                            if (cmd == platform?.Configuration || cmd is "show run" or "show running-config" or "show run-config" or "show run-config commands")
                                configurationCaptured = true;
                            capture.ConfigurationCaptured = configurationCaptured;
                        }
                        else rejected++;
                    }

                    if (successful == 0)
                        throw new InvalidOperationException("No usable configuration backup was captured; previous backup preserved.");
                    if ((builtIn || validCommands.Contains(platform?.Configuration ?? "") || validCommands.Any(c => c is "show run" or "show running-config" or "show run-config" or "show run-config commands")) && !configurationCaptured)
                    {
                        await SavePartialCaptureAsync(backupDir, ip, hostname, sb.ToString(), token);
                        capture.OutputFile = PartialCaptureFilename(ip, hostname);
                        capture.Status = "partial";
                        capture.Reason = "Required configuration output was unavailable; diagnostics only.";
                        progress(ip, 100, $"Partial diagnostics saved: configuration unavailable ({successful} accepted, {rejected} rejected). See Command Results.", protocol, hostname, isAp, uplinkDevice, port);
                        log("Warning", $"Partial diagnostics saved for {ip}; previous configuration backup preserved.");
                        return;
                    }

                    if (writeConfig && !isAp && platform?.Save == "write memory")
                    {
                        var saveCapture = new CommandCapture { Command = "write memory", Stage = "save" };
                        capture.Commands.Add(saveCapture);
                        progress(ip, 95, "Saving Config (write memory)...", protocol, hostname, isAp, uplinkDevice, port);
                        try
                        {
                            string saveOutput = await ExecuteCaptureAsync("write memory", 25000);
                            RecordAttempt(saveCapture, "write memory", DevicePlatforms.Body(saveOutput, "write memory"));
                            sb.AppendLine("==================================================");
                            sb.AppendLine("Command: write memory");
                            sb.AppendLine("==================================================");
                            if (DevicePlatforms.IsRejected(DevicePlatforms.Body(saveOutput, "write memory")) ||
                                string.IsNullOrWhiteSpace(DevicePlatforms.Body(saveOutput, "write memory")))
                            {
                                rejected++;
                                ReportCommandFailure(ip, hostname, "write memory", "Save rejected", DevicePlatforms.Body(saveOutput, "write memory"));
                                log("Warning", $"Save configuration command rejected on {ip}.");
                            }
                            sb.AppendLine(saveOutput.TrimEnd());
                            sb.AppendLine();
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            rejected++;
                            RecordAttempt(saveCapture, "write memory", null, ex);
                            ReportCommandFailure(ip, hostname, "write memory", ex.GetType().Name, ex.Message);
                            log("Warning", $"Failed to save memory on {ip}: {ex.Message}");
                        }
                    }

                    if (writeConfig && !isAp && platform?.Save != "write memory")
                        log("Warning", $"Save configuration skipped on {ip}: no validated non-interactive save command for this OS.");
                    client.Disconnect();

                    string safeHostname = Regex.Replace(hostname, @"[^\w\.\-]", "_");
                    string devTypeSuffix = platform?.Kind ?? "Unknown";
                    string filename = Path.Combine(backupDir, $"{ip}_{safeHostname}_{devTypeSuffix}.txt");
                    string temporaryFile = filename + $".{Guid.NewGuid():N}.tmp";
                    try
                    {
                        await File.WriteAllTextAsync(temporaryFile, sb.ToString(), token);
                        token.ThrowIfCancellationRequested();
                        File.Move(temporaryFile, filename, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
                    }

                    capture.OutputFile = Path.GetFileName(filename);
                    capture.Status = rejected > 0 || !configurationCaptured ? "partial" : "success";
                    capture.Reason = rejected > 0 ? "One or more commands were unsupported or failed; inspect per-command results."
                        : !configurationCaptured ? "Diagnostic capture did not include configuration." : null;
                    progress(ip, 100, $"Completed{(rejected > 0 ? " with warnings" : "")} ({successful} accepted, {rejected} rejected)", protocol, hostname, isAp, uplinkDevice, port);
                    log("Backup", $"Backup captured ({successful} accepted, {rejected} rejected) for {hostname} ({ip}) -> {Path.GetFileName(filename)}");
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                capture.Status = "failure";
                capture.Reason = ex.Message;
                progress(ip, 0, $"Failed: {ex.Message}", "ERR", knownHostname, isAp, uplinkDevice, port);
                log("Error", $"Backup failed on {ip}: {ex.Message}");
                if (capturedSuccessfulCommands > 0 && diagnosticCapture != null)
                {
                    try
                    {
                        await SavePartialCaptureAsync(backupDir, ip, captureHostname, diagnosticCapture.ToString(), token);
                        capture.OutputFile = PartialCaptureFilename(ip, captureHostname);
                        capture.Status = "partial";
                        log("Warning", $"Saved commands captured before the failure on {ip} as partial diagnostics.");
                    }
                    catch (Exception saveError) when (!token.IsCancellationRequested)
                    {
                        log("Error", $"Could not save partial diagnostics on {ip}: {saveError.Message}");
                    }
                }
            }
            finally
            {
                recoveryStream?.Dispose();
                recoveryClient?.Dispose();
            }
        }

        private static void RecordAttempt(CommandCapture capture, string command, string? body, Exception? exception = null)
        {
            bool rejected = body != null && DevicePlatforms.IsRejected(body);
            string status = exception != null || string.IsNullOrWhiteSpace(body) ? "failure"
                : rejected ? (DevicePlatforms.IsUnsupported(body!) ? "unsupported" : "failure") : "success";
            string? reason = exception != null ? $"{exception.GetType().Name}: {exception.Message}"
                : string.IsNullOrWhiteSpace(body) ? "Empty command response." : rejected ? body!.Trim() : null;
            if (reason?.Length > 1000) reason = reason[..1000];
            capture.Attempts.Add(new CommandAttempt(command, DateTimeOffset.UtcNow, status, reason));
            capture.Status = status;
            capture.Reason = reason;
        }

        private void ReportCommandFailure(string ip, string hostname, string command, string error, string response)
        {
            string detail = string.IsNullOrWhiteSpace(response) ? "Device returned no command output." : response.Trim();
            if (detail.Length > 4000) detail = detail[..4000] + "\n[Response truncated to 4000 characters]";
            CommandFailed?.Invoke(new CommandFailure(DateTimeOffset.Now, ip, hostname, command, error, detail));
        }

        private static async Task SavePartialCaptureAsync(string directory, string ip, string hostname, string text, CancellationToken token)
        {
            string path = Path.Combine(directory, PartialCaptureFilename(ip, hostname));
            string temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, "PARTIAL DIAGNOSTICS — NOT A COMPLETE CONFIGURATION BACKUP\nPrevious configuration backup preserved. Review Command Results for failed commands.\n\n" + text, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string PartialCaptureFilename(string ip, string hostname) =>
            $"{ip}_{Regex.Replace(hostname, @"[^\w.\-]", "_")}.partial-diagnostics.txt";

        private async Task<string> ElevatePrivilegesAsync(IBackupShell stream, string initialOutput, string secret, CancellationToken token)
        {
            if (initialOutput.TrimEnd().EndsWith("#"))
            {
                return initialOutput;
            }

            if (!string.IsNullOrEmpty(secret))
            {
                FlushStream(stream);
                stream.WriteLine("enable");

                string response = await ReadUntilPromptOrPatternAsync(stream, new Regex(@"[Pp]assword:|[>#]\s*$", RegexOptions.Compiled), 8000, token);

                if (PasswordRegex.IsMatch(response))
                {
                    FlushStream(stream);
                    stream.WriteLine(secret);
                    return await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 8000, token);
                }
                return response;
            }

            return initialOutput;
        }

        private async Task<string> ExecuteRawCommandAsync(IBackupShell stream, string command, CancellationToken token, int timeoutMs = 20000)
        {
            token.ThrowIfCancellationRequested();
            FlushStream(stream);
            stream.WriteLine(command);
            return await ReadUntilPromptOrPatternAsync(stream, PromptRegex, timeoutMs, token);
        }

        private void FlushStream(IBackupShell stream)
        {
            while (stream.DataAvailable)
            {
                stream.Read();
            }
        }

        private async Task<string> ReadUntilPromptOrPatternAsync(IBackupShell stream, Regex pattern, int timeoutMs, CancellationToken token)
        {
            var sb = new StringBuilder();
            var timer = Stopwatch.StartNew();
            int handledPagerEnd = 0;

            while (timer.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                if (stream.DataAvailable)
                {
                    string chunk = stream.Read();
                    sb.Append(chunk);

                    int searchLen = Math.Min(sb.Length, 512);
                    string rawTail = sb.ToString(sb.Length - searchLen, searchLen);
                    string tail = Regex.Replace(rawTail, @"\x1B\[[0-?]*[ -/]*[@-~]|\x08", "");
                    if (pattern.IsMatch(tail)) return sb.ToString();

                    // Pager text can be split across SSH reads. Respond once per new marker.
                    int tailStart = sb.Length - searchLen;
                    var pager = MoreRegex.Matches(rawTail).Cast<Match>()
                        .FirstOrDefault(m => tailStart + m.Index + m.Length > handledPagerEnd);
                    if (pager != null)
                    {
                        handledPagerEnd = tailStart + pager.Index + pager.Length;
                        string reply = pager.Value.Contains("(y/n)", StringComparison.OrdinalIgnoreCase) ? "y"
                            : pager.Value.Contains("Enter", StringComparison.OrdinalIgnoreCase) ? "\r" : " ";
                        stream.Write(reply);
                    }
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }

            token.ThrowIfCancellationRequested();
            throw new TimeoutException($"No device prompt received within {timeoutMs} ms.");
        }

        private bool IsIpInSubnetFilter(string ipStr, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;

            filter = filter.Trim();
            if (filter.Contains("/"))
            {
                try
                {
                    var parts = filter.Split('/');
                    var targetIp = IPAddress.Parse(ipStr);
                    var filterIp = IPAddress.Parse(parts[0]);
                    int cidr = int.Parse(parts[1]);

                    uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
                    mask = BitConverter.ToUInt32(BitConverter.GetBytes(mask), 0);

                    byte[] targetBytes = targetIp.GetAddressBytes();
                    byte[] filterBytes = filterIp.GetAddressBytes();

                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(targetBytes);
                        Array.Reverse(filterBytes);
                    }

                    uint targetInt = BitConverter.ToUInt32(targetBytes, 0);
                    uint filterInt = BitConverter.ToUInt32(filterBytes, 0);

                    return (targetInt & mask) == (filterInt & mask);
                }
                catch
                {
                    return ipStr.StartsWith(filter.Split('/')[0]);
                }
            }

            return ipStr.StartsWith(filter);
        }

        /// <summary>
        /// Safety guard: Intercepts and blocks commands attempting to enter configuration mode
        /// or mutate device configuration. Configuration push is suppressed until further notice.
        /// </summary>
        public static bool IsConfigurationPushCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            string cmd = command.Trim();

            // Allow AireOS terminal paging commands
            if (cmd.Equals("config paging disable", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("config paging enable", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("set cli screen-length 0", StringComparison.OrdinalIgnoreCase)) return false;
            if (Regex.IsMatch(cmd, @"^(?:conf(?:ig(?:ure)?)?|system-view|set|delete|commit|save|write|copy|reload|reboot|erase|debug|clear|reset|format)(?:\s|$)", RegexOptions.IgnoreCase)) return true;

            // Check common configuration mode entry variants
            if (cmd.Equals("conf t", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("conf term", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("config t", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("config term", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("configure terminal", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("configure", StringComparison.OrdinalIgnoreCase) ||
                cmd.StartsWith("conf t ", StringComparison.OrdinalIgnoreCase) ||
                cmd.StartsWith("configure ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public class DiscoveredTarget
        {
            public string IpAddress { get; set; } = string.Empty;
            public string Hostname { get; set; } = string.Empty;
            public bool IsAp { get; set; } = false;
            public string UplinkDevice { get; set; } = string.Empty;
            public string UplinkIp { get; set; } = string.Empty;
            public string LocalPort { get; set; } = string.Empty;
            public string RemotePort { get; set; } = string.Empty;
            public string Port => string.IsNullOrEmpty(LocalPort) ? RemotePort : (string.IsNullOrEmpty(RemotePort) ? LocalPort : $"{LocalPort} -> {RemotePort}");
        }

        private class CdpNeighbor
        {
            public string DeviceName { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public string Platform { get; set; } = string.Empty;
            public string Capabilities { get; set; } = string.Empty;
            public string LocalPort { get; set; } = string.Empty;
            public string RemotePort { get; set; } = string.Empty;
            public string UplinkDevice { get; set; } = string.Empty;
            public string UplinkIp { get; set; } = string.Empty;
            public bool IsAp { get; set; } = false;
            public bool IsPhone { get; set; } = false;
            public bool IsLldp { get; set; }
            public bool HasCapabilities { get; set; }
            public bool IsEndpoint { get; set; }
            public bool IsNetworkDevice { get; set; }
        }
    }
}

