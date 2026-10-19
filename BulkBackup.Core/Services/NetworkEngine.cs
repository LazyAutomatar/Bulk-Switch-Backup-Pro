using BulkBackup.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Common;
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

namespace BulkBackup.Core.Services
{
    public class NetworkEngine
    {
        public delegate void LogAction(string source, string message);
        public delegate void ProgressAction(string ip, int percentage, string status, string protocol, string? hostname = null, bool isAp = false, string? uplink = null, string? port = null);

        private static readonly Regex PromptRegex = new(@"(?:^|[\r\n])\s*(?:\([\w\.\-\s]+\)|[\w\.\-]+)(?:\([^\)]+\))?[>#]\s*$", RegexOptions.Compiled);
        private static readonly Regex PasswordRegex = new(@"[Pp]assword:\s*$", RegexOptions.Compiled);
        private static readonly Regex MoreRegex = new(@"--\s*More\s*--|Press any key to continue", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DeviceIdRegex = new(@"Device ID:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlatformRegex = new(@"Platform:\s*([^,\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CapabilitiesRegex = new(@"Capabilities:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CdpIpRegex = new(@"(?:Entry address\(es\):|Management address\(es\):)[\s\S]*?(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FallbackIpRegex = new(@"(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            if (candidateCredentials == null || candidateCredentials.Count == 0)
            {
                throw new ArgumentException("At least one credential must be provided.");
            }

            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                destinationDirectory = Path.Combine(Environment.CurrentDirectory, "Switch_Backups");
            }
            Directory.CreateDirectory(destinationDirectory);

            logCallback("System", $"Starting Bulk Switch Backup Pro (Profile: {profileName}, Threads: {maxConcurrency}, Credentials: {candidateCredentials.Count})...");

            var devicesToBackup = new ConcurrentDictionary<string, DiscoveredTarget>(StringComparer.OrdinalIgnoreCase);
            var visitedDiscovery = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (enableCdp)
            {
                logCallback("System", "Initiating Recursive CDP/LLDP Discovery Phase (IP Phones automatically excluded)...");
                var discoveryTasks = new List<Task>();
                foreach (var ip in targetIps)
                {
                    AddOrMergeDiscoveredTarget(devicesToBackup, new DiscoveredTarget { IpAddress = ip, Hostname = ip, IsAp = false });
                    discoveryTasks.Add(PerformDiscoveryAsync(ip, candidateCredentials, filterType, filterValue, includeAps, devicesToBackup, visitedDiscovery, logCallback, progressCallback, token));
                }
                await Task.WhenAll(discoveryTasks);
                logCallback("System", $"CDP/LLDP Discovery Phase complete. Discovered {devicesToBackup.Count} total valid targets.");
            }
            else
            {
                foreach (var ip in targetIps)
                {
                    AddOrMergeDiscoveredTarget(devicesToBackup, new DiscoveredTarget { IpAddress = ip, Hostname = ip, IsAp = false });
                }
            }

            logCallback("System", $"Initiating Parallel Backup Phase across {devicesToBackup.Count} targets (Up to {maxConcurrency} concurrent workers)...");

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxConcurrency),
                CancellationToken = token
            };

            await Parallel.ForEachAsync(devicesToBackup.Values, parallelOptions, async (target, ct) =>
            {
                if (string.IsNullOrWhiteSpace(target.IpAddress) ||
                    target.IpAddress.StartsWith("0.0.0.") ||
                    target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                {
                    logCallback("Discovery", $"Device '{target.Hostname}' on {target.UplinkDevice} ({target.Port}) has no advertised IP address. Skipping SSH backup.");
                    progressCallback(target.Hostname, 0, "Skipped (No IP)", "L2", target.Hostname, target.IsAp, target.UplinkDevice, target.Port);
                    return;
                }

                var cmds = target.IsAp ? CommandProfiles.CiscoApCommands.ToList() : customCommands;
                await PerformBackupWithFallbackAsync(target.IpAddress, candidateCredentials, target.IsAp, destinationDirectory, cmds, profileName, writeConfig, logCallback, progressCallback, ct, target.Hostname, target.UplinkDevice, target.Port);
            });

            logCallback("System", $"All backups completed! Configurations stored directly in: {destinationDirectory}");
            return destinationDirectory;
        }

        public static async Task<bool> IsSshPortReachableAsync(string host, int port = 22, int timeoutMs = 2500, CancellationToken token = default)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeoutMs);

                await client.ConnectAsync(host, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private async Task PerformDiscoveryAsync(
            string ip,
            List<CredentialEntry> candidateCredentials,
            int filterType,
            string filterValue,
            bool includeAps,
            ConcurrentDictionary<string, DiscoveredTarget> backupQueue,
            ConcurrentDictionary<string, bool> visited,
            LogAction log,
            ProgressAction progress,
            CancellationToken token,
            string? parentHost = null)
        {
            if (visited.ContainsKey(ip)) return;
            visited.TryAdd(ip, true);

            progress(ip, 5, "Probing SSH (Port 22)...", "SSH", null, false, parentHost, null);
            bool reachable = await IsSshPortReachableAsync(ip, 22, 2500, token);
            if (!reachable)
            {
                progress(ip, 0, "Unreachable / Port 22 Closed", "ERR", null, false, parentHost, null);
                log("Error", $"Seed {ip} is unreachable on port 22 (timeout or connection refused). Skipping CDP discovery.");
                return;
            }

            progress(ip, 15, "Discovering CDP/LLDP neighbors...", "SSH", null, false, parentHost, null);

            SshClient? client = null;
            CredentialEntry? activeCred = null;

            foreach (var cred in candidateCredentials)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var testClient = new SshClient(ip, cred.Username, cred.Password);
                    testClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(7);
                    await Task.Run(() => testClient.Connect(), token);

                    client = testClient;
                    activeCred = cred;
                    break;
                }
                catch (SshAuthenticationException)
                {
                    log("Auth", $"Credential '{cred.Username}' rejected by {ip}. Trying next stored credential...");
                }
                catch (Exception ex)
                {
                    log("Error", $"Connection error to {ip} with '{cred.Username}': {ex.Message}");
                    break;
                }
            }

            if (client == null || activeCred == null)
            {
                progress(ip, 0, "Discovery Auth Failed", "ERR", null, false, parentHost, null);
                log("Error", $"All credentials failed on seed {ip}.");
                return;
            }

            try
            {
                using (client)
                {
                    using var stream = client.CreateShellStream("vt100", 255, 50, 800, 600, 4096);
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
                    var selfTarget = backupQueue.Values.FirstOrDefault(t => t.IpAddress == ip);
                    if (selfTarget != null && (string.IsNullOrWhiteSpace(selfTarget.Hostname) || selfTarget.Hostname == selfTarget.IpAddress))
                    {
                        selfTarget.Hostname = currentHost;
                    }

                    await ExecuteRawCommandAsync(stream, "terminal length 0", token);

                    // 1. Protocols table: map Device ID -> IPv4 address fast and reliably
                    string cdpProtocolsOutput = string.Empty;
                    try
                    {
                        cdpProtocolsOutput = await ExecuteRawCommandAsync(stream, "show cdp entry * protocols", token, timeoutMs: 12000);
                    }
                    catch (Exception ex)
                    {
                        log("CDP", $"Optional 'show cdp entry * protocols' skipped on {ip}: {ex.Message}");
                    }

                    // 2. Detailed CDP
                    string cdpDetailOutput = string.Empty;
                    try
                    {
                        cdpDetailOutput = await ExecuteRawCommandAsync(stream, "show cdp neighbors detail", token, timeoutMs: 25000);
                    }
                    catch (Exception ex)
                    {
                        log("CDP", $"Error running 'show cdp neighbors detail' on {ip}: {ex.Message}");
                    }

                    // 3. Tabular CDP (captures ports across 25G Twe, 10G Ten, 1G Gig, Fo 40G, Hu 100G)
                    string cdpTableOutput = string.Empty;
                    try
                    {
                        cdpTableOutput = await ExecuteRawCommandAsync(stream, "show cdp neighbors", token, timeoutMs: 20000);
                    }
                    catch (Exception ex)
                    {
                        log("CDP", $"Error running 'show cdp neighbors' on {ip}: {ex.Message}");
                    }

                    // 4. LLDP Detail
                    string lldpDetailOutput = string.Empty;
                    try
                    {
                        lldpDetailOutput = await ExecuteRawCommandAsync(stream, "show lldp neighbors detail", token, timeoutMs: 15000);
                    }
                    catch
                    {
                        // LLDP is optional if switch doesn't have it enabled
                    }

                    client.Disconnect();

                    progress(ip, 100, "Discovery complete", "SSH", currentHost, false, parentHost, null);

                    var protocolsIpMap = ParseCdpProtocols(cdpProtocolsOutput);
                    var detailNeighbors = ParseCdpNeighborsDetail(cdpDetailOutput, currentHost, ip, protocolsIpMap);
                    var tableNeighbors = ParseCdpNeighborsTable(cdpTableOutput, currentHost, ip, protocolsIpMap);
                    var lldpNeighbors = ParseLldpNeighborsDetail(lldpDetailOutput, currentHost, ip);

                    var combinedNeighbors = CombineDiscoveredNeighbors(detailNeighbors, tableNeighbors, lldpNeighbors);
                    var subTasks = new List<Task>();

                    foreach (var neighbor in combinedNeighbors)
                    {
                        if (string.IsNullOrWhiteSpace(neighbor.DeviceName) || !IsValidCiscoDeviceId(neighbor.DeviceName)) continue;

                        // CRITICAL: NEVER ATTEMPT SSH ON IP PHONES!
                        if (neighbor.IsPhone)
                        {
                            log("CDP", $"Found IP Phone: {neighbor.DeviceName} ({neighbor.IpAddress}) [{neighbor.Platform}] on {currentHost} ({neighbor.LocalPort} -> {neighbor.RemotePort}) - Skipped (No SSH).");
                            continue;
                        }

                        bool isAp = neighbor.IsAp;
                        if (isAp && !includeAps) continue;

                        bool validTarget = isAp;
                        if (!isAp)
                        {
                            if (filterType == 0 && !string.IsNullOrWhiteSpace(neighbor.IpAddress) &&
                                !neighbor.IpAddress.StartsWith("0.0.0.") &&
                                !neighbor.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                            {
                                validTarget = IsIpInSubnetFilter(neighbor.IpAddress, filterValue);
                            }
                            else
                            {
                                validTarget = true;
                            }
                        }

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

                            bool isNew = AddOrMergeDiscoveredTarget(backupQueue, target);
                            if (isNew)
                            {
                                log("CDP", $"Discovered {neighbor.DeviceName} ({target.IpAddress}) [{(isAp ? "AP" : "Switch")}] via {currentHost} ({target.Port})");
                                progress(target.IpAddress, 0, "Discovered via CDP", "Waiting", neighbor.DeviceName, isAp, currentHost, target.Port);

                                // Recurse only if switch has a valid, routable IP address
                                if (!isAp && !string.IsNullOrWhiteSpace(target.IpAddress) &&
                                    !target.IpAddress.StartsWith("0.0.0.") &&
                                    !target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                                {
                                    subTasks.Add(PerformDiscoveryAsync(target.IpAddress, candidateCredentials, filterType, filterValue, includeAps, backupQueue, visited, log, progress, token, currentHost));
                                }
                            }
                        }
                    }

                    await Task.WhenAll(subTasks);
                }
            }
            catch (Exception ex)
            {
                progress(ip, 0, "Discovery Failed", "ERR", null, false, parentHost, null);
                log("Error", $"CDP/LLDP Discovery failed on {ip}: {ex.Message}");
            }
        }

        private static bool AddOrMergeDiscoveredTarget(
            ConcurrentDictionary<string, DiscoveredTarget> dict,
            DiscoveredTarget target)
        {
            string hostKey = HostKey.Of(target.Hostname);
            DiscoveredTarget? existing = null;

            if (!string.IsNullOrWhiteSpace(target.IpAddress) &&
                !target.IpAddress.StartsWith("0.0.0.") &&
                !target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
            {
                dict.TryGetValue(target.IpAddress, out existing);
            }

            if (existing == null && !string.IsNullOrEmpty(hostKey))
            {
                existing = dict.Values.FirstOrDefault(t => HostKey.Same(t.Hostname, target.Hostname));
            }

            if (existing != null)
            {
                // Promote placeholder to real IP if discovered
                if ((string.IsNullOrWhiteSpace(existing.IpAddress) || existing.IpAddress.StartsWith("0.0.0.") || existing.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(target.IpAddress) && !target.IpAddress.StartsWith("0.0.0.") && !target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
                {
                    var oldKeys = dict.Where(kvp => kvp.Value == existing).Select(kvp => kvp.Key).ToList();
                    foreach (var k in oldKeys) dict.TryRemove(k, out _);

                    existing.IpAddress = target.IpAddress;
                    dict[existing.IpAddress] = existing;
                }

                if (string.IsNullOrWhiteSpace(existing.Hostname) || existing.Hostname == existing.IpAddress)
                {
                    if (!string.IsNullOrWhiteSpace(target.Hostname)) existing.Hostname = target.Hostname;
                }

                if (string.IsNullOrWhiteSpace(existing.UplinkDevice) && !string.IsNullOrWhiteSpace(target.UplinkDevice))
                {
                    existing.UplinkDevice = target.UplinkDevice;
                    existing.UplinkIp = target.UplinkIp;
                }
                else if (!string.IsNullOrWhiteSpace(target.UplinkDevice) &&
                         existing.UplinkDevice.StartsWith("Core", StringComparison.OrdinalIgnoreCase) &&
                         !target.UplinkDevice.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
                {
                    // Prefer specific distribution uplink over generic Core
                    existing.UplinkDevice = target.UplinkDevice;
                    existing.UplinkIp = target.UplinkIp;
                }

                if (string.IsNullOrWhiteSpace(existing.LocalPort) && !string.IsNullOrWhiteSpace(target.LocalPort))
                {
                    existing.LocalPort = target.LocalPort;
                }
                if (string.IsNullOrWhiteSpace(existing.RemotePort) && !string.IsNullOrWhiteSpace(target.RemotePort))
                {
                    existing.RemotePort = target.RemotePort;
                }

                return false;
            }

            string key;
            if (!string.IsNullOrWhiteSpace(target.IpAddress) &&
                !target.IpAddress.StartsWith("0.0.0.") &&
                !target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
            {
                key = target.IpAddress;
            }
            else if (!string.IsNullOrEmpty(hostKey))
            {
                key = $"placeholder:{hostKey}";
            }
            else
            {
                key = $"placeholder:{Guid.NewGuid():N}";
            }

            target.IpAddress = string.IsNullOrWhiteSpace(target.IpAddress) ? key : target.IpAddress;
            return dict.TryAdd(key, target);
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
                    HostKey.Same(x.DeviceName, n.DeviceName) ||
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

                    if ((string.IsNullOrEmpty(existing.Platform) || existing.Platform.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) &&
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

                    if (n.IsPhone) existing.IsPhone = true;
                    if (n.IsAp && !existing.IsPhone) existing.IsAp = true;
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

            var blocks = Regex.Split(protocolsOutput, @"-{15,}");
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;
                var devMatch = DeviceIdRegex.Match(block);
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

            var blocks = Regex.Split(cdpOutput, @"-{20,}");
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

                var ipMatch = CdpIpRegex.Match(block);
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

        private static List<CdpNeighbor> ParseLldpNeighborsDetail(string lldpOutput, string currentHost, string currentIp)
        {
            var list = new List<CdpNeighbor>();
            if (string.IsNullOrWhiteSpace(lldpOutput)) return list;

            var blocks = Regex.Split(lldpOutput, @"-{20,}");
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;

                var n = new CdpNeighbor
                {
                    UplinkDevice = currentHost,
                    UplinkIp = currentIp
                };

                var sysMatch = Regex.Match(block, @"System Name:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (sysMatch.Success)
                {
                    n.DeviceName = sysMatch.Groups[1].Value.Trim().Split('.')[0];
                }
                else
                {
                    var chMatch = Regex.Match(block, @"Chassis id:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (chMatch.Success) n.DeviceName = chMatch.Groups[1].Value.Trim();
                }

                if (string.IsNullOrWhiteSpace(n.DeviceName)) continue;

                var ipMatch = Regex.Match(block, @"(?:Management Address|IPv4|IP):\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase);
                if (ipMatch.Success) n.IpAddress = ipMatch.Groups[1].Value.Trim();

                var descMatch = Regex.Match(block, @"System Description:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (descMatch.Success) n.Platform = descMatch.Groups[1].Value.Trim();

                var capMatch = Regex.Match(block, @"(?:System Capabilities|Enabled Capabilities):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (capMatch.Success) n.Capabilities = capMatch.Groups[1].Value.Trim();

                var localMatch = Regex.Match(block, @"(?:Local Interface|Port Description):\s*([^,\r\n]+)", RegexOptions.IgnoreCase);
                if (localMatch.Success) n.LocalPort = localMatch.Groups[1].Value.Trim();

                var remoteMatch = Regex.Match(block, @"Port id:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (remoteMatch.Success) n.RemotePort = remoteMatch.Groups[1].Value.Trim();

                ClassifyNeighbor(n);
                list.Add(n);
            }

            return list;
        }

        private static void ClassifyNeighbor(CdpNeighbor n)
        {
            string dev = n.DeviceName;
            string plat = n.Platform;
            string cap = n.Capabilities;

            bool hasSwitchCap = Regex.IsMatch(cap, @"\bS\b") || cap.Contains("Switch", StringComparison.OrdinalIgnoreCase);
            bool hasPhoneCap = Regex.IsMatch(cap, @"\bP\b") || cap.Contains("Phone", StringComparison.OrdinalIgnoreCase);
            bool hasRouterCap = Regex.IsMatch(cap, @"\bR\b") || cap.Contains("Router", StringComparison.OrdinalIgnoreCase);
            bool hasApCap = Regex.IsMatch(cap, @"\b(T|W)\b") || cap.Contains("Access-Point", StringComparison.OrdinalIgnoreCase) || cap.Contains("Trans-Bridge", StringComparison.OrdinalIgnoreCase) || cap.Contains("Wlan", StringComparison.OrdinalIgnoreCase);

            // 1. IP Phones
            n.IsPhone = dev.StartsWith("SEP", StringComparison.OrdinalIgnoreCase) ||
                        dev.StartsWith("CP-", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("IP Phone", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("ATA", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("794", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("796", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("884", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("885", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("886", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("782", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("784", StringComparison.OrdinalIgnoreCase) ||
                        plat.Contains("786", StringComparison.OrdinalIgnoreCase) ||
                        (hasPhoneCap && !hasSwitchCap && !hasRouterCap);

            if (n.IsPhone)
            {
                n.IsAp = false;
                return;
            }

            // 2. Wireless LAN Controllers (WLC) - Must NOT be classified as AP
            bool isWlc = plat.Contains("AIR-CT", StringComparison.OrdinalIgnoreCase) ||
                         plat.Contains("C9800", StringComparison.OrdinalIgnoreCase) ||
                         plat.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
                         cap.Contains("WLC", StringComparison.OrdinalIgnoreCase) ||
                         dev.Contains("wlc", StringComparison.OrdinalIgnoreCase) ||
                         dev.Contains("controller", StringComparison.OrdinalIgnoreCase);

            // 3. AP Detection
            n.IsAp = !isWlc && (
                     hasApCap ||
                     cap.Contains("Access-Point", StringComparison.OrdinalIgnoreCase) ||
                     cap.Contains("Trans-Bridge", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AIR-AP", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AIR-CAP", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AIR-SAP", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("C91", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("CW91", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AP1G", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AP2G", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AP3G", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AP4G", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AP5G", StringComparison.OrdinalIgnoreCase) ||
                     plat.StartsWith("AP6G", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("9105", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("9115", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("9120", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("9124", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("9130", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("Aironet", StringComparison.OrdinalIgnoreCase) ||
                     plat.Contains("Capwap", StringComparison.OrdinalIgnoreCase) ||
                     dev.StartsWith("AP-", StringComparison.OrdinalIgnoreCase) ||
                     dev.StartsWith("AP_", StringComparison.OrdinalIgnoreCase) ||
                     (dev.StartsWith("AP", StringComparison.OrdinalIgnoreCase) && !dev.StartsWith("APP", StringComparison.OrdinalIgnoreCase)));
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
            string? port = null)
        {
            progress(ip, 5, "Probing SSH (Port 22)...", "SSH", knownHostname, isAp, uplinkDevice, port);

            bool reachable = await IsSshPortReachableAsync(ip, 22, 2500, token);
            if (!reachable)
            {
                progress(ip, 0, "Unreachable / Port 22 Closed", "ERR", knownHostname, isAp, uplinkDevice, port);
                log("Error", $"Device {ip} is unreachable (TCP port 22 connection timed out or refused).");
                return;
            }

            SshClient? client = null;
            CredentialEntry? activeCred = null;

            foreach (var cred in candidateCredentials)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    progress(ip, 10, $"Authenticating ({cred.Username})...", "SSH", knownHostname, isAp, uplinkDevice, port);
                    var testClient = new SshClient(ip, cred.Username, cred.Password);
                    testClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(7);
                    await Task.Run(() => testClient.Connect(), token);

                    client = testClient;
                    activeCred = cred;
                    log("Auth", $"Device {ip} successfully authenticated with user '{cred.Username}'.");
                    break;
                }
                catch (SshAuthenticationException)
                {
                    log("Auth", $"Credential '{cred.Username}' failed for {ip}. Attempting fallback credential...");
                }
                catch (Exception ex)
                {
                    log("Error", $"Connection error to {ip} with '{cred.Username}': {ex.Message}");
                    if (ex.Message.Contains("timed out") || ex.Message.Contains("No route") || ex.Message.Contains("refused"))
                    {
                        break;
                    }
                }
            }

            if (client == null || activeCred == null)
            {
                progress(ip, 0, "Auth Failed (All Creds)", "ERR", knownHostname, isAp, uplinkDevice, port);
                log("Error", $"All candidate credentials failed on {ip}.");
                return;
            }

            try
            {
                using (client)
                {
                    progress(ip, 20, "Connected", "SSH", knownHostname, isAp, uplinkDevice, port);
                    using var stream = client.CreateShellStream("vt100", 255, 50, 800, 600, 4096);

                    string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 10000, token);
                    string prompt = await ElevatePrivilegesAsync(stream, initial, activeCred.Secret, token);

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

                    progress(ip, 25, "Session Ready", "SSH", hostname, isAp, uplinkDevice, port);
                    if (!profileName.Contains("AireOS", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExecuteRawCommandAsync(stream, "terminal length 0", token);
                    }
                    else
                    {
                        await ExecuteRawCommandAsync(stream, "config paging disable", token);
                    }

                    string deviceTypeLabel = isAp ? "Access Point" : (profileName.Contains("WLC", StringComparison.OrdinalIgnoreCase) || profileName.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ? "Cisco Wireless Controller" : "Cisco Switch");

                    var sb = new StringBuilder();
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
                    sb.AppendLine($"Profile     : {(isAp ? "Cisco Access Point" : profileName)}");
                    sb.AppendLine($"Auth User   : {activeCred.Username}");
                    sb.AppendLine($"Protocol    : SSH (Interactive Shell)");
                    sb.AppendLine("==================================================\n");

                    var validCommands = new List<string>();
                    foreach (var c in commands)
                    {
                        var trimmed = c.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("!"))
                        {
                            validCommands.Add(trimmed);
                        }
                    }

                    if (validCommands.Count == 0) validCommands.Add("show run");

                    for (int i = 0; i < validCommands.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string cmd = validCommands[i];

                        int pct = 25 + (int)(((double)(i + 1) / validCommands.Count) * 65);
                        progress(ip, pct, $"Running: {cmd}", "SSH", hostname, isAp, uplinkDevice, port);

                        string output = await ExecuteRawCommandAsync(stream, cmd, token);

                        sb.AppendLine("==================================================");
                        sb.AppendLine($"Command: {cmd}");
                        sb.AppendLine("==================================================");
                        sb.AppendLine(output.TrimEnd());
                        sb.AppendLine();
                    }

                    if (writeConfig && !isAp)
                    {
                        progress(ip, 95, "Saving Config (write memory)...", "SSH", hostname, isAp, uplinkDevice, port);
                        try
                        {
                            string saveOutput = await ExecuteRawCommandAsync(stream, "write memory", token, timeoutMs: 25000);
                            sb.AppendLine("==================================================");
                            sb.AppendLine("Command: write memory");
                            sb.AppendLine("==================================================");
                            sb.AppendLine(saveOutput.TrimEnd());
                            sb.AppendLine();
                        }
                        catch (Exception ex)
                        {
                            log("Warning", $"Failed to save memory on {ip}: {ex.Message}");
                        }
                    }

                    client.Disconnect();

                    string safeHostname = Regex.Replace(hostname, @"[^\w\.\-]", "_");
                    string devTypeSuffix = isAp ? "AP" : (profileName.Contains("WLC", StringComparison.OrdinalIgnoreCase) || profileName.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ? "WLC" : "Switch");
                    string filename = Path.Combine(backupDir, $"{ip}_{safeHostname}_{devTypeSuffix}.txt");
                    await File.WriteAllTextAsync(filename, sb.ToString(), token);

                    progress(ip, 100, $"Completed ({validCommands.Count} cmds)", "SSH", hostname, isAp, uplinkDevice, port);
                    log("Backup", $"Successfully backed up {hostname} ({ip}) -> {Path.GetFileName(filename)}");
                }
            }
            catch (Exception ex)
            {
                progress(ip, 0, $"Failed: {ex.Message}", "ERR", knownHostname, isAp, uplinkDevice, port);
                log("Error", $"Backup failed on {ip}: {ex.Message}");
            }
        }

        private async Task<string> ElevatePrivilegesAsync(ShellStream stream, string initialOutput, string secret, CancellationToken token)
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

        private async Task<string> ExecuteRawCommandAsync(ShellStream stream, string command, CancellationToken token, int timeoutMs = 20000)
        {
            FlushStream(stream);
            stream.WriteLine(command);
            return await ReadUntilPromptOrPatternAsync(stream, PromptRegex, timeoutMs, token);
        }

        private void FlushStream(ShellStream stream)
        {
            while (stream.DataAvailable)
            {
                stream.Read();
            }
        }

        private async Task<string> ReadUntilPromptOrPatternAsync(ShellStream stream, Regex pattern, int timeoutMs, CancellationToken token)
        {
            var sb = new StringBuilder();
            var startTime = DateTime.UtcNow;

            while (!token.IsCancellationRequested && (DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                if (stream.DataAvailable)
                {
                    string chunk = stream.Read();
                    sb.Append(chunk);

                    if (MoreRegex.IsMatch(chunk))
                    {
                        stream.Write(" ");
                        continue;
                    }

                    int searchLen = Math.Min(sb.Length, 512);
                    string tail = sb.ToString(sb.Length - searchLen, searchLen);

                    if (pattern.IsMatch(tail))
                    {
                        return sb.ToString();
                    }
                }
                else
                {
                    await Task.Delay(10, token);
                }
            }

            return sb.ToString();
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
        }
    }
}
