namespace BulkBackup.Core.Models;

public sealed class CaptureManifest
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
    public string CollectorVersion { get; set; } = "core/" + (typeof(CaptureManifest).Assembly.GetName().Version?.ToString() ?? "unknown");
    public string CommandProfileVersion { get; set; } = "2026-10-26.1";
    public string CollectionPurpose { get; set; } = "Configuration Backup";
    public bool CounterRateSampling { get; set; }
    public string ExportKind { get; set; } = "privateRaw";
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
    public string SnapshotId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string IdentityBasis { get; set; } = "managementIp";
    public string? Family { get; set; }
    public string? SerialNumber { get; set; }
    public string? ChassisId { get; set; }
    public string? Model { get; set; }
    public string? Role { get; set; }
    public List<DeviceMember> Members { get; set; } = [];
    public string? UplinkDevice { get; set; }
    public string? UplinkPort { get; set; }
    public DateTimeOffset? CollectionStartedAtUtc { get; set; }
    public DateTimeOffset? CollectionCompletedAtUtc { get; set; }
    public double? DurationMilliseconds { get; set; }
    public string Completeness { get; set; } = "notCollected";
    public string? SnapshotFile { get; set; }
    public string? OutputSha256 { get; set; }
    public List<CounterRate> CounterRates { get; set; } = [];
    public string? CounterInterpretation { get; set; }
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
    public string CompletionCheck { get; set; } = "promptDelimited";
    public string RequestedCommand { get; set; } = "";
    public string CanonicalCommand { get; set; } = "";
    public string Outcome { get; set; } = "notAttempted";
    public string Completeness { get; set; } = "notCollected";
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public double? DurationMilliseconds { get; set; }
    public int? SampleNumber { get; set; }
    public string? SampleGroup { get; set; }
    public string Command { get; set; } = "";
    public string Stage { get; set; } = "capture";
    public string Status { get; set; } = "notAttempted";
    public string? Reason { get; set; }
    public List<CommandAttempt> Attempts { get; set; } = [];
}

public sealed record CommandAttempt(string Command, DateTimeOffset CapturedAtUtc, string Status, string? Reason)
{
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
    public double DurationMilliseconds { get; init; }
    public string Outcome { get; init; } = "notAttempted";
    public string Completeness { get; init; } = "notCollected";
    public bool PromptReceived { get; init; }
    public string RawOutput { get; init; } = "";
}

public sealed record DeviceMember(string MemberId, string? Role, string? SerialNumber, string? ChassisId);

public sealed record CaptureOptions(bool Assessment = false, bool CounterRateSampling = false,
    int CounterIntervalSeconds = 30, string? CollectorVersion = null);

public sealed record CounterRate(string Interface, string Counter, long First, long Second,
    double IntervalSeconds, long? Delta, double? PerSecond, string Status);
