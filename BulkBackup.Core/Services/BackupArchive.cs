using System.IO.Compression;
using System.Text.Json;
using BulkBackup.Core.Models;

namespace BulkBackup.Core.Services;

public static class BackupArchive
{
    public const string ManifestName = "capture-manifest.json";
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static string MetadataPath(string directory) => Path.Combine(directory, ".capture", ManifestName);

    public static async Task WriteManifestAsync(string directory, CaptureManifest manifest)
    {
        string path = MetadataPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public static void Create(string sourceDirectory, string destinationZip)
    {
        string root = Path.GetFullPath(sourceDirectory);
        string metadata = MetadataPath(root);
        CaptureManifest manifest;
        IEnumerable<string> files;
        if (File.Exists(metadata))
        {
            manifest = JsonSerializer.Deserialize<CaptureManifest>(File.ReadAllText(metadata), JsonOptions)
                ?? throw new InvalidDataException("Capture manifest is empty.");
            files = manifest.Devices.Where(d => d.OutputFile != null).Select(d => d.OutputFile!).Distinct();
        }
        else
        {
            // Legacy exports have no evidence of a successful current-run capture.
            manifest = new CaptureManifest { CompletedAtUtc = DateTimeOffset.UtcNow };
            manifest.UnverifiedFiles = Directory.GetFiles(root, "*.txt").Select(Path.GetFileName).OfType<string>().ToList();
            files = manifest.UnverifiedFiles;
        }
        string temporary = Path.GetFullPath(destinationZip) + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (string relative in files)
                {
                    string path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Capture file is outside the backup directory.");
                    archive.CreateEntryFromFile(path, relative.Replace('\\', '/'));
                }
                using var writer = new StreamWriter(archive.CreateEntry(ManifestName).Open());
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
            }
            File.Move(temporary, destinationZip, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
