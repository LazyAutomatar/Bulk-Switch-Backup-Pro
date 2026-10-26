using System.IO.Compression;
using System.Text.Json;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class WlcCaptureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BulkBackup.Wlc", Guid.NewGuid().ToString("N"));
    private readonly FakeBackupTransport _transport = new();
    private readonly List<string> _statuses = [];
    private readonly List<CommandFailure> _failures = [];
    private static readonly string[] EvidenceCommands =
    [
        "show platform resources", "show environment all", "show ap uptime",
        "show wireless stats ap history", "show wireless wps rogue ap summary", "show ap dot11 5ghz cleanair config"
    ];

    private FakeDevice Controller()
    {
        var device = _transport.Add("192.0.2.1", "WLC-9800");
        device.Outputs["show version"] = "Cisco IOS XE Software, Catalyst 9800 C9800-CL";
        foreach (string command in EvidenceCommands) device.Outputs[command] = "Evidence from " + command;
        return device;
    }

    private async Task<CaptureManifest> Run(bool save = false, bool router = false)
    {
        var engine = new NetworkEngine(_transport);
        engine.CommandFailed += _failures.Add;
        await engine.StartEngineAsync(router ? ["192.0.2.1", "192.0.2.2"] : ["192.0.2.1"],
            [new() { Username = "operator", Password = "never-export-this-password" }], false, 0, "", false, 1,
            CommandProfiles.CiscoWlcCommands.ToList(), "Cisco Catalyst 9800 WLC (IOS-XE)", save, _directory,
            (_, _) => { }, (_, _, status, _, _, _, _, _) => _statuses.Add(status), CancellationToken.None);
        return JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(BackupArchive.MetadataPath(_directory)), BackupArchive.JsonOptions)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Supported_Wlc_evidence_and_Zip_manifest_preserve_identity_and_explicit_save(bool save)
    {
        Controller();
        _transport.Add("192.0.2.2", "UNREACHABLE-ROUTER").Reachable = false;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "192.0.2.99_OMITTED_ROUTER.txt"), "Older unrelated backup");
        File.WriteAllText(Path.Combine(_directory, "192.0.2.2_OLD_ROUTER.txt"), "Previous router backup");
        var manifest = await Run(save, router: true);
        var wlc = Assert.Single(manifest.Devices, d => d.IpAddress == "192.0.2.1");
        Assert.Equal("success", wlc.Status);
        Assert.True(wlc.ConfigurationCaptured);
        Assert.Equal("WLC-9800", wlc.Hostname);
        Assert.Equal("c9800", wlc.Platform);
        Assert.Contains("Catalyst 9800", wlc.IdentityEvidence);
        Assert.Equal(TimeSpan.Zero, wlc.CapturedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, manifest.CompletedAtUtc!.Value.Offset);
        foreach (string command in EvidenceCommands)
        {
            var result = Assert.Single(wlc.Commands, c => c.Stage == "capture" && c.Command == command);
            Assert.Equal("success", result.Status);
            Assert.Equal(command, Assert.Single(result.Attempts).Command);
        }
        Assert.Equal(save ? 1 : 0, _transport.Commands.Count(c => c.Command == "write memory"));
        Assert.Equal(save, wlc.SaveRequested);
        var unreachable = Assert.Single(manifest.Devices, d => d.IpAddress == "192.0.2.2");
        Assert.Equal("unreachable", unreachable.Status);
        Assert.False(unreachable.ConfigurationCaptured);
        Assert.Null(unreachable.OutputFile);
        Assert.All(unreachable.Commands, c => Assert.Equal("notAttempted", c.Status));
        string zip = Path.Combine(_directory, "export.zip");
        BackupArchive.Create(_directory, zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(2, archive.Entries.Count);
        Assert.NotNull(archive.GetEntry(wlc.OutputFile!));
        Assert.DoesNotContain(archive.Entries, e => e.Name.Contains("ROUTER"));
        using var reader = new StreamReader(archive.GetEntry(BackupArchive.ManifestName)!.Open());
        string json = reader.ReadToEnd();
        Assert.DoesNotContain("never-export-this-password", json);
        var exported = JsonSerializer.Deserialize<CaptureManifest>(json, BackupArchive.JsonOptions)!;
        Assert.Equal(2, exported.Devices.Count);
        Assert.DoesNotContain(exported.Devices, d => d.IpAddress == "192.0.2.99");
    }

    [Fact]
    public async Task Unsupported_history_and_CleanAir_are_not_satisfied_by_logging_or_radio_summary()
    {
        var device = Controller();
        device.Outputs["show wireless stats ap history"] = "% Invalid input detected at '^' marker.";
        device.Outputs["show ap dot11 5ghz cleanair config"] = "% This command is not supported on this platform";
        device.Outputs["show wireless wps rogue ap summary"] = "% Authorization failed";
        var manifest = await Run();
        var wlc = Assert.Single(manifest.Devices);
        Assert.Equal("partial", wlc.Status);
        Assert.True(wlc.ConfigurationCaptured);
        foreach (string command in new[] { "show wireless stats ap history", "show ap dot11 5ghz cleanair config" })
        {
            var result = Assert.Single(wlc.Commands, c => c.Command == command);
            Assert.Equal("unsupported", result.Status);
            Assert.NotNull(result.Reason);
            Assert.Equal(command, Assert.Single(result.Attempts).Command);
            Assert.Equal([command], DevicePlatforms.Alternatives(DevicePlatforms.Find("c9800"), command));
        }
        Assert.Equal("failure", wlc.Commands.Single(c => c.Command == "show wireless wps rogue ap summary").Status);
        Assert.Equal("success", wlc.Commands.Single(c => c.Command == "show logging").Status);
        Assert.Equal("success", wlc.Commands.Single(c => c.Command == "show ap dot11 5ghz summary").Status);
        Assert.Contains(_transport.Commands, c => c.Command == "show startup-config");
        Assert.Equal(3, _failures.Count);
    }

    [Fact]
    public async Task Empty_explicit_save_response_is_a_failure_without_discarding_configuration()
    {
        Controller().Outputs["write memory"] = "";
        var manifest = await Run(save: true);
        var wlc = Assert.Single(manifest.Devices);
        Assert.True(wlc.ConfigurationCaptured);
        Assert.Equal("partial", wlc.Status);
        Assert.Equal("failure", wlc.Commands.Single(c => c.Stage == "save").Status);
        Assert.NotNull(wlc.OutputFile);
    }

    [Fact]
    public async Task Timeout_preserves_failure_and_collects_later_commands_on_a_different_shell()
    {
        Controller().FailedCommands.Add("show wireless stats ap history");
        var manifest = await Run();
        var wlc = Assert.Single(manifest.Devices);
        Assert.Equal("partial", wlc.Status);
        Assert.True(wlc.ConfigurationCaptured);
        var history = wlc.Commands.Single(c => c.Command == "show wireless stats ap history");
        Assert.Equal("failure", history.Status);
        Assert.Contains("TimeoutException", history.Reason);
        Assert.Equal("success", wlc.Commands.Single(c => c.Command == "show ap dot11 5ghz cleanair config").Status);
        int failedShell = _transport.ShellCommands.Single(c => c.Command == history.Command).Shell;
        int laterShell = _transport.ShellCommands.Single(c => c.Command == "show ap dot11 5ghz cleanair config").Shell;
        Assert.NotEqual(failedShell, laterShell);
        Assert.Equal(2, _transport.Attempts.Count);
        Assert.Equal(0, _transport.Active);
        Assert.DoesNotContain(_transport.Commands, c => c.Command == "write memory");
    }

    [Fact]
    public async Task Missing_configuration_exports_partial_diagnostics_and_never_saves_or_reuses_old_backup()
    {
        var device = Controller();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "192.0.2.1_WLC-9800_WLC.txt"), "Previous configuration");
        device.FailedCommands.Add("show running-config");
        var manifest = await Run(save: true);
        var wlc = Assert.Single(manifest.Devices);
        Assert.Equal("partial", wlc.Status);
        Assert.False(wlc.ConfigurationCaptured);
        Assert.EndsWith(".partial-diagnostics.txt", wlc.OutputFile);
        Assert.Contains(_transport.Commands, c => c.Command == "show startup-config");
        Assert.DoesNotContain(_transport.Commands, c => c.Command == "write memory");
        Assert.DoesNotContain(_statuses, s => s.StartsWith("Completed"));
        string zip = Path.Combine(_directory, "partial.zip");
        BackupArchive.Create(_directory, zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry(wlc.OutputFile!));
        Assert.Null(archive.GetEntry("192.0.2.1_WLC-9800_WLC.txt"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
