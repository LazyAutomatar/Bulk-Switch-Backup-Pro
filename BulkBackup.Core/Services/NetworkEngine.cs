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
        public delegate void ProgressAction(string ip, int percentage, string status, string protocol, string? hostname = null, bool isAp = false);

        private static readonly Regex PromptRegex = new(@"(?:^|[\r\n])\s*[\w\.\-]+(?:\([^\)]+\))?[>#]\s*$", RegexOptions.Compiled);
        private static readonly Regex PasswordRegex = new(@"[Pp]assword:\s*$", RegexOptions.Compiled);
        private static readonly Regex MoreRegex = new(@"--\s*More\s*--", RegexOptions.Compiled);
        private static readonly Regex DeviceIdRegex = new(@"Device ID:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PlatformRegex = new(@"Platform:\s*([^,\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CapabilitiesRegex = new(@"Capabilities:\s*([^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CdpIpRegex = new(@"(?:Entry address\(es\):|Management address\(es\):)[\s\S]*?(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FallbackIpRegex = new(@"(?:IP|IPv4)\s*[Aa]ddress:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            LogAction logCallback,
            ProgressAction progressCallback,
            CancellationToken token)
        {
            if (candidateCredentials == null || candidateCredentials.Count == 0)
            {
                throw new ArgumentException("At least one credential must be provided.");
            }

            logCallback("System", $"Starting Bulk Switch Backup Pro (Profile: {profileName}, Threads: {maxConcurrency}, Credentials: {candidateCredentials.Count})...");

            var devicesToBackup = new ConcurrentDictionary<string, DiscoveredTarget>();
            var visitedDiscovery = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (enableCdp)
            {
                logCallback("System", "Initiating Recursive CDP Discovery Phase (IP Phones automatically excluded)...");
                var discoveryTasks = new List<Task>();
                foreach (var ip in targetIps)
                {
                    devicesToBackup.TryAdd(ip, new DiscoveredTarget { IpAddress = ip, IsAp = false });
                    discoveryTasks.Add(PerformDiscoveryAsync(ip, candidateCredentials, filterType, filterValue, includeAps, devicesToBackup, visitedDiscovery, logCallback, progressCallback, token));
                }
                await Task.WhenAll(discoveryTasks);
                logCallback("System", $"CDP Discovery Phase complete. Discovered {devicesToBackup.Count} total valid targets.");
            }
            else
            {
                foreach (var ip in targetIps)
                {
                    devicesToBackup.TryAdd(ip, new DiscoveredTarget { IpAddress = ip, IsAp = false });
                }
            }

            logCallback("System", $"Initiating Backup Phase across {devicesToBackup.Count} targets...");
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string backupDir = Path.Combine(Environment.CurrentDirectory, "Switch_Backups", timestamp);
            Directory.CreateDirectory(backupDir);

            var backupTasks = new List<Task>();
            var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency));

            foreach (var kvp in devicesToBackup)
            {
                var target = kvp.Value;
                backupTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(token);
                    try
                    {
                        var cmds = target.IsAp ? CommandProfiles.CiscoApCommands.ToList() : customCommands;
                        await PerformBackupWithFallbackAsync(target.IpAddress, candidateCredentials, target.IsAp, backupDir, cmds, profileName, writeConfig, logCallback, progressCallback, token);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, token));
            }

            await Task.WhenAll(backupTasks);
            logCallback("System", $"All backups completed! Configurations stored in: {backupDir}");
            return backupDir;
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
            CancellationToken token)
        {
            if (visited.ContainsKey(ip)) return;
            visited.TryAdd(ip, true);

            progress(ip, 15, "Discovering CDP neighbors...", "SSH");

            SshClient? client = null;
            CredentialEntry? activeCred = null;

            foreach (var cred in candidateCredentials)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var testClient = new SshClient(ip, cred.Username, cred.Password);
                    testClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(12);
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
                progress(ip, 0, "Discovery Auth Failed", "ERR");
                log("Error", $"All credentials failed on seed {ip}.");
                return;
            }

            try
            {
                using (client)
                {
                    using var stream = client.CreateShellStream("vt100", 255, 50, 800, 600, 2048);
                    string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 8000, token);

                    await ElevatePrivilegesAsync(stream, initial, activeCred.Secret, token);
                    await ExecuteRawCommandAsync(stream, "terminal length 0", token);

                    string cdpOutput = await ExecuteRawCommandAsync(stream, "show cdp neighbors detail", token);
                    client.Disconnect();

                    progress(ip, 100, "Discovery complete", "SSH");

                    var neighbors = ParseCdpNeighbors(cdpOutput);
                    var subTasks = new List<Task>();

                    foreach (var neighbor in neighbors)
                    {
                        if (string.IsNullOrWhiteSpace(neighbor.IpAddress)) continue;

                        // CRITICAL: NEVER ATTEMPT SSH ON IP PHONES!
                        if (neighbor.IsPhone)
                        {
                            log("CDP", $"Found IP Phone: {neighbor.DeviceName} ({neighbor.IpAddress}) [{neighbor.Platform}] - Skipped (No SSH).");
                            continue;
                        }

                        bool isAp = neighbor.IsAp;
                        if (isAp && !includeAps) continue;

                        bool validTarget = isAp;
                        if (!isAp)
                        {
                            if (filterType == 0)
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
                                IsAp = isAp
                            };

                            if (backupQueue.TryAdd(neighbor.IpAddress, target))
                            {
                                log("CDP", $"Discovered {neighbor.DeviceName} ({neighbor.IpAddress}) [{(isAp ? "AP" : "Switch")}]");
                                progress(neighbor.IpAddress, 0, "Discovered via CDP", "Waiting", neighbor.DeviceName, isAp);

                                if (!isAp)
                                {
                                    subTasks.Add(PerformDiscoveryAsync(neighbor.IpAddress, candidateCredentials, filterType, filterValue, includeAps, backupQueue, visited, log, progress, token));
                                }
                            }
                        }
                    }

                    await Task.WhenAll(subTasks);
                }
            }
            catch (Exception ex)
            {
                progress(ip, 0, "Discovery Failed", "ERR");
                log("Error", $"CDP Discovery parse failed on {ip}: {ex.Message}");
            }
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
            CancellationToken token)
        {
            progress(ip, 5, "Connecting SSH...", "SSH", null, isAp);

            SshClient? client = null;
            CredentialEntry? activeCred = null;

            foreach (var cred in candidateCredentials)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    progress(ip, 10, $"Authenticating ({cred.Username})...", "SSH", null, isAp);
                    var testClient = new SshClient(ip, cred.Username, cred.Password);
                    testClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
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
                progress(ip, 0, "Auth Failed (All Creds)", "ERR", null, isAp);
                log("Error", $"All candidate credentials failed on {ip}.");
                return;
            }

            try
            {
                using (client)
                {
                    progress(ip, 20, "Connected", "SSH", null, isAp);
                    using var stream = client.CreateShellStream("vt100", 255, 50, 800, 600, 4096);

                    string initial = await ReadUntilPromptOrPatternAsync(stream, PromptRegex, 10000, token);
                    string prompt = await ElevatePrivilegesAsync(stream, initial, activeCred.Secret, token);

                    string hostname = ip;
                    var hostMatch = Regex.Match(prompt, @"(?:^|[\r\n])\s*([\w\.\-]+?)(?:\([^\)]+\))?[>#]");
                    if (hostMatch.Success && !hostMatch.Groups[1].Value.Equals("Switch", StringComparison.OrdinalIgnoreCase) && !hostMatch.Groups[1].Value.Equals("Router", StringComparison.OrdinalIgnoreCase))
                    {
                        hostname = hostMatch.Groups[1].Value.Trim();
                    }

                    progress(ip, 25, "Session Ready", "SSH", hostname, isAp);
                    await ExecuteRawCommandAsync(stream, "terminal length 0", token);

                    var sb = new StringBuilder();
                    sb.AppendLine("==================================================");
                    sb.AppendLine($"Backup Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"Device IP   : {ip}");
                    sb.AppendLine($"Hostname    : {hostname}");
                    sb.AppendLine($"Device Type : {(isAp ? "Access Point" : "Cisco Switch")}");
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
                        progress(ip, pct, $"Running: {cmd}", "SSH", hostname, isAp);

                        string output = await ExecuteRawCommandAsync(stream, cmd, token);

                        sb.AppendLine("==================================================");
                        sb.AppendLine($"Command: {cmd}");
                        sb.AppendLine("==================================================");
                        sb.AppendLine(output.TrimEnd());
                        sb.AppendLine();
                    }

                    if (writeConfig && !isAp)
                    {
                        progress(ip, 95, "Saving Config (write memory)...", "SSH", hostname, isAp);
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
                    string filename = Path.Combine(backupDir, $"{ip}_{safeHostname}_{(isAp ? "AP" : "Switch")}.txt");
                    await File.WriteAllTextAsync(filename, sb.ToString(), token);

                    progress(ip, 100, $"Completed ({validCommands.Count} cmds)", "SSH", hostname, isAp);
                    log("Backup", $"Successfully backed up {hostname} ({ip}) -> {Path.GetFileName(filename)}");
                }
            }
            catch (Exception ex)
            {
                progress(ip, 0, $"Failed: {ex.Message}", "ERR", null, isAp);
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
            var startTime = DateTime.Now;

            while (!token.IsCancellationRequested && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
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

                    if (pattern.IsMatch(sb.ToString()))
                    {
                        return sb.ToString();
                    }
                }
                else
                {
                    await Task.Delay(50, token);
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

        private List<CdpNeighbor> ParseCdpNeighbors(string cdpOutput)
        {
            var list = new List<CdpNeighbor>();
            var blocks = Regex.Split(cdpOutput, @"-{20,}");

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;

                var n = new CdpNeighbor();
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

                var platMatch = PlatformRegex.Match(block);
                if (platMatch.Success) n.Platform = platMatch.Groups[1].Value.Trim();

                var capMatch = CapabilitiesRegex.Match(block);
                if (capMatch.Success) n.Capabilities = capMatch.Groups[1].Value.Trim();

                if (!string.IsNullOrEmpty(n.IpAddress))
                {
                    string dev = n.DeviceName;
                    string plat = n.Platform;
                    string cap = n.Capabilities;

                    // IP Phone Detection (from NetHealth)
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
                                cap.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
                                (cap.Contains("P", StringComparison.Ordinal) && !cap.Contains("S", StringComparison.Ordinal) && !cap.Contains("R", StringComparison.Ordinal));

                    // AP Detection
                    n.IsAp = !n.IsPhone && (
                             cap.Contains("Access-Point", StringComparison.OrdinalIgnoreCase) ||
                             cap.Contains("Trans-Bridge", StringComparison.OrdinalIgnoreCase) ||
                             plat.StartsWith("AIR-", StringComparison.OrdinalIgnoreCase) ||
                             plat.StartsWith("C91", StringComparison.OrdinalIgnoreCase) ||
                             plat.Contains("AP", StringComparison.OrdinalIgnoreCase) ||
                             dev.StartsWith("AP", StringComparison.OrdinalIgnoreCase));

                    list.Add(n);
                }
            }

            return list;
        }

        public class DiscoveredTarget
        {
            public string IpAddress { get; set; } = string.Empty;
            public string Hostname { get; set; } = string.Empty;
            public bool IsAp { get; set; } = false;
        }

        private class CdpNeighbor
        {
            public string DeviceName { get; set; } = string.Empty;
            public string IpAddress { get; set; } = string.Empty;
            public string Platform { get; set; } = string.Empty;
            public string Capabilities { get; set; } = string.Empty;
            public bool IsAp { get; set; } = false;
            public bool IsPhone { get; set; } = false;
        }
    }
}
