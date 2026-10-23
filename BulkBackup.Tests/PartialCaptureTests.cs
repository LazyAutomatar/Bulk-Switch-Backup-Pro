using BulkBackup.Core.Models;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public class PartialCaptureTests
{
    [Fact]
    public async Task TimeoutRetainsEarlierCommandsWithoutRunningMoreOnAnUnsynchronizedShell()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bulk-partial-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var transport = new FakeBackupTransport();
            transport.Add("192.0.2.1").FailedCommands.Add("show running-config");
            var engine = new NetworkEngine(transport);
            var failures = new List<CommandFailure>();
            engine.CommandFailed += failures.Add;
            await engine.StartEngineAsync(["192.0.2.1"], [new() { Username = "test", Password = "synthetic" }],
                false, 0, "", false, 1, ["show inventory", "show running-config", "show vlan"], "Custom Profile", false,
                directory, (_, _) => { }, (_, _, _, _, _, _, _, _) => { }, CancellationToken.None);
            string partial = Assert.Single(Directory.GetFiles(directory));
            Assert.EndsWith(".partial-diagnostics.txt", partial);
            Assert.Contains("captured show inventory", File.ReadAllText(partial));
            Assert.DoesNotContain(transport.Commands, c => c.Command == "show vlan");
            Assert.Equal("TimeoutException", Assert.Single(failures).Error);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RejectedConfigurationDoesNotStopWorkingCommandsAndReportsEveryFailedAttempt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "bulk-partial-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var transport = new FakeBackupTransport();
            var device = transport.Add("192.0.2.1");
            device.Outputs["show running-config"] = "% Authorization failed";
            device.Outputs["show run"] = "% Authorization failed";
            var engine = new NetworkEngine(transport);
            var failures = new List<CommandFailure>();
            var statuses = new List<string>();
            engine.CommandFailed += failures.Add;
            await engine.StartEngineAsync(["192.0.2.1"], [new() { Username = "test", Password = "synthetic" }],
                false, 0, "", false, 1, ["show running-config", "show inventory", "show vlan"], "Custom Profile", false,
                directory, (_, _) => { }, (_, _, status, _, _, _, _, _) => statuses.Add(status), CancellationToken.None);
            Assert.Contains(transport.Commands, c => c.Command == "show inventory");
            Assert.Contains(transport.Commands, c => c.Command == "show vlan");
            var file = Assert.Single(Directory.GetFiles(directory));
            Assert.EndsWith(".partial-diagnostics.txt", file);
            var text = File.ReadAllText(file);
            Assert.Contains("captured show inventory", text);
            Assert.Contains("captured show vlan", text);
            Assert.Equal(2, failures.Count);
            Assert.All(failures, f => Assert.Contains("Authorization failed", f.Response));
            Assert.DoesNotContain(statuses, s => s.StartsWith("Completed"));
            Assert.Contains(statuses, s => s.StartsWith("Partial"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
