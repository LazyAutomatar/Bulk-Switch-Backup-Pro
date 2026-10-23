using System.Collections.Concurrent;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class NetworkEngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BulkBackup.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeBackupTransport _transport = new();
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly ConcurrentQueue<(string Host, string Status, string Protocol)> _progress = new();
    private static CredentialEntry Credential(string user = "operator") => new()
    {
        Username = user, Password = "test-password", Secret = "test-enable-secret"
    };

    private Task<string> RunAsync(string[]? hosts = null, string[]? commands = null,
        bool discover = false, bool aps = false, bool save = false, int concurrency = 2,
        string profile = "Cisco IOS", List<CredentialEntry>? credentials = null,
        CancellationToken token = default, string filter = "") =>
        new NetworkEngine(_transport).StartEngineAsync(
            (hosts ?? ["192.0.2.1"]).ToList(), credentials ?? [Credential()], discover,
            0, filter, aps, concurrency, (commands ?? ["show version"]).ToList(), profile,
            save, _directory, (source, message) => _logs.Enqueue($"{source}: {message}"),
            (host, _, status, protocol, _, _, _, _) => _progress.Enqueue((host, status, protocol)), token);

    [Fact]
    public async Task Backup_preserves_command_order_and_captured_output_without_secrets()
    {
        var device = _transport.Add("192.0.2.1", "ACCESS-01");
        device.Outputs["show version"] = "Cisco IOS XE Software, Version 17.12.4";
        device.Outputs["show running-config"] = "hostname ACCESS-01";
        var result = await RunAsync(commands: ["! heading", "", " show version ", "show running-config"]);
        Assert.Equal(_directory, result);
        Assert.Equal(["show version", "terminal length 0", "show version", "show running-config"], _transport.Commands.Select(x => x.Command));
        var content = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory, "*.txt")));
        Assert.Contains("192.0.2.1_ACCESS-01_Switch.txt", Assert.Single(Directory.GetFiles(_directory)));
        Assert.Contains("Hostname    : ACCESS-01", content);
        Assert.Contains("Cisco IOS XE Software, Version 17.12.4", content);
        Assert.Contains("hostname ACCESS-01", content);
        Assert.DoesNotContain("test-password", content + string.Join('\n', _logs));
        Assert.DoesNotContain("test-enable-secret", content + string.Join('\n', _logs));
        Assert.Contains(_progress, p => p.Status.StartsWith("Completed"));
        Assert.Equal(1, _transport.Disposed);
        Assert.Equal(1, _transport.ShellsDisposed);
    }

    [Fact]
    public async Task Authentication_rejection_tries_the_next_credential()
    {
        _transport.Add("192.0.2.1").RejectedUsers.Add("old-user");
        await RunAsync(credentials: [Credential("old-user"), Credential("new-user")]);
        Assert.Equal(["old-user", "new-user"], _transport.Attempts.Select(a => a.Username));
        Assert.Single(Directory.GetFiles(_directory, "*.txt"));
    }

    [Fact]
    public async Task All_rejected_credentials_fail_only_that_device()
    {
        _transport.Add("192.0.2.1").RejectedUsers.Add("operator");
        _transport.Add("192.0.2.2");
        await RunAsync(hosts: ["192.0.2.1", "192.0.2.2"]);
        Assert.Contains(_progress, p => p.Host == "192.0.2.1" && p.Status.Contains("Auth Failed"));
        Assert.Contains(_progress, p => p.Host == "192.0.2.2" && p.Status.StartsWith("Completed"));
        Assert.Single(Directory.GetFiles(_directory, "*.txt"));
    }

    [Fact]
    public async Task Unreachable_device_never_authenticates_and_does_not_block_healthy_device()
    {
        _transport.Add("192.0.2.1").Reachable = false;
        _transport.Add("192.0.2.2");
        await RunAsync(hosts: ["192.0.2.1", "192.0.2.2"]);
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "192.0.2.1");
        Assert.Contains(_progress, p => p.Host == "192.0.2.1" && p.Protocol == "ERR");
        Assert.Single(Directory.GetFiles(_directory, "*.txt"));
    }

    [Fact]
    public async Task Command_timeout_disposes_session_and_does_not_write_a_completed_backup()
    {
        _transport.Add("192.0.2.1").FailedCommands.Add("show version");
        await RunAsync();
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Contains(_progress, p => p.Status.StartsWith("Failed:"));
        Assert.DoesNotContain(_progress, p => p.Status.StartsWith("Completed"));
        Assert.Equal(0, _transport.Active);
        Assert.Equal(1, _transport.ShellsDisposed);
    }

    [Fact]
    public async Task Missing_initial_prompt_times_out_without_marking_partial_data_complete()
    {
        _transport.Add("192.0.2.1").Silent = true;
        await RunAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Contains(_progress, p => p.Status.Contains("No device prompt"));
        Assert.DoesNotContain(_progress, p => p.Status.StartsWith("Completed"));
        Assert.Equal(0, _transport.Active);
    }

    [Fact]
    public async Task Failed_repeat_backup_preserves_the_previous_completed_file()
    {
        var device = _transport.Add("192.0.2.1");
        await RunAsync();
        var file = Assert.Single(Directory.GetFiles(_directory));
        var original = await File.ReadAllTextAsync(file);
        device.FailedCommands.Add("show version");
        await RunAsync();
        Assert.Equal(original, await File.ReadAllTextAsync(file));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_and_discovery_respect_the_worker_limit(bool discover)
    {
        var hosts = Enumerable.Range(1, 6).Select(i => $"192.0.2.{i}").ToArray();
        foreach (var host in hosts) _transport.Add(host);
        var twoWorkers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.BeforeConnect = async (_, token) =>
        {
            if (_transport.Active == 2) twoWorkers.TrySetResult();
            await twoWorkers.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        };
        await RunAsync(hosts: hosts, discover: discover, concurrency: 2);
        Assert.Equal(2, _transport.MaximumActive);
        Assert.Equal(0, _transport.Active);
        Assert.Equal(6, Directory.GetFiles(_directory, "*.txt").Length);
    }

    [Fact]
    public async Task Precancelled_run_creates_no_directory_or_connections()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(token: cancellation.Token));
        Assert.False(Directory.Exists(_directory));
        Assert.Empty(_transport.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_a_prompt_wait_propagates_and_releases_session(bool discover)
    {
        _transport.Add("192.0.2.1").Silent = true;
        using var cancellation = new CancellationTokenSource();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.BeforeConnect = (_, _) => { connected.TrySetResult(); return Task.CompletedTask; };
        var run = RunAsync(discover: discover, token: cancellation.Token);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, _transport.Active);
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.DoesNotContain(_progress, p => p.Status.StartsWith("Completed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_port_probe_is_not_reported_as_unreachable(bool discover)
    {
        using var cancellation = new CancellationTokenSource();
        _transport.BeforeProbe = (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(discover: discover, token: cancellation.Token));
        Assert.Empty(_transport.Attempts);
        Assert.DoesNotContain(_progress, p => p.Status.Contains("Unreachable"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_stops_queued_workers_and_disposes_active_sessions(bool discover)
    {
        var hosts = Enumerable.Range(1, 5).Select(i => $"192.0.2.{i}").ToArray();
        foreach (var host in hosts) _transport.Add(host).Silent = true;
        using var cancellation = new CancellationTokenSource();
        var workersStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.BeforeConnect = (_, _) =>
        {
            if (_transport.Active == 2) workersStarted.TrySetResult();
            return Task.CompletedTask;
        };
        var run = RunAsync(hosts: hosts, discover: discover, concurrency: 2, token: cancellation.Token);
        await workersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(2, _transport.Attempts.Count);
        Assert.Equal(0, _transport.Active);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("IOS", "show spanning-tree summary", "show startup-config")]
    [InlineData("Nexus", "show port-channel summary", "show startup-config")]
    [InlineData("Catalyst WLC", "show ap summary", "show wireless client summary")]
    [InlineData("AireOS WLC", "show sysinfo", "show run-config commands")]
    public async Task Builtin_profiles_preserve_their_platform_specific_capture_commands(string profile, string first, string second)
    {
        var device = _transport.Add("192.0.2.1");
        device.Outputs["show version"] = profile switch
        {
            "IOS" => "Cisco IOS Software",
            "Nexus" => "Cisco NX-OS Software",
            "Catalyst WLC" => "Cisco IOS XE Software C9800-L",
            _ => "Cisco AireOS Wireless Controller"
        };
        var commands = profile switch
        {
            "IOS" => CommandProfiles.CiscoIosCommands,
            "Nexus" => CommandProfiles.CiscoNexusCommands,
            "Catalyst WLC" => CommandProfiles.CiscoWlcCommands,
            _ => CommandProfiles.CiscoAireOsWlcCommands
        };
        await RunAsync(profile: profile, commands: commands);
        var content = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(_directory)));
        Assert.Contains($"Command: {first}", content);
        Assert.Contains($"Command: {second}", content);
        Assert.DoesNotContain(_transport.Commands, c => c.Command.StartsWith('!') || string.IsNullOrWhiteSpace(c.Command));
    }

    [Fact]
    public async Task Cancellation_during_save_is_not_swallowed_as_a_warning()
    {
        using var cancellation = new CancellationTokenSource();
        _transport.Add("192.0.2.1").OnCommand = command => { if (command == "write memory") cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(save: true, token: cancellation.Token));
        Assert.Equal(0, _transport.Active);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Session_enters_enable_mode_when_required()
    {
        _transport.Add("192.0.2.1").RequiresEnable = true;
        await RunAsync();
        Assert.Equal(["show version", "enable", "test-enable-secret", "terminal length 0", "show version"], _transport.Commands.Select(c => c.Command));
        Assert.Single(Directory.GetFiles(_directory, "*.txt"));
    }

    [Fact]
    public async Task Pager_is_advanced_and_all_pages_are_saved()
    {
        _transport.Add("192.0.2.1").PagedCommands.Add("show version");
        await RunAsync();
        Assert.Contains(_transport.Commands, c => c.Command == " ");
        var content = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.txt")));
        Assert.Contains("first page", content);
        Assert.Contains("remaining page", content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Configuration_save_requires_the_explicit_option(bool save)
    {
        _transport.Add("192.0.2.1");
        await RunAsync(save: save);
        Assert.Equal(save, _transport.Commands.Any(c => c.Command == "write memory"));
    }

    [Fact]
    public async Task AireOS_uses_its_paging_command_and_controller_filename()
    {
        _transport.Add("192.0.2.1", "WLC-01").Outputs["show version"] = "Cisco AireOS Wireless Controller";
        await RunAsync(profile: "Cisco AireOS WLC", commands: ["show sysinfo"]);
        Assert.Equal(["show version", "config paging disable", "show sysinfo"], _transport.Commands.Select(c => c.Command));
        Assert.DoesNotContain(_transport.Commands, c => c.Command == "terminal length 0");
        Assert.EndsWith("_WLC.txt", Assert.Single(Directory.GetFiles(_directory)));
    }

    [Fact]
    public async Task Configuration_mode_commands_are_suppressed_but_AireOS_paging_is_retained()
    {
        _transport.Add("192.0.2.1");
        await RunAsync(commands: ["conf t", "configure terminal", "config paging disable", "show version"]);
        Assert.DoesNotContain(_transport.Commands, c => c.Command is "conf t" or "configure terminal");
        Assert.Contains(_transport.Commands, c => c.Command == "config paging disable");
        Assert.Contains(_logs, log => log.StartsWith("Safety:"));
    }

    [Fact]
    public async Task Empty_profile_retains_the_existing_running_config_fallback()
    {
        _transport.Add("192.0.2.1");
        await RunAsync(commands: ["! comment", " "]);
        Assert.Contains(_transport.Commands, c => c.Command == "show running-config");
    }

    [Fact]
    public async Task Duplicate_seed_is_backed_up_once()
    {
        _transport.Add("192.0.2.1");
        await RunAsync(hosts: ["192.0.2.1", "192.0.2.1"]);
        Assert.Single(_transport.Attempts);
    }

    [Fact]
    public async Task Discovery_excludes_phones_and_uses_AP_profile_without_saving_AP_config()
    {
        _transport.Add("192.0.2.1", "CORE-01").Outputs["show cdp neighbors detail"] = Fixture("cdp-mixed.txt");
        _transport.Add("192.0.2.2", "ACCESS-02");
        _transport.Add("192.0.2.3", "AP-03").Outputs["show version"] = "Cisco IOS Software, AP3G2 Software";
        _transport.Add("192.0.2.4", "SEP001122334455");
        await RunAsync(discover: true, aps: true, save: true, concurrency: 1);
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "192.0.2.4");
        Assert.Single(_transport.Attempts, a => a.Host == "192.0.2.3"); // backup, no AP recursion
        Assert.Equal(CommandProfiles.CiscoApCommands, _transport.Commands.Where(c => c.Host == "192.0.2.3").Select(c => c.Command).Skip(1));
        Assert.Contains(Directory.GetFiles(_directory), f => f.EndsWith("_AP.txt"));
        Assert.Equal(3, Directory.GetFiles(_directory, "*.txt").Length);
    }

    [Fact]
    public async Task Discovery_leaves_APs_out_when_disabled_and_skips_neighbors_without_IPs()
    {
        var core = _transport.Add("192.0.2.1", "CORE-01");
        core.Outputs["show cdp neighbors detail"] = Fixture("cdp-mixed.txt") + "\n-------------------------\nDevice ID: ACCESS-NO-IP\nPlatform: cisco C9300, Capabilities: Switch\nInterface: GigabitEthernet1/0/9, Port ID (outgoing port): GigabitEthernet1/0/1\n";
        _transport.Add("192.0.2.2", "ACCESS-02");
        await RunAsync(discover: true, concurrency: 1);
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "192.0.2.3" || a.Host.StartsWith("placeholder:"));
        Assert.Contains(_progress, p => p.Status == "Skipped (No IP)");
        Assert.Equal(2, Directory.GetFiles(_directory, "*.txt").Length);
    }

    [Fact]
    public async Task Reciprocal_CDP_neighbors_terminate_and_single_worker_recursion_does_not_deadlock()
    {
        _transport.Add("192.0.2.1", "CORE-01").Outputs["show cdp neighbors detail"] = Neighbor("ACCESS-02", "192.0.2.2");
        _transport.Add("192.0.2.2", "ACCESS-02").Outputs["show cdp neighbors detail"] = Neighbor("CORE-01", "192.0.2.1");
        await RunAsync(discover: true, concurrency: 1).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(4, _transport.Attempts.Count); // discovery and backup once per device
        Assert.Equal(1, _transport.MaximumActive);
    }

    [Fact]
    public async Task Subnet_filter_excludes_outside_switches()
    {
        _transport.Add("192.0.2.1", "CORE-01").Outputs["show cdp neighbors detail"] = Neighbor("ACCESS-OTHER", "198.51.100.2");
        _transport.Add("198.51.100.2", "ACCESS-OTHER");
        await RunAsync(discover: true, filter: "192.0.2.0/24");
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "198.51.100.2");
        Assert.Single(Directory.GetFiles(_directory, "*.txt"));
    }

    [Fact]
    public async Task LLDP_and_CDP_observations_for_the_same_device_are_merged()
    {
        var core = _transport.Add("192.0.2.1", "CORE-01");
        core.Outputs["show cdp neighbors detail"] = Neighbor("ACCESS-02", "192.0.2.2");
        core.Outputs["show lldp neighbors detail"] = Fixture("lldp-access.txt");
        _transport.Add("192.0.2.2", "ACCESS-02");
        await RunAsync(discover: true);
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
        Assert.Equal(2, Directory.GetFiles(_directory, "*.txt").Length);
    }

    [Fact]
    public async Task Missing_credentials_are_rejected_before_output_is_created()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(credentials: []));
        Assert.False(Directory.Exists(_directory));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    private static string Neighbor(string host, string ip) => $"Device ID: {host}\nEntry address(es):\n  IP address: {ip}\nPlatform: cisco C9300, Capabilities: Switch\nInterface: GigabitEthernet1/0/1, Port ID (outgoing port): GigabitEthernet1/0/48\n";
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

