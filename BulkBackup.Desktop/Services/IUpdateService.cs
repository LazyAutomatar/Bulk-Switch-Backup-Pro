using System;
using System.Threading;
using System.Threading.Tasks;

namespace BulkBackup.Desktop.Services;

public interface IUpdateService
{
    bool SupportsInstallation { get; }
    Task<UpdateInfo> CheckForUpdatesAsync(string? customManifestUrlOrPath = null, CancellationToken ct = default);
    Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default);
    void LaunchInstallerAndExit(string installerPath);
}
