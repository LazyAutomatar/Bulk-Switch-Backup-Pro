namespace BulkBackup.Core.Models;

public sealed class CaptureManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Application { get; set; } = "Bulk Switch Backup Pro";
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string SelectedProfile { get; set; } = "";
    public List<string> RequestedTargets { get; set; } = [];
    public List<DeviceCapture> Devices { get; set; } = [];
    public List<string> UnverifiedFiles { get; set; } = [];
}

public sealed class DeviceCapture
{
    public string IpAddress { get; set; } = "";
    public string Hostname { get; set; } = "";
    public DateTimeOffset? CapturedAtUtc { get; set; }
    public string? Platform { get; set; }
    public string? DeviceType { get; set; }
    public string Profile { get; set; } = "";
    public string? IdentityEvidence { get; set; }
    public string? Protocol { get; set; }
    public string Status { get; set; } = "notAttempted";
    public string? Reason { get; set; } = "Capture not started.";
    public bool ConfigurationCaptured { get; set; }
    public bool SaveRequested { get; set; }
    public string? OutputFile { get; set; }
    public List<CommandCapture> Commands { get; set; } = [];
}

public sealed class CommandCapture
{
    public string Command { get; set; } = "";
    public string Stage { get; set; } = "capture";
    public string Status { get; set; } = "notAttempted";
    public string? Reason { get; set; }
    public List<CommandAttempt> Attempts { get; set; } = [];
}

public sealed record CommandAttempt(string Command, DateTimeOffset CapturedAtUtc, string Status, string? Reason);
