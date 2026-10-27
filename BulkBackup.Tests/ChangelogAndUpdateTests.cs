using BulkBackup.Desktop.Services;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Tests;

public class ChangelogAndUpdateTests
{
    [Fact]
    public void PackagedHistoryIncludesCurrentAndPreviousReleaseOffline()
    {
        var history = ChangelogService.LoadInstalledHistory();
        Assert.Equal("v" + UpdateService.DefaultCurrentVersion, history[0].VersionTag);
        Assert.Contains(history, entry => entry.Notes.Contains("29 CLI families"));
        Assert.Contains(history, entry => entry.VersionTag == "v2.5.4");
        Assert.DoesNotContain(history, entry => entry.VersionTag.Contains("Unreleased"));
    }

    [Fact]
    public void ParsesVersionSectionsWithoutLeakingOtherReleaseNotes()
    {
        const string text = "## [Unreleased]\r\nFuture\r\n## [3.1.0] - 2026-10-24\r\n### Added\r\n- New capability\r\n## [3.0.0] - 2026-10-23\r\nOlder changes\r\n";
        var entries = ChangelogService.Parse(text);
        Assert.Equal(2, entries.Count);
        Assert.Equal("2026-10-24", entries[0].ReleaseDate);
        Assert.Equal("### Added\n- New capability", entries[0].Notes);
        Assert.Equal(entries[0].Notes, ChangelogService.NotesForVersion("v3.1.0", text));
    }

    [Fact]
    public void EmptyNotesHaveAnExplicitFallback()
        => Assert.Contains("not provided", ChangelogService.NotesForVersion("3.1.0", " "));

    [Fact]
    public void IncomingReleaseReplacesDuplicatesAndRetainsOfflineHistory()
    {
        var vm = new AboutViewModel(new FakeUpdater());
        int before = vm.ChangelogHistory.Count;
        string futureVersion = new Version(Version.Parse(UpdateService.DefaultCurrentVersion).Major + 1, 0, 0).ToString();
        vm.SyncIncomingVersionToChangelog(futureVersion, "First notes");
        vm.SyncIncomingVersionToChangelog("v" + futureVersion, "Corrected notes");
        Assert.Equal(before + 1, vm.ChangelogHistory.Count);
        Assert.Equal("v" + futureVersion, vm.ChangelogHistory[0].VersionTag);
        Assert.Equal("Corrected notes", vm.ChangelogHistory[0].Notes);
        Assert.Contains(vm.ChangelogHistory, entry => entry.VersionTag == "v3.0.0");
    }

    [Fact]
    public async Task DownloadRequiresReviewAndCancelDoesNotInstall()
    {
        var updater = new FakeUpdater();
        var vm = new AboutViewModel(updater);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal("New features", vm.ReleaseNotes);
        Assert.True(vm.CanReviewUpdate);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, updater.Downloads);
        vm.ReviewUpdateCommand.Execute(null);
        Assert.True(vm.IsReviewingUpdate);
        vm.CancelUpdateReviewCommand.Execute(null);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, updater.Downloads);
        vm.ReviewUpdateCommand.Execute(null);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, updater.Downloads);
        Assert.Equal(1, updater.Launches);
        Assert.False(vm.IsReviewingUpdate);
    }

    [Fact]
    public async Task FailedRecheckClearsPreviousUpdateAndReview()
    {
        var updater = new FakeUpdater();
        var vm = new AboutViewModel(updater);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.ReviewUpdateCommand.Execute(null);
        updater.Result = new UpdateInfo { ErrorMessage = "Offline" };
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.False(vm.IsReviewingUpdate);
        Assert.False(vm.IsUpdateAvailable);
        Assert.False(vm.CanReviewUpdate);
        Assert.Empty(vm.ReleaseNotes);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, updater.Downloads);
    }

    [Theory]
    [InlineData(false, "https://example.invalid/setup.exe")]
    [InlineData(true, null)]
    public async Task UnsupportedPlatformOrMissingPackageCannotLaunchInstaller(bool supportsInstall, string? url)
    {
        var updater = new FakeUpdater { SupportsInstallation = supportsInstall };
        updater.Result = updater.Result with { DownloadUrl = url };
        var vm = new AboutViewModel(updater);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.ReviewUpdateCommand.Execute(null);
        Assert.False(vm.CanReviewUpdate);
        Assert.False(vm.IsReviewingUpdate);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, updater.Downloads);
    }

    [Fact]
    public async Task DownloadFailureRequiresANewReviewBeforeRetry()
    {
        var updater = new FakeUpdater { FailDownload = true };
        var vm = new AboutViewModel(updater);
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        vm.ReviewUpdateCommand.Execute(null);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.False(vm.IsDownloadingUpdate);
        Assert.False(vm.IsReviewingUpdate);
        Assert.Contains("Download failed", vm.UpdateStatusText);
        Assert.Equal(0, updater.Launches);
        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, updater.Downloads);
    }

    [Fact]
    public void ReleaseSelectsSetupInsteadOfPortableAndRetainsChecksumAndNotes()
    {
        string hash = new('a', 64);
        var release = new GitHubReleaseDto
        {
            TagName = "v3.1.0", Body = "## [3.1.0] - 2026-10-24\nNew changes\n## [3.0.0]\nOld changes",
            Assets = [
                new() { Name = "BulkSwitchBackupPro.exe", BrowserDownloadUrl = "portable" },
                new() { Name = "BulkSwitchBackupPro_Setup.exe", BrowserDownloadUrl = "installer", Digest = "sha256:" + hash, Size = 123 }
            ]
        };
        var update = UpdateService.DescribeRelease(release, "3.0.0");
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("installer", update.DownloadUrl);
        Assert.Equal(hash, update.Sha256);
        Assert.Equal(123, update.FileSizeBytes);
        Assert.Equal("New changes", update.ReleaseNotes);
    }

    [Fact]
    public void PortableOnlyReleaseDoesNotPretendToOfferAnInstaller()
    {
        var update = UpdateService.DescribeRelease(new GitHubReleaseDto
        {
            TagName = "v3.1.0", Assets = [new() { Name = "BulkSwitchBackupPro.exe", BrowserDownloadUrl = "portable" }]
        }, "3.0.0");
        Assert.Null(update.DownloadUrl);
    }

    internal sealed class FakeUpdater : IUpdateService
    {
        public bool SupportsInstallation { get; set; } = true;
        public bool FailDownload { get; set; }
        public int Downloads { get; private set; }
        public int Launches { get; private set; }
        public UpdateInfo Result { get; set; } = new()
        {
            CurrentVersion = "3.0.0", LatestVersion = "3.1.0", IsUpdateAvailable = true,
            ReleaseNotes = "New features", DownloadUrl = "https://example.invalid/setup.exe"
        };
        public Task<UpdateInfo> CheckForUpdatesAsync(string? customManifestUrlOrPath = null, CancellationToken ct = default)
            => Task.FromResult(Result);
        public Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Downloads++;
            if (FailDownload) throw new IOException("Simulated failure");
            return Task.FromResult("simulated-installer.exe");
        }
        public void LaunchInstallerAndExit(string installerPath) => Launches++;
    }
}
