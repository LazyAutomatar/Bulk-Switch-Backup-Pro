using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BulkBackup.Core.Models;

namespace BulkBackup.Core.Services;

public static class BackupArchive
{
    public const string ManifestName = "capture-manifest.json";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static string MetadataPath(string directory) => Path.Combine(directory, ".capture", ManifestName);

    public static async Task FreezeDeviceAsync(string directory, DeviceCapture device)
    {
        if (device.OutputFile == null) return;
        string relative = Path.Combine(".capture", "snapshots", device.SnapshotId, device.OutputFile);
        string snapshot = SafePath(directory, relative);
        PrivateStorage.CreateDirectory(Path.GetDirectoryName(snapshot)!);
        byte[] bytes = await File.ReadAllBytesAsync(SafePath(directory, device.OutputFile));
        await using (var stream = PrivateStorage.CreateFile(snapshot)) await stream.WriteAsync(bytes);
        device.SnapshotFile = relative.Replace('\\', '/');
        device.OutputSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static async Task WriteManifestAsync(string directory, CaptureManifest manifest)
    {
        string path = MetadataPath(directory);
        PrivateStorage.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        string history = Path.Combine(directory, ".capture", "snapshots", manifest.SnapshotId, ManifestName);
        PrivateStorage.CreateDirectory(Path.GetDirectoryName(history)!);
        await PrivateStorage.WriteTextAsync(history, json);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await PrivateStorage.WriteTextAsync(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string SafePath(string root, string relative)
    {
        root = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw new InvalidDataException("Capture file is outside the backup directory.");
        for (var info = new FileInfo(path) as FileSystemInfo; info != null && info.FullName != root; info = Directory.GetParent(info.FullName))
            if (info.LinkTarget != null) throw new InvalidDataException("Capture paths cannot contain symbolic links.");
        return path;
    }

    public static void Create(string sourceDirectory, string destinationZip, bool redacted = false)
    {
        string root = Path.GetFullPath(sourceDirectory);
        string metadata = MetadataPath(root);
        CaptureManifest manifest;
        if (File.Exists(metadata))
            manifest = JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(metadata), JsonOptions)
                ?? throw new InvalidDataException("Capture manifest is empty.");
        else
        {
            manifest = new CaptureManifest { CompletedAtUtc = DateTimeOffset.UtcNow };
            manifest.UnverifiedFiles = Directory.GetFiles(root, "*.txt").Select(Path.GetFileName).OfType<string>().ToList();
        }
        var redactor = redacted ? new SharingRedactor() : null;
        manifest.ExportKind = redacted ? "redactedSharing" : "privateRaw";
        string temporary = Path.GetFullPath(destinationZip) + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = PrivateStorage.CreateFile(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var device in manifest.Devices.Where(d => d.OutputFile != null))
                {
                    _ = SafePath(root, device.OutputFile!);
                    if (!emitted.Add(device.OutputFile!)) throw new InvalidDataException("Two devices reference the same output filename.");
                    if (!string.IsNullOrEmpty(device.SnapshotId) && device.SnapshotId != manifest.SnapshotId)
                        throw new InvalidDataException("Device output belongs to a different snapshot.");
                    if (!string.IsNullOrEmpty(device.SnapshotId) && device.OutputSha256 == null)
                        throw new InvalidDataException("Snapshot output was not finalized; export refused.");
                    byte[] bytes = File.ReadAllBytes(SafePath(root, device.SnapshotFile ?? device.OutputFile!));
                    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (device.OutputSha256 != null && !string.Equals(device.OutputSha256, hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Snapshot output changed after collection; export refused.");
                    if (redactor != null) bytes = System.Text.Encoding.UTF8.GetBytes(redactor.Redact(System.Text.Encoding.UTF8.GetString(bytes)));
                    using (var entry = archive.CreateEntry(device.OutputFile!.Replace('\\', '/')).Open()) entry.Write(bytes);
                    device.SnapshotFile = null; // ZIP resolves outputFile within this archive.
                    device.OutputSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                }
                foreach (string relative in manifest.UnverifiedFiles)
                {
                    byte[] bytes = File.ReadAllBytes(SafePath(root, relative));
                    if (redactor != null) bytes = System.Text.Encoding.UTF8.GetBytes(redactor.Redact(System.Text.Encoding.UTF8.GetString(bytes)));
                    using var entry = archive.CreateEntry(relative).Open();
                    entry.Write(bytes);
                }
                // Redact individual string values, never JSON syntax or field names.
                JsonNode json = JsonSerializer.SerializeToNode(manifest, JsonOptions)!;
                if (redactor != null) RedactValues(json, redactor);
                using var writer = new StreamWriter(archive.CreateEntry(ManifestName).Open());
                writer.Write(json.ToJsonString(JsonOptions));
            }
            File.Move(temporary, destinationZip, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RedactValues(JsonNode node, SharingRedactor redactor)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[pair.Key] = redactor.Redact(text);
                else if (pair.Value != null) RedactValues(pair.Value, redactor);
            }
        else if (node is JsonArray array)
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = redactor.Redact(text);
                else if (array[i] != null) RedactValues(array[i]!, redactor);
            }
    }
}
