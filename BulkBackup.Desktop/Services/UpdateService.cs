using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public string CurrentVersion { get; init; } = UpdateService.DefaultCurrentVersion;
    public string LatestVersion { get; init; } = UpdateService.DefaultCurrentVersion;
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

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

public class GitHubTagDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class UpdateService : IUpdateService
{
    public bool SupportsInstallation => OperatingSystem.IsWindows();

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    private static readonly HttpClient DownloadHttpClient = new()
    {
        Timeout = TimeSpan.FromHours(1)
    };

    public const string DefaultCurrentVersion = "3.0.6";
    public const string GitHubRepo = "LazyAutomatar/Bulk-Switch-Backup-Pro";

    static UpdateService()
    {
        var productHeader = new ProductInfoHeaderValue("BulkSwitchBackupPro", DefaultCurrentVersion);
        HttpClient.DefaultRequestHeaders.UserAgent.Add(productHeader);
        DownloadHttpClient.DefaultRequestHeaders.UserAgent.Add(productHeader);
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
        AppLogger.LogInfo("UpdateService", $"CheckForUpdatesAsync initiated. CurrentVersion: {currentVer}. Custom manifest: {customManifestUrlOrPath ?? "none"}");

        try
        {
            // Mode A: Custom manifest URL or network path provided
            if (!string.IsNullOrWhiteSpace(customManifestUrlOrPath))
            {
                AppLogger.LogInfo("UpdateService", $"Checking custom manifest: {customManifestUrlOrPath}");
                return await CheckCustomManifestAsync(customManifestUrlOrPath, currentVer, ct);
            }

            // Mode B: Query GitHub Releases API
            AppLogger.LogInfo("UpdateService", "Querying GitHub Releases API...");
            var ghResult = await CheckGitHubReleasesAsync(currentVer, ct);
            if (ghResult.ErrorMessage == null && (!ghResult.IsUpdateAvailable || !string.IsNullOrWhiteSpace(ghResult.DownloadUrl)))
            {
                AppLogger.LogInfo("UpdateService", $"GitHub Releases API returned result: IsUpdateAvailable={ghResult.IsUpdateAvailable}, LatestVersion={ghResult.LatestVersion}");
                return ghResult;
            }

            // Mode C: Fallback to repository update manifest if available (fast CDN, no rate limit)
            try
            {
                string rawManifestUrl = $"https://raw.githubusercontent.com/{GitHubRepo}/main/dist/update_manifest.json";
                AppLogger.LogInfo("UpdateService", $"Attempting CDN fallback manifest: {rawManifestUrl}");
                var manifestResult = await CheckCustomManifestAsync(rawManifestUrl, currentVer, ct);
                if (manifestResult.ErrorMessage == null)
                {
                    AppLogger.LogInfo("UpdateService", $"CDN fallback manifest succeeded: IsUpdateAvailable={manifestResult.IsUpdateAvailable}, LatestVersion={manifestResult.LatestVersion}");
                    return manifestResult;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("UpdateService", $"CDN fallback manifest check failed: {ex.Message}");
            }

            // Mode D: Fallback to GitHub Tags API if releases/latest is not published yet
            AppLogger.LogInfo("UpdateService", "Checking GitHub Tags API fallback...");
            var tagResult = await CheckGitHubTagsAsync(currentVer, ct);
            if (tagResult != null && tagResult.ErrorMessage == null)
            {
                AppLogger.LogInfo("UpdateService", $"GitHub Tags API fallback succeeded: LatestVersion={tagResult.LatestVersion}");
                return tagResult;
            }

            return ghResult;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("UpdateService", "CheckForUpdatesAsync failed with exception", ex);
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

            var sw = Stopwatch.StartNew();
            using var resp = await HttpClient.SendAsync(req, ct);
            sw.Stop();
            AppLogger.LogInfo("UpdateService", $"GET {url} responded with HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} in {sw.ElapsedMilliseconds}ms");

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // GitHub returns 404 when no releases are published yet on a repository.
                // Fall back to checking tags or report current version is up to date.
                AppLogger.LogInfo("UpdateService", "GitHub releases returned 404. Falling back to tags.");
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
                    AppLogger.LogWarning("UpdateService", "GitHub API rate limit reached (HTTP 403).");
                    return new UpdateInfo
                    {
                        CurrentVersion = currentVer,
                        LatestVersion = currentVer,
                        IsUpdateAvailable = false,
                        ErrorMessage = "GitHub API rate limit reached. Please wait a moment or configure a custom manifest URL."
                    };
                }

                AppLogger.LogWarning("UpdateService", $"GitHub API error HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}).");
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
                AppLogger.LogWarning("UpdateService", "GitHub release deserialization returned null or empty tag name.");
                return new UpdateInfo
                {
                    CurrentVersion = currentVer,
                    LatestVersion = currentVer,
                    IsUpdateAvailable = false,
                    ErrorMessage = null
                };
            }

            return DescribeRelease(release, currentVer);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("UpdateService", "GitHub update check failed with exception", ex);
            return new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = currentVer,
                IsUpdateAvailable = false,
                ErrorMessage = $"GitHub update check failed: {ex.Message}"
            };
        }
    }

    public static UpdateInfo DescribeRelease(GitHubReleaseDto release, string currentVersion)
    {
        string version = release.TagName.TrimStart('v', 'V');
        var installer = release.Assets.FirstOrDefault(asset => asset.Name.Equals(
            "BulkSwitchBackupPro_Setup.exe", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(asset => asset.Name.Equals(
                $"BulkSwitchBackupPro_Setup_v{version}.exe", StringComparison.OrdinalIgnoreCase));
        string? checksum = installer?.Digest;
        checksum = checksum?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? checksum[7..] : null;
        if (checksum != null && (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))) checksum = null;

        var info = new UpdateInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = version,
            IsUpdateAvailable = CompareVersions(version, currentVersion) > 0,
            Title = release.Name,
            PublishedAt = release.PublishedAt,
            ReleaseNotes = ChangelogService.NotesForVersion(version, release.Body),
            DownloadUrl = installer?.BrowserDownloadUrl,
            FileSizeBytes = installer?.Size ?? 0,
            Sha256 = checksum
        };

        AppLogger.LogInfo("UpdateService", $"DescribeRelease: Tag={release.TagName}, Version={version}, IsUpdateAvailable={info.IsUpdateAvailable}, Assets={release.Assets?.Length ?? 0}, Installer={installer?.Name ?? "none"}, Size={info.FileSizeBytes} bytes, Sha256={checksum ?? "none"}");
        return info;
    }

    private async Task<UpdateInfo?> CheckGitHubTagsAsync(string currentVer, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{GitHubRepo}/tags";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            TryAttachAuthHeader(req);

            var sw = Stopwatch.StartNew();
            using var resp = await HttpClient.SendAsync(req, ct);
            sw.Stop();
            AppLogger.LogInfo("UpdateService", $"GET {url} responded with HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds}ms");

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

            AppLogger.LogInfo("UpdateService", $"Latest tag from GitHub: {tags[0].Name} (Parsed: {latestTag}, IsNewer: {isNewer})");

            return new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion = latestTag,
                IsUpdateAvailable = isNewer,
                Title = $"Version {latestTag}",
                ReleaseNotes = isNewer ? $"New release tag v{latestTag} available on GitHub." : string.Empty,
                DownloadUrl = null, // A tag alone does not prove a package has been published.
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            AppLogger.LogError("UpdateService", "CheckGitHubTagsAsync failed with exception", ex);
            return null;
        }
    }

    private async Task<UpdateInfo> CheckCustomManifestAsync(string urlOrPath, string currentVer, CancellationToken ct)
    {
        try
        {
            string json;
            if (urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, urlOrPath);
                TryAttachAuthHeader(req);
                var sw = Stopwatch.StartNew();
                using var resp = await HttpClient.SendAsync(req, ct);
                sw.Stop();
                AppLogger.LogInfo("UpdateService", $"GET {urlOrPath} responded with HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds}ms");
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            else
            {
                AppLogger.LogInfo("UpdateService", $"Reading local/UNC manifest file: {urlOrPath}");
                json = await File.ReadAllTextAsync(urlOrPath, ct);
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifestDto>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException("Invalid update manifest format.");
            }

            string cleanLatest = manifest.Version.TrimStart('v', 'V');
            bool isNewer = CompareVersions(cleanLatest, currentVer) > 0;

            AppLogger.LogInfo("UpdateService", $"Manifest parsed: Version={cleanLatest}, CurrentVersion={currentVer}, IsNewer={isNewer}, DownloadUrl={manifest.DownloadUrl}, Size={manifest.FileSizeBytes} bytes, Sha256={manifest.Sha256 ?? "none"}");

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
        catch (Exception ex)
        {
            AppLogger.LogError("UpdateService", $"CheckCustomManifestAsync failed for '{urlOrPath}'", ex);
            throw;
        }
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
            var ex = new InvalidOperationException("No download URL is available for this update.");
            AppLogger.LogError("UpdateService", "Download requested with null or empty DownloadUrl", ex);
            throw ex;
        }

        string tempDir = Path.GetTempPath();
        string fileName = $"BulkSwitchBackupPro_v{update.LatestVersion}.exe";
        string targetPath = Path.Combine(tempDir, fileName);

        AppLogger.LogInfo("UpdateService", $"Download started. URL: {update.DownloadUrl}");
        AppLogger.LogInfo("UpdateService", $"Destination target: {targetPath}");
        AppLogger.LogInfo("UpdateService", $"Expected size: {update.FileSizeBytes} bytes ({update.FileSizeBytes / (1024.0 * 1024.0):F2} MB), Expected SHA256: {update.Sha256 ?? "none"}");

        try
        {
            var sw = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            TryAttachAuthHeader(request);
            using var response = await DownloadHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            AppLogger.LogInfo("UpdateService", $"Download HTTP response: {(int)response.StatusCode} {response.ReasonPhrase}. Headers: ContentLength={response.Content.Headers.ContentLength?.ToString() ?? "unknown"}, ContentType={response.Content.Headers.ContentType?.ToString() ?? "unknown"}");
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength ?? (update.FileSizeBytes > 0 ? update.FileSizeBytes : null);

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            int lastReportedPercent = -1;
            long lastLogBytes = 0;
            var logSw = Stopwatch.StartNew();

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    double ratio = (double)totalRead / totalBytes.Value;
                    progress?.Report(ratio);

                    int currentPercent = (int)(ratio * 100);
                    if (currentPercent >= lastReportedPercent + 10 || (logSw.ElapsedMilliseconds >= 5000 && totalRead - lastLogBytes >= 5 * 1024 * 1024))
                    {
                        double mbRead = totalRead / (1024.0 * 1024.0);
                        double totalMb = totalBytes.Value / (1024.0 * 1024.0);
                        double speedMbPerSec = logSw.Elapsed.TotalSeconds > 0 ? (totalRead - lastLogBytes) / (1024.0 * 1024.0 * logSw.Elapsed.TotalSeconds) : 0;
                        AppLogger.LogInfo("UpdateService", $"Download progress: {currentPercent}% ({mbRead:F2} MB of {totalMb:F2} MB) [{speedMbPerSec:F2} MB/s]");
                        lastReportedPercent = currentPercent;
                        lastLogBytes = totalRead;
                        logSw.Restart();
                    }
                }
            }

            // Flush and close stream before verification
            await fileStream.FlushAsync(ct);
            await fileStream.DisposeAsync();

            sw.Stop();
            double totalMbFinal = totalRead / (1024.0 * 1024.0);
            double avgSpeed = sw.Elapsed.TotalSeconds > 0 ? totalMbFinal / sw.Elapsed.TotalSeconds : 0;
            AppLogger.LogInfo("UpdateService", $"Download finished in {sw.Elapsed.TotalSeconds:F2}s ({totalMbFinal:F2} MB at {avgSpeed:F2} MB/s). File size on disk: {new FileInfo(targetPath).Length} bytes.");

            // Cryptographic Security Verification: Verify SHA256 checksum if provided
            if (!string.IsNullOrWhiteSpace(update.Sha256))
            {
                AppLogger.LogInfo("UpdateService", $"Computing SHA256 hash for {targetPath}...");
                using var sha = System.Security.Cryptography.SHA256.Create();
                await using var verifyStream = File.OpenRead(targetPath);
                byte[] hashBytes = await sha.ComputeHashAsync(verifyStream, ct);
                string computedHash = Convert.ToHexString(hashBytes);

                AppLogger.LogInfo("UpdateService", $"Computed SHA256: {computedHash}");
                AppLogger.LogInfo("UpdateService", $"Expected SHA256: {update.Sha256.Trim()}");

                if (!computedHash.Equals(update.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogError("UpdateService", $"Cryptographic Security Verification FAILED! Expected {update.Sha256.Trim()} but calculated {computedHash}. Deleting untrusted file.");
                    try { File.Delete(targetPath); } catch { }
                    throw new System.Security.SecurityException(
                        $"Cryptographic Security Verification Failed:\nDownloaded binary SHA256 ({computedHash}) did not match the expected release checksum ({update.Sha256}). The untrusted file has been deleted."
                    );
                }

                AppLogger.LogInfo("UpdateService", "Cryptographic Security Verification PASSED: SHA256 matched successfully.");
            }
            else
            {
                AppLogger.LogWarning("UpdateService", "No expected SHA256 checksum was provided in the update manifest/release. Skipping hash verification.");
            }

            progress?.Report(1.0);
            return targetPath;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("UpdateService", $"Download failed for {update.DownloadUrl}", ex);
            throw;
        }
    }

    /// <summary>
    /// Launches the downloaded installer or executable to perform an in-place upgrade and cleanly closes the current application.
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        AppLogger.LogInfo("UpdateService", $"LaunchInstallerAndExit invoked for: '{installerPath}'");

        if (!SupportsInstallation)
        {
            var ex = new PlatformNotSupportedException("Download the matching package from the releases page on macOS or Linux.");
            AppLogger.LogError("UpdateService", "Installation not supported on this platform", ex);
            throw ex;
        }

        if (!File.Exists(installerPath))
        {
            var ex = new FileNotFoundException("Installer executable was not found.", installerPath);
            AppLogger.LogError("UpdateService", "Installer file not found on disk", ex);
            throw ex;
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
            AppLogger.LogInfo("UpdateService", "PE executable header verified ('MZ').");
        }
        catch (System.Security.SecurityException ex)
        {
            AppLogger.LogError("UpdateService", "Executable binary integrity check failed. Deleting invalid installer file.", ex);
            try { File.Delete(installerPath); } catch { }
            throw;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/CLOSEAPPLICATIONS",
            UseShellExecute = true
        };

        AppLogger.LogInfo("UpdateService", $"Spawning installer process: '{startInfo.FileName}' with args '{startInfo.Arguments}'");
        var proc = Process.Start(startInfo);
        AppLogger.LogInfo("UpdateService", $"Installer process spawned (PID: {proc?.Id.ToString() ?? "unknown"}). Shutting down host application.");

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
