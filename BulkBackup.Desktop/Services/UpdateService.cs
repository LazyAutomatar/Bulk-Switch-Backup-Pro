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

public class GitHubTagDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class UpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public const string DefaultCurrentVersion = "2.5.1";
    public const string GitHubRepo = "LazyAutomatar/Bulk-Switch-Backup-Pro";

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

            // Mode B: Query GitHub Releases API
            var ghResult = await CheckGitHubReleasesAsync(currentVer, ct);
            if (ghResult.ErrorMessage == null)
            {
                return ghResult;
            }

            // Mode C: Fallback to GitHub Tags API if releases/latest is not published yet
            var tagResult = await CheckGitHubTagsAsync(currentVer, ct);
            if (tagResult != null && tagResult.ErrorMessage == null)
            {
                return tagResult;
            }

            // Mode D: Fallback to repository update manifest if available
            try
            {
                string rawManifestUrl = $"https://raw.githubusercontent.com/{GitHubRepo}/main/dist/update_manifest.json";
                return await CheckCustomManifestAsync(rawManifestUrl, currentVer, ct);
            }
            catch
            {
                // When no remote releases or manifests exist yet, the running build is the latest
                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = null
                };
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
            TryAttachAuthHeader(req);

            using var resp = await HttpClient.SendAsync(req, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // GitHub returns 404 when no releases are published yet on a repository.
                // Fall back to checking tags or report current version is up to date.
                var tagFallback = await CheckGitHubTagsAsync(currentVer, ct);
                if (tagFallback != null)
                {
                    return tagFallback;
                }

                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = null
                };
            }

            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode == 403)
                {
                    return new UpdateInfo
                    {
                        CurrentVersion = currentVer,
                        LatestVersion = currentVer,
                        IsUpdateAvailable = false,
                        ErrorMessage = "GitHub API rate limit reached. Please wait a moment or configure a custom manifest URL."
                    };
                }

                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = $"GitHub returned HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})."
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
                    IsUpdateAvailable = false,
                    ErrorMessage = null
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
                FileSizeBytes = fileSize,
                ErrorMessage = null
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

    private async Task<UpdateInfo?> CheckGitHubTagsAsync(string currentVer, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{GitHubRepo}/tags";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            TryAttachAuthHeader(req);

            using var resp = await HttpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync(ct);
            var tags = JsonSerializer.Deserialize<GitHubTagDto[]>(json);
            if (tags == null || tags.Length == 0)
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = null
                };
            }

            string latestTag = tags[0].Name.TrimStart('v', 'V');
            bool isNewer = CompareVersions(latestTag, currentVer) > 0;

            return new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = latestTag,
                IsUpdateAvailable = isNewer,
                Title = $"Version {latestTag}",
                ReleaseNotes = isNewer ? $"New release tag v{latestTag} available on GitHub." : string.Empty,
                DownloadUrl = isNewer ? $"https://github.com/{GitHubRepo}/releases/download/v{latestTag}/BulkSwitchBackupPro.exe" : null,
                ErrorMessage = null
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<UpdateInfo> CheckCustomManifestAsync(string urlOrPath, string currentVer, CancellationToken ct)
    {
        string json;
        if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, urlOrPath);
            TryAttachAuthHeader(req);
            using var resp = await HttpClient.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            json = await resp.Content.ReadAsStringAsync(ct);
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

    private static void TryAttachAuthHeader(HttpRequestMessage req)
    {
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
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

        using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
        TryAttachAuthHeader(request);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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

        // Flush and close stream before verification
        await fileStream.FlushAsync(ct);
        await fileStream.DisposeAsync();

        // Cryptographic Security Verification: Verify SHA256 checksum if provided
        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            await using var verifyStream = File.OpenRead(targetPath);
            byte[] hashBytes = await sha.ComputeHashAsync(verifyStream, ct);
            string computedHash = Convert.ToHexString(hashBytes);

            if (!computedHash.Equals(update.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(targetPath); } catch { }
                throw new System.Security.SecurityException(
                    $"Cryptographic Security Verification Failed:\nDownloaded binary SHA256 ({computedHash}) did not match the expected release checksum ({update.Sha256}). The untrusted file has been deleted."
                );
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

        // Verify file is a valid Win32/PE executable before launching
        try
        {
            using var fs = File.OpenRead(installerPath);
            var header = new byte[2];
            if (fs.Read(header, 0, 2) < 2 || header[0] != 0x4D || header[1] != 0x5A) // 'MZ' signature
            {
                throw new System.Security.SecurityException("Downloaded file failed binary integrity verification (invalid executable PE header).");
            }
        }
        catch (System.Security.SecurityException)
        {
            try { File.Delete(installerPath); } catch { }
            throw;
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
