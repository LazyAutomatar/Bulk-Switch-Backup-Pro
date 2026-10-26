using BulkBackup.Core.Models;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class DevicePlatformTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BulkBackup.Platforms", Guid.NewGuid().ToString("N"));
    private readonly FakeBackupTransport _transport = new();
    private readonly List<string> _statuses = [];
    private Task Run(string profile = "Auto", string[]? commands = null, bool save = false) =>
        new NetworkEngine(_transport).StartEngineAsync(["192.0.2.1"],
            [new CredentialEntry { Username = "test", Password = "test-password", Secret = "test-enable-secret" }],
            false, 0, "", false, 1, (commands ?? []).ToList(), profile, save, _directory,
            (_, _) => { }, (_, _, status, _, _, _, _, _) => _statuses.Add(status), CancellationToken.None);

    public static TheoryData<string, string> Identities => new()
    {
        { "ios", "Cisco IOS Software, C2960 Software, Version 15.2" },
        { "ios", "Cisco IOS XE Software, Version 17.12.4" },
        { "ios", "Cisco IOS Software, C1000 Software (C1000-UNIVERSALK9-M), Version 15.2(7)E" },
        { "cisco-sb", "System Description: SG350-28 28-Port Gigabit Managed Switch" },
        { "nxos", "Cisco Nexus Operating System (NX-OS) Software" },
        { "c9800", "Cisco IOS XE Software\nCisco Wireless Controller C9800-CL" },
        { "aireos", "Product Name.................................. Cisco Controller\nProduct Version............................... 8.10.190.0" },
        { "cisco-ap", "Cisco IOS Software, AP3G2 Software" },
        { "cisco-sb", "Cisco CBS350-24T-4G" },
        { "arista", "Arista DCS-7050SX3-48YC8\nSoftware image version: 4.31.2F" },
        { "junos", "Model: ex4300-48p\nJunos: 21.4R3-S5.4" },
        { "aruba-cx", "ArubaOS-CX Version : FL.10.13.1000" },
        { "procurve", "Image stamp:\n WC.16.11.0014" },
        { "comware", "HPE Comware Software, Version 7.1.070" },
        { "huawei", "Huawei Versatile Routing Platform Software" },
        { "dell-os10", "Dell EMC Networking SmartFabric OS10 Enterprise" },
        { "dell-os9", "Dell Networking Operating System\nApplication Software Version: 9.14" },
        { "dell-os6", "Dell Networking N3048" },
        { "extreme-exos", "ExtremeXOS version 32.6.1" },
        { "extreme-voss", "VOSS Software Version 8.10" },
        { "fastiron", "Ruckus ICX7150\nFastIron 09.0" },
        { "alcatel", "OmniSwitch 6860" },
        { "fortiswitch", "Version: FortiSwitch-124F v7.4.3" },
        { "routeros", "version: 7.17 (stable)\nboard-name: CRS326-24G-2S+" },
        { "netgear", "NETGEAR M4300-28G" },
        { "dlink", "Device Type : DGS-3120-24TC" },
        { "tplink", "TP-Link JetStream T2600G-28TS" },
        { "edgeswitch", "Ubiquiti EdgeSwitch 48" },
        { "allied", "AlliedWare Plus Version 5.5.3" },
        { "ruijie", "Ruijie RGOS Software" },
        { "zte", "ZTE ZXR10 5950" },
        { "sonic", "SONiC Software Version: SONiC-OS-4.4.0" }
    };

    [Theory]
    [MemberData(nameof(Identities))]
    public async Task Automatic_backup_identifies_family_before_selecting_commands(string id, string identity)
    {
        var device = _transport.Add("192.0.2.1");
        device.Outputs["show version"] = identity;
        await Run();
        var platform = DevicePlatforms.Find(id)!;
        Assert.Equal(id, DevicePlatforms.Recognize(identity)?.Id);
        Assert.Equal("show version", _transport.Commands.First().Command);
        Assert.Contains(_transport.Commands, c => c.Command == platform.Configuration);
        if (!platform.UsesEnable) Assert.DoesNotContain(_transport.Commands, c => c.Command == "enable");
        var file = Assert.Single(Directory.GetFiles(_directory, "*.txt"));
        Assert.EndsWith($"_{platform.Kind}.txt", file);
        Assert.Contains($"Detected OS : {platform.Name}", File.ReadAllText(file));
        Assert.Contains(_statuses, s => s.StartsWith("Completed"));
        if (platform.Kind == "Switch")
            Assert.DoesNotContain(_transport.Commands, c => c.Command.Contains("wired", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("% Invalid input detected at '^' marker.")]
    [InlineData("Error: Unrecognized command found at '^' position.")]
    [InlineData("Unknown command")]
    [InlineData("% Authorization failed.")]
    [InlineData("syntax error, expecting <command>.")]
    [InlineData("Incomplete command.")]
    [InlineData("Permission denied")]
    [InlineData("bad command name show (line 1 column 1)")]
    public void Cli_errors_are_rejected(string output) => Assert.True(DevicePlatforms.IsRejected(output));

    [Theory]
    [InlineData("hostname Unknown-command\ninterface Ethernet1\n description Error: uplink")]
    [InlineData("0 input errors, 0 CRC, 0 frame")]
    public void Configuration_and_counter_text_are_not_command_errors(string output) => Assert.False(DevicePlatforms.IsRejected(output));

    [Fact]
    public async Task Identity_probes_fall_back_to_the_correct_dialect()
    {
        var device = _transport.Add("192.0.2.1");
        device.Outputs["show version"] = "% Invalid input";
        device.Outputs["show sysinfo"] = "Unknown command";
        device.Outputs["display version"] = "HPE Comware Software";
        await Run();
        Assert.Equal(["show version", "show sysinfo", "display version", "screen-length disable"], _transport.Commands.Take(4).Select(c => c.Command));
        Assert.DoesNotContain(_transport.Commands, c => c.Command == "terminal length 0");
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Wrong_selected_builtin_is_replaced_with_detected_wlc_profile()
    {
        _transport.Add("192.0.2.1").Outputs["show version"] = "Cisco AireOS Wireless Controller";
        await Run("Cisco IOS", CommandProfiles.CiscoIosCommands);
        Assert.Contains(_transport.Commands, c => c.Command == "show run-config commands");
        Assert.DoesNotContain(_transport.Commands, c => c.Command is "show running-config" or "terminal length 0" or "enable");
    }

    [Fact]
    public async Task Unsupported_config_alternative_is_retried_and_recorded()
    {
        var device = _transport.Add("192.0.2.1");
        device.Outputs["show running-config"] = "% Invalid input detected";
        device.Outputs["show run"] = "hostname test-switch";
        await Run();
        var text = File.ReadAllText(Assert.Single(Directory.GetFiles(_directory)));
        Assert.Contains("Command: show run\nResult: Accepted", text.Replace("\r", ""));
        Assert.Contains("Result: Rejected or empty", text);
    }

    [Fact]
    public async Task Rejected_optional_command_reports_warning_but_preserves_valid_configuration()
    {
        _transport.Add("192.0.2.1").Outputs["show inventory"] = "% Invalid input";
        await Run();
        Assert.Contains(_statuses, s => s.StartsWith("Completed with warnings"));
        Assert.Contains("show running-config", File.ReadAllText(Assert.Single(Directory.GetFiles(_directory))));
    }

    [Theory]
    [InlineData("% Authorization failed")]
    [InlineData("")]
    public async Task Failed_configuration_preserves_previous_backup(string error)
    {
        var device = _transport.Add("192.0.2.1");
        await Run();
        string file = Assert.Single(Directory.GetFiles(_directory));
        string original = File.ReadAllText(file);
        _statuses.Clear();
        device.Outputs["show running-config"] = error;
        device.Outputs["show run"] = error;
        await Run();
        Assert.Equal(original, File.ReadAllText(file));
        Assert.DoesNotContain(_statuses, s => s.StartsWith("Completed"));
        string partial = Assert.Single(Directory.GetFiles(_directory, "*.partial-diagnostics.txt"));
        Assert.Contains("NOT A COMPLETE CONFIGURATION BACKUP", File.ReadAllText(partial));
        Assert.Contains(_statuses, s => s.StartsWith("Partial diagnostics saved"));
    }

    [Fact]
    public async Task Unknown_device_does_not_inherit_cisco_commands_or_save()
    {
        var device = _transport.Add("192.0.2.1");
        foreach (var probe in DevicePlatforms.Probes) device.Outputs[probe] = "% Invalid input";
        await Run(save: true);
        Assert.Equal(DevicePlatforms.Probes, _transport.Commands.Select(c => c.Command));
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Contains(_statuses, s => s.Contains("not recognized"));
    }

    [Fact]
    public async Task Edited_script_preserves_order_after_detection_and_paging()
    {
        _transport.Add("192.0.2.1");
        await Run("Custom Profile", ["show clock", "show interfaces description"]);
        Assert.Equal(["show version", "terminal length 0", "show clock", "show interfaces description"], _transport.Commands.Select(c => c.Command));
    }

    [Fact]
    public async Task Aireos_config_fallback_and_catalyst_commands_stay_separate()
    {
        var device = _transport.Add("192.0.2.1");
        device.Outputs["show version"] = "AireOS";
        device.Outputs["show run-config commands"] = "Incorrect usage";
        await Run(save: true);
        Assert.Contains(_transport.Commands, c => c.Command == "show run-config");
        Assert.DoesNotContain(_transport.Commands, c => c.Command is "write memory" or "show wireless client summary" or "terminal length 0");
        Assert.Contains("show ap dot11 24ghz summary", CommandProfiles.CiscoWlcCommands);
        Assert.DoesNotContain("show ap dot11 2.4ghz summary", CommandProfiles.CiscoWlcCommands);
    }

    [Theory]
    [InlineData("<HUAWEI>")]
    [InlineData("[H3C]")]
    [InlineData("admin@ex4300>")]
    [InlineData("(Cisco Controller) >")]
    [InlineData("* X440.1 #")]
    [InlineData("[admin@MikroTik] >")]
    public void Vendor_prompts_are_removed_from_response_body(string prompt) => Assert.Equal("", DevicePlatforms.Body("show version\r\n" + prompt, "show version"));

    [Theory]
    [InlineData("<HUAWEI>", "Huawei VRP Software")]
    [InlineData("[H3C]", "H3C Comware Software")]
    [InlineData("admin@ex4300>", "Junos: 21.4R3")]
    [InlineData("(Cisco Controller) >", "AireOS")]
    [InlineData("* X440.1 #", "ExtremeXOS")]
    [InlineData("[admin@MikroTik] >", "MikroTik RouterOS")]
    public async Task Interactive_backup_handles_vendor_prompts(string prompt, string identity)
    {
        var device = _transport.Add("192.0.2.1");
        device.PromptOverride = prompt;
        device.Outputs["show version"] = identity;
        await Run();
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Contains(_statuses, s => s.StartsWith("Completed"));
    }

    [Fact]
    public async Task Explicit_custom_script_is_not_replaced_even_when_it_matches_a_template()
    {
        _transport.Add("192.0.2.1").Outputs["show version"] = "Cisco AireOS Wireless Controller";
        await Run("Custom Profile", CommandProfiles.CiscoIosCommands);
        Assert.Contains(_transport.Commands, c => c.Command == "show running-config");
        Assert.DoesNotContain(_transport.Commands, c => c.Command == "show run-config commands");
    }

    [Fact]
    public async Task Custom_script_preserves_repeated_commands()
    {
        _transport.Add("192.0.2.1");
        await Run("Custom Profile", ["show clock", "show clock"]);
        Assert.Equal(2, _transport.Commands.Count(c => c.Command == "show clock"));
    }

    [Fact]
    public void Colored_prompt_alone_is_not_configuration_content() =>
        Assert.Equal("", DevicePlatforms.Body("show running-config\r\n\u001b[32mSW#\u001b[0m", "show running-config"));

    [Theory]
    [InlineData("--More--", " ")]
    [InlineData("Press Enter to continue", "\r")]
    [InlineData("Would you like to display the next 15 entries? (y/n)", "y")]
    public async Task Split_pager_markers_receive_the_correct_response(string marker, string reply)
    {
        var device = _transport.Add("192.0.2.1");
        device.PagerMarker = marker;
        device.PagedCommands.Add("show running-config");
        await Run();
        Assert.Contains(_transport.Commands, c => c.Command == reply);
        Assert.Contains("remaining page", File.ReadAllText(Assert.Single(Directory.GetFiles(_directory))));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
