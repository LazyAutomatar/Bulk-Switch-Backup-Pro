using System.IO.Compression;
using System.Text.Json;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class AssessmentSnapshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bulk-assessment-" + Guid.NewGuid().ToString("N"));
    private readonly FakeBackupTransport _transport = new();
    private async Task<CaptureManifest> Run(CaptureOptions? options = null, string[]? targets = null, bool save = false)
    {
        await new NetworkEngine(_transport).StartEngineAsync((targets ?? ["192.0.2.1"]).ToList(),
            [new() { Username = "test", Password = "synthetic-password" }], false, 0, "", false, 2,
            [], "Auto", save, _directory, (_, _) => { }, (_, _, _, _, _, _, _, _) => { },
            CancellationToken.None, options ?? new CaptureOptions(Assessment: true));
        return JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(BackupArchive.MetadataPath(_directory)), BackupArchive.JsonOptions)!;
    }

    [Theory]
    [InlineData("Cisco IOS XE Software, C9300-48P", true)]
    [InlineData("Cisco IOS Software, C1000-48P", false)]
    [InlineData("Cisco IOS XE Software, C9500-48Y4C", false)]
    [InlineData("Cisco IOS XE Software", false)]
    [InlineData("Cisco Small Business SG350-28P", false)]
    public async Task Assessment_collects_reporter_commands_and_only_supported_stack_commands(string identity, bool stack)
    {
        _transport.Add("192.0.2.1").Outputs["show version"] = identity;
        var manifest = await Run(save: true);
        var device = Assert.Single(manifest.Devices);
        foreach (string command in AssessmentProfiles.Catalyst.Where(c => !c.StartsWith("show switch")))
            Assert.Contains(device.Commands, c => c.Stage == "capture" && c.Command == command);
        Assert.Equal(stack, _transport.Commands.Any(c => c.Command == "show switch detail"));
        Assert.Equal(stack, _transport.Commands.Any(c => c.Command == "show switch stack-ring speed"));
        Assert.DoesNotContain(_transport.Commands, c => c.Command == "write memory");
        Assert.False(device.SaveRequested);
        Assert.Equal("Assessment", manifest.CollectionPurpose);
        Assert.Equal("switch", device.Family);
        Assert.Contains("Command: show version", File.ReadAllText(Path.Combine(_directory, device.OutputFile!)));
    }

    [Fact]
    public async Task Ap_identity_and_wired_data_survive_unsupported_capwap()
    {
        var ap = _transport.Add("192.0.2.1", "AP-1");
        ap.Outputs["show version"] = "Cisco AP-COS C9120AXI Software";
        ap.Outputs["show capwap client rcb"] = "% Invalid input detected";
        ap.Outputs["show capwap client config"] = "Primary Controller Name: WLC-1\nPrimary Controller IP: 192.0.2.9";
        var device = Assert.Single((await Run()).Devices);
        Assert.Equal("ap", device.Family);
        foreach (string command in new[] { "show version", "show inventory", "show capwap client config", "show interfaces wired 0" })
            Assert.Contains(device.Commands, c => c.Stage == "capture" && c.Command == command && c.Completeness == "complete");
        Assert.Equal("unsupported", device.Commands.Single(c => c.Command == "show capwap client rcb").Outcome);
        Assert.NotNull(device.OutputFile);
    }

    [Theory]
    [InlineData("% Invalid input detected", "unsupported")]
    [InlineData("% Authorization failed", "unauthorized")]
    [InlineData("", "empty")]
    [InlineData("Rows 1-12\n[output truncated]", "truncated")]
    public async Task Detailed_outcomes_preserve_legacy_fields_and_exact_output(string body, string expected)
    {
        _transport.Add("192.0.2.1").Outputs["show inventory"] = body;
        var device = Assert.Single((await Run()).Devices);
        var command = device.Commands.Single(c => c.Stage == "capture" && c.Command == "show inventory");
        Assert.Equal(expected, command.Outcome);
        Assert.Equal("incomplete", command.Completeness);
        Assert.Equal("show inventory", command.RequestedCommand);
        Assert.Equal("show inventory", command.CanonicalCommand);
        var attempt = Assert.Single(command.Attempts);
        Assert.Equal($"show inventory\r\n{body}\r\nSW-192-0-2-1#", attempt.RawOutput);
        Assert.True(attempt.PromptReceived);
        Assert.True(attempt.DurationMilliseconds >= 0);
        Assert.True(attempt.CompletedAtUtc >= attempt.StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, attempt.StartedAtUtc.Offset);
        Assert.Equal("partial", device.Status);
    }

    [Fact]
    public async Task Accepted_configuration_without_end_is_incomplete_and_never_reuses_previous_file()
    {
        var fake = _transport.Add("192.0.2.1");
        var first = await Run();
        var old = Assert.Single(first.Devices);
        string oldText = File.ReadAllText(Path.Combine(_directory, old.OutputFile!));
        fake.Outputs["show running-config"] = "hostname PARTIAL";
        fake.Outputs["show run"] = "hostname PARTIAL";
        var device = Assert.Single((await Run()).Devices);
        var command = device.Commands.Single(c => c.Command == "show running-config");
        Assert.Equal("accepted", command.Outcome);
        Assert.Equal("incomplete", command.Completeness);
        Assert.False(device.ConfigurationCaptured);
        Assert.Equal(oldText, File.ReadAllText(Path.Combine(_directory, old.OutputFile!)));
        Assert.EndsWith(".partial-diagnostics.txt", device.OutputFile);
    }

    [Fact]
    public async Task Paired_controllers_keep_independent_identity_and_collection_windows()
    {
        foreach (var (ip, serial, role) in new[] { ("192.0.2.1", "SERIAL-A", "ACTIVE"), ("192.0.2.2", "SERIAL-B", "STANDBY HOT") })
        {
            var device = _transport.Add(ip, "SHARED-WLC");
            device.Outputs["show version"] = $"Cisco IOS XE Software, C9800-L\nProcessor board ID {serial}\nBase Ethernet MAC Address : 0011.2233.4455";
            device.Outputs["show redundancy"] = "My State = " + role;
            device.Outputs["show inventory"] = $"NAME: \"Chassis\"\nPID: C9800-L, VID: V01, SN: {serial}";
        }
        var manifest = await Run(new CaptureOptions(true, CollectorVersion: "test-collector-1"), targets: ["192.0.2.1", "192.0.2.2", "192.0.2.3"]);
        Assert.Equal(3, manifest.Devices.Count);
        Assert.Equal(3, manifest.Devices.Select(d => d.DeviceId).Distinct().Count());
        Assert.All(manifest.Devices, d => Assert.Equal(manifest.SnapshotId, d.SnapshotId));
        Assert.Equal("SERIAL-A", manifest.Devices[0].SerialNumber);
        Assert.Equal("active", manifest.Devices[0].Role);
        Assert.Equal("standby hot", manifest.Devices[1].Role);
        Assert.All(manifest.Devices.Take(2), d => Assert.Equal("wlc", d.Family));
        Assert.All(manifest.Devices, d => Assert.True(d.CollectionCompletedAtUtc >= d.CollectionStartedAtUtc));
        Assert.Equal("unreachable", manifest.Devices[2].Status);
        Assert.Null(manifest.Devices[2].OutputFile);
        Assert.Equal("test-collector-1", manifest.CollectorVersion);
        Assert.False(string.IsNullOrEmpty(manifest.CommandProfileVersion));
    }

    [Fact]
    public async Task Snapshot_export_ignores_overwritten_working_files_and_rejects_tampered_snapshot()
    {
        _transport.Add("192.0.2.1");
        var manifest = await Run();
        var device = Assert.Single(manifest.Devices);
        File.WriteAllText(Path.Combine(_directory, device.OutputFile!), "Different run content");
        string zip = Path.Combine(_directory, "frozen.zip");
        BackupArchive.Create(_directory, zip);
        using (var archive = ZipFile.OpenRead(zip))
        using (var reader = new StreamReader(archive.GetEntry(device.OutputFile!)!.Open()))
        {
            string text = reader.ReadToEnd();
            Assert.Contains(manifest.SnapshotId, text);
            Assert.DoesNotContain("Different run content", text);
        }
        File.AppendAllText(Path.Combine(_directory, device.SnapshotFile!), "modified");
        Assert.Throws<InvalidDataException>(() => BackupArchive.Create(_directory, zip));
    }

    [Fact]
    public async Task Repeated_runs_keep_old_snapshot_and_latest_unreachable_target_without_stale_text()
    {
        var fake = _transport.Add("192.0.2.1");
        var first = await Run();
        fake.Reachable = false;
        var second = await Run();
        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
        Assert.True(File.Exists(Path.Combine(_directory, first.Devices[0].SnapshotFile!)));
        string zip = Path.Combine(_directory, "latest.zip");
        BackupArchive.Create(_directory, zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(BackupArchive.ManifestName, Assert.Single(archive.Entries).Name);
    }

    [Fact]
    public async Task Counter_sampling_has_two_timestamped_outputs_and_reset_aware_rates()
    {
        var fake = _transport.Add("192.0.2.1");
        int samples = 0;
        fake.OnCommand = command =>
        {
            if (command == "show interfaces counters errors")
                fake.Outputs[command] = ++samples == 1 ? "Port FCS-Err OutDiscards\nGi1/0/1 10 50" : "Port FCS-Err OutDiscards\nGi1/0/1 14 1";
        };
        var device = Assert.Single((await Run(new CaptureOptions(true, true, 1))).Devices);
        Assert.Equal(2, samples);
        var captures = device.Commands.Where(c => c.SampleNumber != null).ToArray();
        Assert.Equal(new int?[] { 1, 2 }, captures.Select(c => c.SampleNumber));
        Assert.True((captures[1].StartedAtUtc - captures[0].CompletedAtUtc)!.Value.TotalSeconds >= .95);
        var errors = device.CounterRates.Single(c => c.Counter == "FCS-Err");
        Assert.Equal(4, errors.Delta);
        Assert.True(errors.PerSecond > 0);
        var discard = device.CounterRates.Single(c => c.Counter == "OutDiscards");
        Assert.Equal("resetOrWrap", discard.Status);
        Assert.Null(discard.PerSecond);
        Assert.Contains("do not", device.CounterInterpretation);
    }

    [Fact]
    public async Task Sharing_export_redacts_secrets_in_text_and_manifest_without_changing_topology_or_raw_files()
    {
        var fake = _transport.Add("192.0.2.1", "CORE");
        string config = "hostname CORE\ninterface GigabitEthernet1/0/1\n description AP-1\n switchport access vlan 42\n spanning-tree portfast\n" +
            "username admin secret 9 adminSECRET\nenable secret 9 enableSECRET\nsnmp-server community snmpSECRET RO\n" +
            "radius-server host 192.0.2.9 key 7 radiusSECRET\n key-string chainSECRET\n" +
            "wpa-psk ascii 0 wirelessSECRET\ncrypto key generate rsa modulus 2048\nend";
        fake.Outputs["show running-config"] = config;
        var manifest = await Run();
        string raw = File.ReadAllText(Path.Combine(_directory, manifest.Devices[0].OutputFile!));
        string zip = Path.Combine(_directory, "sharing.zip");
        BackupArchive.Create(_directory, zip, redacted: true);
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                string text = reader.ReadToEnd();
                foreach (string secret in new[] { "adminSECRET", "enableSECRET", "snmpSECRET", "radiusSECRET", "chainSECRET", "wirelessSECRET" }) Assert.DoesNotContain(secret, text);
                Assert.Contains("CORE", text);
                Assert.Contains("GigabitEthernet1/0/1", text);
                Assert.Contains("switchport access vlan 42", text);
                Assert.Contains("192.0.2.9", text);
                Assert.Contains("crypto key generate rsa modulus 2048", text);
                if (entry.Name == BackupArchive.ManifestName)
                {
                    var exported = JsonSerializer.Deserialize<CaptureManifest>(text, BackupArchive.JsonOptions)!;
                    Assert.Equal("redactedSharing", exported.ExportKind);
                    Assert.Equal(manifest.SnapshotId, exported.SnapshotId);
                }
            }
        }
        Assert.Equal(raw, File.ReadAllText(Path.Combine(_directory, manifest.Devices[0].OutputFile!)));
        Assert.Contains("adminSECRET", raw);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(zip));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(BackupArchive.MetadataPath(_directory)));
        }
    }

    [Fact]
    public async Task Incomplete_transport_retains_partial_raw_output_and_detailed_timeout()
    {
        var capture = new CommandCapture { Command = "show interfaces counters errors" };
        await Assert.ThrowsAsync<IncompleteCommandException>(() => CaptureEvidence.ExecuteAsync(capture, capture.Command,
            () => throw new IncompleteCommandException("No prompt", "Gi1/0/1 23", "timedOut")));
        Assert.Equal("timedOut", capture.Outcome);
        Assert.Equal("failure", capture.Status);
        Assert.False(Assert.Single(capture.Attempts).PromptReceived);
        Assert.Equal("Gi1/0/1 23", capture.Attempts[0].RawOutput);
    }

    [Fact]
    public async Task Stack_members_retain_roles_hardware_identity_and_router_family_is_explicit()
    {
        var device = _transport.Add("192.0.2.1");
        device.Outputs["show version"] = "Cisco IOS XE Software C9300-48P";
        device.Outputs["show inventory"] = "NAME: \"Switch 1\"\nPID: C9300-48P, VID: V01, SN: STACK-A\nNAME: \"Switch 2\"\nPID: C9300-48P, VID: V01, SN: STACK-B";
        device.Outputs["show switch detail"] = "*1 Active 0011.2233.4455 15 Ready\n 2 Standby 0011.2233.4466 14 Ready";
        var stack = Assert.Single((await Run()).Devices);
        Assert.Equal(2, stack.Members.Count);
        Assert.Equal("STACK-B", stack.Members[1].SerialNumber);
        Assert.Equal("standby", stack.Members[1].Role);
        device.Outputs["show version"] = "Cisco IOS XE Software ISR4431/K9\nProcessor board ID ROUTER-1";
        var router = Assert.Single((await Run()).Devices);
        Assert.Equal("router", router.Family);
        Assert.Equal("ROUTER-1", router.SerialNumber);
    }

    [Fact]
    public void Redaction_preserves_key_relationships_and_covers_quoted_and_truncated_private_material()
    {
        var redactor = new SharingRedactor();
        string text = "key chain OSPF\n key 1\n key-string \"shared secret with spaces\"\nsnmp-server user usr grp auth sha authSECRET priv aes 128 privSECRET\nset password ENC encSECRET\npassword policy strong\nno password\ninterface Gi1/0/1\n-----BEGIN RSA PRIVATE KEY-----\nprivateKEYpayload";
        string redacted = redactor.Redact(text);
        foreach (string secret in new[] { "shared secret with spaces", "authSECRET", "privSECRET", "encSECRET", "privateKEYpayload" }) Assert.DoesNotContain(secret, redacted);
        Assert.Contains("key chain OSPF", redacted);
        Assert.Contains("key 1", redacted);
        Assert.Contains("password policy strong", redacted);
        Assert.Contains("no password\ninterface Gi1/0/1", redacted);
        Assert.Equal(redactor.Redact("password repeatSECRET").Split(' ')[1], redactor.Redact("secret repeatSECRET").Split(' ')[1]);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
