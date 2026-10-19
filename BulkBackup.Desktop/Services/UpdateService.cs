using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace BulkBackup.Desktop.Services;

public record UpdateInfo
{
    public string CurrentVersion { get; init; } = "2.5.0";
    public string LatestVersion { get; init; } = "2.5.0";
    public bool IsUpdateAvailable { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string? DownloadUrl { get; init; }
    public DateTime? PublishedAt { get; init; }
    public long FileSizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public string? ErrorMessage { get; init; }
}

public class UpdateManifestDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("publishedAt")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }
}

public class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAssetDto[] Assets { get; set; } = Array.Empty<GitHubAssetDto>();
}

public class GitHubAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class UpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public const string DefaultCurrentVersion = "2.5.0";
    public const string GitHubRepo = "Majordean0303/Bulk-Switch-Backup";

    static UpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BulkSwitchBackupPro", DefaultCurrentVersion));
    }

    /// <summary>
    /// Checks for updates. Supports:
    /// 1. Public GitHub Releases (if repo is public)
    /// 2. Custom Manifest URL (e.g. AWS S3, Cloudflare, raw JSON gist, company internal web server)
    /// 3. Local/UNC network file path (for air-gapped enterprise environments)
    /// </summary>
    public async Task<UpdateInfo> CheckForUpdatesAsync(string? customManifestUrlOrPath = null, CancellationToken ct = default)
    {
        string currentVer = DefaultCurrentVersion;

        try
        {
            // Mode A: Custom manifest URL or network path provided
            if (!string.IsNullOrWhiteSpace(customManifestUrlOrPath))
            {
                return await CheckCustomManifestAsync(customManifestUrlOrPath, currentVer, ct);
            }

            // Mode B: Try GitHub Releases API first
            var ghResult = await CheckGitHubReleasesAsync(currentVer, ct);
            if (ghResult.ErrorMessage == null)
            {
                return ghResult;
            }

            // Fallback: If GitHub API fails (rate-limited / repo private), check raw manifest
            try
            {
                string rawManifestUrl = $"https://raw.githubusercontent.com/{GitHubRepo}/main/dist/update_manifest.json";
                return await CheckCustomManifestAsync(rawManifestUrl, currentVer, ct);
            }
            catch
            {
                return ghResult;
            }
        }
        catch (Exception ex)
        {
            return new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = currentVer,
                IsUpdateAvailable = false,
                ErrorMessage = $"Update check failed: {ex.Message}"
            };
        }
    }

    private async Task<UpdateInfo> CheckGitHubReleasesAsync(string currentVer, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var resp = await HttpClient.SendAsync(req, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = "Update check: Repository is currently private or no public releases are published yet."
                };
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = $"Update check returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})."
                };
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false
                };
            }

            string cleanLatest = release.TagName.TrimStart('v', 'V');
            bool isNewer = CompareVersions(cleanLatest, currentVer) > 0;

            // Find executable asset (.exe)
            string? downloadUrl = null;
            long fileSize = 0;
            if (release.Assets != null)
            {
                foreach (var asset in release.Assets)
                {
                    if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        fileSize = asset.Size;
                        break;
                    }
                }
            }

            return new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = cleanLatest,
                IsUpdateAvailable = isNewer,
                Title = release.Name ?? $"Version {cleanLatest}",
                ReleaseNotes = release.Body ?? string.Empty,
                DownloadUrl = downloadUrl,
                PublishedAt = release.PublishedAt,
                FileSizeBytes = fileSize
            };
        }
        catch (Exception ex)
        {
            return new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = currentVer,
                IsUpdateAvailable = false,
                ErrorMessage = $"GitHub update check failed: {ex.Message}"
            };
        }
    }

    private async Task<UpdateInfo> CheckCustomManifestAsync(string urlOrPath, string currentVer, CancellationToken ct)
    {
        string json;
        if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            json = await HttpClient.GetStringAsync(urlOrPath, ct);
        }
        else
        {
            // Local file or UNC network share
            json = await File.ReadAllTextAsync(urlOrPath, ct);
        }

        var manifest = JsonSerializer.Deserialize<UpdateManifestDto>(json);
        if (manifest == null)
        {
            throw new InvalidOperationException("Invalid update manifest format.");
        }

        string cleanLatest = manifest.Version.TrimStart('v', 'V');
        bool isNewer = CompareVersions(cleanLatest, currentVer) > 0;

        return new UpdateInfo
        {
            CurrentVersion = currentVer,
            LatestVersion = cleanLatest,
            IsUpdateAvailable = isNewer,
            Title = manifest.Title,
            ReleaseNotes = manifest.ReleaseNotes,
            DownloadUrl = manifest.DownloadUrl,
            PublishedAt = manifest.PublishedAt,
            FileSizeBytes = manifest.FileSizeBytes,
            Sha256 = manifest.Sha256
        };
    }

    /// <summary>
    /// Downloads the update package to the temporary folder with progress reporting.
    /// </summary>
    public async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            throw new InvalidOperationException("No download URL is available for this update.");
        }

        string tempDir = Path.GetTempPath();
        string fileName = $"BulkSwitchBackupPro_v{update.LatestVersion}.exe";
        string targetPath = Path.Combine(tempDir, fileName);

        using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength ?? (update.FileSizeBytes > 0 ? update.FileSizeBytes : null);

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                progress?.Report((double)totalRead / totalBytes.Value);
            }
        }

        progress?.Report(1.0);
        return targetPath;
    }

    /// <summary>
    /// Launches the downloaded installer or executable to perform an in-place upgrade and cleanly closes the current application.
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer executable was not found.", installerPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/CLOSEAPPLICATIONS",
            UseShellExecute = true
        };

        Process.Start(startInfo);

        // Shut down current Avalonia desktop application cleanly
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private static int CompareVersions(string v1, string v2)
    {
        string clean1 = v1.Split(' ')[0].Trim();
        string clean2 = v2.Split(' ')[0].Trim();

        if (Version.TryParse(clean1, out var parsed1) && Version.TryParse(clean2, out var parsed2))
        {
            return parsed1.CompareTo(parsed2);
        }

        return string.Compare(clean1, clean2, StringComparison.OrdinalIgnoreCase);
    }
}
