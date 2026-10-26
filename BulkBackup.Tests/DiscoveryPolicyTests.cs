using System.Collections.Concurrent;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class DiscoveryPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BulkBackup.Discovery", Guid.NewGuid().ToString("N"));
    private readonly FakeBackupTransport _transport = new();
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly ConcurrentQueue<(string Host, string Status, string? Port)> _progress = new();

    private Task Run(int concurrency = 2, bool aps = false, string filter = "", CancellationToken token = default) =>
        new NetworkEngine(_transport).StartEngineAsync(["192.0.2.1"],
            [new CredentialEntry { Username = "test", Password = "test" }], true, 0, filter, aps,
            concurrency, ["show version"], "Custom Profile", false, _directory,
            (_, message) => _logs.Enqueue(message),
            (host, _, status, _, _, _, _, port) => _progress.Enqueue((host, status, port)), token);

    private static string Lldp(string name = "NEIGHBOR", string ip = "192.0.2.2", string system = "B,R",
        string? enabled = "B", string description = "Unknown device", string local = "GigabitEthernet1/0/1") =>
        $"Local Intf: {local}\nChassis id: 0011.2233.4455\nPort id: Ethernet48\nPort Description: remote-port-description\nSystem Name: {name}\nSystem Description:\n{description}\nTime remaining: 120 seconds\nSystem Capabilities: {system}\n"
        + (enabled == null ? "" : $"Enabled Capabilities: {enabled}\n")
        + $"Management Addresses:\n    IP: {ip}\n";

    private void Setup(string advertisement)
    {
        _transport.Add("192.0.2.1", "CORE").Outputs["show lldp neighbors detail"] = advertisement;
        _transport.Add("192.0.2.2", "NEIGHBOR");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Actual_Sg350_detail_populates_addresses_and_distinguishes_APs_from_C1000(bool includeAps, bool omitSeparators)
    {
        var seed = _transport.Add("192.0.2.1", "ACCESS-SWITCH-3");
        seed.Outputs["show version"] = "Cisco Small Business SG350-28P";
        string detail = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sg350-cdp-detail.txt"));
        if (omitSeparators) detail = detail.Replace("---------------------------------------------", "");
        seed.Outputs["show cdp neighbors detail"] = detail;
        foreach (string ap in new[] { "10.158.104.163", "10.158.104.149", "10.158.104.148" })
            _transport.Add(ap).Outputs["show version"] = "Cisco Aironet AP Software";
        _transport.Add("10.158.100.18", "BARBIL_UPLINK_SW1").Outputs["show version"] = "Cisco IOS Software, C1000 Software";
        await Run(aps: includeAps);
        foreach (string ap in new[] { "10.158.104.163", "10.158.104.149", "10.158.104.148" })
            Assert.Equal(includeAps ? 1 : 0, _transport.Attempts.Count(a => a.Host == ap));
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "10.158.100.18"));
        Assert.DoesNotContain(_transport.Commands, c => c.Host == "192.0.2.1" && c.Command == "show cdp entry *");
        Assert.DoesNotContain(_logs, l => l.Contains("placeholder:") || l.Contains("no usable management"));
        Assert.Contains(_logs, l => l.Contains("parsed 4 CDP detail"));
        Assert.Contains(_progress, p => p.Host == "10.158.100.18" && p.Port == "gi26 -> GigabitEthernet1/0/2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sg350_wrapped_table_traverses_C1000_and_its_downstream_switch(bool includeAps)
    {
        var seed = _transport.Add("192.0.2.1", "ACCESS-SWITCH-3");
        seed.Outputs["show version"] = "Firmware version 2.5";
        seed.Outputs["show inventory"] = "NAME: \"1\" DESCR: \"SG350-28P 28-Port Gigabit PoE Managed Switch\"\nPID: SG350-28P-K9";
        seed.Outputs["show cdp neighbors"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sg350-cdp-table.txt"));
        seed.Outputs["show cdp entry * protocol"] = "Protocol information for BARBIL_UPLINK_SW1:\n IP address: 192.0.2.2\nProtocol information for 3RD_FLOOR1:\n IP address: 192.0.2.4";
        var uplink = _transport.Add("192.0.2.2", "BARBIL_UPLINK_SW1");
        uplink.Outputs["show version"] = "Cisco IOS Software, C1000 Software, Version 15.2";
        uplink.Outputs["show cdp neighbors detail"] = "Device ID: DOWNSTREAM\nEntry address(es):\n IP address: 192.0.2.3\nPlatform: Cisco SG350-28P, Capabilities: Switch IGMP\nInterface: GigabitEthernet1/0/3, Port ID (outgoing port): gi1";
        _transport.Add("192.0.2.3", "DOWNSTREAM");
        _transport.Add("192.0.2.4", "3RD_FLOOR1");
        await Run(aps: includeAps);
        Assert.Contains(_transport.Commands, c => c.Host == "192.0.2.1" && c.Command == "terminal datadump");
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.3"));
        Assert.Equal(includeAps ? 1 : 0, _transport.Attempts.Count(a => a.Host == "192.0.2.4"));
        Assert.Contains(_progress, p => p.Host == "192.0.2.2" && p.Port != null && p.Port.Contains("GigabitEthernet1/0/2"));
    }

    [Fact]
    public async Task Detail_records_without_separators_do_not_lose_neighbors()
    {
        var seed = _transport.Add("192.0.2.1", "CORE");
        seed.Outputs["show cdp neighbors detail"] = "Device ID: ONE\nIP address: 192.0.2.2\nPlatform: Cisco C1000-24P-4G-L, Capabilities: Switch\nDevice ID: TWO\nIP address: 192.0.2.3\nPlatform: Cisco SG350-28P, Capabilities: Switch";
        _transport.Add("192.0.2.2", "ONE");
        _transport.Add("192.0.2.3", "TWO");
        await Run();
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.3"));
    }

    [Fact]
    public async Task C1000_officegate_sample_traverses_uplink_as_switch()
    {
        var seed = _transport.Add("192.0.2.1", "OFFICEGATE1");
        seed.Outputs["show version"] = "Cisco IOS Software, C1000 Software (C1000-UNIVERSALK9-M), Version 15.2(7)E6";
        seed.Outputs["show cdp neighbors"] = "Device ID        Local Intrfce     Holdtme    Capability  Platform  Port ID\nBARBIL_UPLINK_SW1\n                 Gig 1/0/10        132              S I   C1000-24P Gig 1/0/7\n\nTotal cdp entries displayed : 1";
        seed.Outputs["show cdp neighbors detail"] = "-------------------------\nDevice ID: BARBIL_UPLINK_SW1\nEntry address(es):\n  IP address: 10.158.100.18\nPlatform: cisco C1000-24P-4G-L,  Capabilities: Switch IGMP\nInterface: GigabitEthernet1/0/10,  Port ID (outgoing port): GigabitEthernet1/0/7\nHoldtime : 177 sec\n\nVersion :\nCisco IOS Software, C1000 Software (C1000-UNIVERSALK9-M), Version 15.2(7)E6\n\nadvertisement version: 2\nVTP Management Domain: ''\nNative VLAN: 1\nDuplex: full\nManagement address(es):\n  IP address: 10.158.100.18\n\nTotal cdp entries displayed : 1";
        _transport.Add("10.158.100.18", "BARBIL_UPLINK_SW1");
        await Run(aps: false);
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "10.158.100.18"));
        Assert.Contains(_progress, p => p.Host == "10.158.100.18" && p.Port != null && p.Port.Contains("GigabitEthernet1/0/7"));
        Assert.DoesNotContain(_transport.Commands, c => c.Command.Contains("wired"));
        Assert.Equal(2, Directory.GetFiles(_directory, "*_Switch.txt").Length);
    }

    [Theory]
    [InlineData("B", "B", "Unknown device", true)]
    [InlineData("B,R", "R", "Unknown device", true)]
    [InlineData("Bridge, Router", "Bridge", "Unknown device", true)]
    [InlineData("B,R", "S", "Cisco C9300", false)]
    [InlineData("B,R", "", "Cisco C9300", false)]
    [InlineData("B,R", "None", "Cisco C9300", false)]
    [InlineData("B,T", "B,T", "Telephone", false)]
    [InlineData("B,T", "B,T", "Unknown device", false)]
    [InlineData("Station Only", null, "Unknown device", false)]
    [InlineData("B", "B", "Linux workstation 6.1", false)]
    [InlineData("B", "B", "VMware ESXi", false)]
    [InlineData("B", "B", "Windows Server", false)]
    [InlineData("B", "B", "Network Printer", false)]
    [InlineData("O", "O", "Unknown device", false)]
    [InlineData("B", null, "Unknown device", true)]
    [InlineData("W", "W", "Wireless access point", false)]
    public async Task Only_positive_infrastructure_evidence_is_admitted(string system, string? enabled, string description, bool admitted)
    {
        Setup(Lldp(system: system, enabled: enabled, description: description));
        await Run();
        Assert.Equal(admitted ? 2 : 0, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
        Assert.Equal(admitted ? 2 : 0, _transport.Probes.Count(host => host == "192.0.2.2"));
        Assert.Equal(admitted ? 2 : 1, Directory.GetFiles(_directory).Length);
    }

    [Theory]
    [InlineData("SW-01", "Unknown vendor", false)]
    [InlineData("AP-01", "Unknown vendor", false)]
    [InlineData("controller", "Unknown vendor", false)]
    [InlineData("NEIGHBOR", "ArubaOS-CX Version 10.13", true)]
    [InlineData("NEIGHBOR", "cisco C9300-48P", true)]
    [InlineData("NEIGHBOR", "cisco C1000-24T-4G-L", true)]
    public async Task Missing_capabilities_requires_model_or_os_evidence_not_a_name(string name, string description, bool admitted)
    {
        Setup(Lldp(name: name, description: description, enabled: null).Replace("System Capabilities: B,R\n", ""));
        await Run();
        Assert.Equal(admitted, _transport.Attempts.Any(a => a.Host == "192.0.2.2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wlan_capability_obeys_ap_option_and_does_not_recurse(bool include)
    {
        Setup(Lldp(system: "B,W", enabled: "W"));
        await Run(aps: include);
        Assert.Equal(include ? 1 : 0, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
    }

    [Fact]
    public async Task C1000_model_is_a_switch_even_with_bridge_ap_capability_hint()
    {
        Setup(Lldp(description: "Cisco C1000-24T-4G-L", system: "B,W", enabled: "B,W"));
        _transport.Devices["192.0.2.2"].Outputs["show version"] = "Cisco IOS Software, C1000 Software, Version 15.2";
        await Run(aps: false);
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
        Assert.DoesNotContain(_transport.Commands, c => c.Command.Contains("wired", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, Directory.GetFiles(_directory, "*_Switch.txt").Length);
    }

    [Fact]
    public async Task Ap_cannot_bypass_subnet_filter()
    {
        Setup(Lldp(ip: "198.51.100.2", system: "B,W", enabled: "W"));
        _transport.Add("198.51.100.2");
        await Run(aps: true, filter: "192.0.2.0/24");
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "198.51.100.2");
    }

    [Fact]
    public async Task Multiple_records_without_separators_do_not_mix_capabilities_or_addresses()
    {
        Setup(Lldp(name: "SWITCH", local: "Gi1/0/5") + Lldp(name: "PHONE", ip: "192.0.2.3", enabled: "B,T"));
        _transport.Add("192.0.2.3");
        await Run();
        Assert.Contains(_transport.Attempts, a => a.Host == "192.0.2.2");
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "192.0.2.3");
        Assert.Contains(_progress, p => p.Host == "192.0.2.2" && p.Port == "Gi1/0/5 -> Ethernet48");
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.999")]
    public async Task Invalid_or_non_unicast_management_addresses_are_never_contacted(string ip)
    {
        Setup(Lldp(ip: ip));
        await Run();
        Assert.All(_transport.Attempts, attempt => Assert.Equal("192.0.2.1", attempt.Host));
        Assert.Contains(_progress, p => p.Status == "Skipped (No IP)");
    }

    [Fact]
    public async Task Phone_evidence_overrides_positive_cdp_switch_evidence()
    {
        Setup(Lldp(enabled: "B,T"));
        _transport.Devices["192.0.2.1"].Outputs["show cdp neighbors detail"] =
            "Device ID: NEIGHBOR\nEntry address(es):\n IP address: 192.0.2.2\nPlatform: Generic, Capabilities: Switch\nInterface: GigabitEthernet1/0/1, Port ID (outgoing port): Ethernet48\n";
        await Run();
        Assert.DoesNotContain(_transport.Attempts, a => a.Host == "192.0.2.2");
    }

    [Fact]
    public async Task Distinct_management_ips_with_same_hostname_are_not_collapsed()
    {
        Setup(Lldp(name: "DUPLICATE") + Lldp(name: "DUPLICATE", ip: "192.0.2.3"));
        _transport.Add("192.0.2.3");
        await Run();
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.2"));
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.3"));
        Assert.Equal(3, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public async Task Placeholder_promoted_to_ip_is_scheduled_once()
    {
        Setup(Lldp(name: "LATER", ip: "0.0.0.0") + Lldp(name: "TRANSIT"));
        _transport.Devices["192.0.2.2"].Outputs["show lldp neighbors detail"] = Lldp(name: "LATER", ip: "192.0.2.3");
        _transport.Add("192.0.2.3");
        await Run(concurrency: 1);
        Assert.Equal(2, _transport.Attempts.Count(a => a.Host == "192.0.2.3"));
        Assert.Equal(3, Directory.GetFiles(_directory).Length);
        Assert.DoesNotContain(_progress, p => p.Status == "Skipped (No IP)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Wide_cyclic_graph_uses_bounded_workers_and_visits_each_ip_once_per_phase(int workers)
    {
        const int total = 100;
        for (int i = 1; i <= total; i++) _transport.Add($"192.0.2.{i}", $"SW-{i}");
        _transport.Devices["192.0.2.1"].Outputs["show lldp neighbors detail"] = string.Concat(
            Enumerable.Range(2, total - 1).Select(i => Lldp(name: $"SW-{i}", ip: $"192.0.2.{i}")));
        for (int i = 2; i <= total; i++)
            _transport.Devices[$"192.0.2.{i}"].Outputs["show lldp neighbors detail"] = Lldp(name: "SW-1", ip: "192.0.2.1");
        _transport.BeforeConnect = (_, token) => Task.Delay(2, token);
        await Run(concurrency: workers).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(workers, _transport.MaximumActive);
        Assert.Equal(total * 2, _transport.Attempts.Count);
        Assert.All(_transport.Attempts.GroupBy(a => a.Host), group => Assert.Equal(2, group.Count()));
        Assert.Equal(total, Directory.GetFiles(_directory).Length);
        Assert.Equal(0, _transport.Active);
    }

    [Fact]
    public async Task Cancellation_with_queued_discovery_nodes_releases_workers_and_skips_backup()
    {
        Setup(string.Concat(Enumerable.Range(2, 50).Select(i => Lldp(name: $"SW-{i}", ip: $"192.0.2.{i}"))));
        for (int i = 3; i <= 51; i++) _transport.Add($"192.0.2.{i}");
        using var stop = new CancellationTokenSource();
        _transport.BeforeConnect = (host, token) =>
        {
            if (host != "192.0.2.1") { stop.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(concurrency: 4, token: stop.Token));
        Assert.Equal(0, _transport.Active);
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.DoesNotContain(_progress, p => p.Status.StartsWith("Completed"));
    }

    [Fact]
    public async Task Long_chain_completes_without_recursive_tasks()
    {
        const int total = 128;
        for (int i = 1; i <= total; i++)
        {
            var device = _transport.Add($"192.0.2.{i}", $"SW-{i}");
            if (i < total) device.Outputs["show lldp neighbors detail"] = Lldp(name: $"SW-{i + 1}", ip: $"192.0.2.{i + 1}");
        }
        await Run(concurrency: 1).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, _transport.MaximumActive);
        Assert.Equal(total * 2, _transport.Attempts.Count);
        Assert.Equal(total, Directory.GetFiles(_directory).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task Worker_setting_is_clamped_without_allocating_requested_task_count(int concurrency)
    {
        Setup("");
        await Run(concurrency: concurrency).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, _transport.MaximumActive);
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
