using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;
using BulkBackup.Desktop.Controls;
using BulkBackup.Desktop.Services;
using BulkBackup.Desktop.ViewModels;
using BulkBackup.Desktop.Views;

namespace BulkBackup.Tests;

#pragma warning disable CA1416 // Empty isolated storage; these tests never invoke DPAPI or real credentials.
public class RunMonitorAndProfilesTests
{
    [AvaloniaFact]
    public void SavedCredentialsPageOpens()
    {
        using var fixture = new UiFixture();
        var window = new Window { Content = new CredentialsView(fixture.ViewModel), Width = 1100, Height = 800 };
        try { window.Show(); Dispatcher.UIThread.RunJobs(); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SavedCredentialsCanBeCreatedAppliedLockedAndUnlockedWhilePageIsOpen()
    {
        using var fixture = new UiFixture();
        var vm = fixture.ViewModel;
        var window = new Window { Content = new CredentialsView(vm), Width = 1100, Height = 800 };
        try
        {
            window.Show();
            vm.VaultPassword = vm.VaultConfirmation = "test master password 123";
            await vm.UnlockVaultCommand.ExecuteAsync(null);
            Assert.True(vm.IsVaultUnlocked);
            vm.NewCredProfileName = "Test switch"; vm.NewCredUsername = "operator";
            vm.NewCredPassword = "test-device-password"; vm.NewCredSecret = "test-enable-secret";
            await vm.SaveNewCredentialCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var entry = Assert.Single(vm.StoredCredentials);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "test-enable-secret");
            var apply = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Apply to Session"));
            Assert.NotNull(apply.Command);
            apply.Command.Execute(apply.CommandParameter);
            Assert.Equal("test-device-password", vm.ActivePassword);
            vm.LockVaultCommand.Execute(null);
            Assert.Empty(vm.ActivePassword); Assert.Empty(vm.StoredCredentials);
            vm.VaultPassword = "wrong password";
            await vm.UnlockVaultCommand.ExecuteAsync(null);
            Assert.False(vm.IsVaultUnlocked);
            Assert.Contains("Wrong master password", vm.VaultStatus);
            vm.VaultPassword = "test master password 123";
            await vm.UnlockVaultCommand.ExecuteAsync(null);
            Assert.Single(vm.StoredCredentials);
            vm.DeleteStoredCredentialCommand.Execute(vm.StoredCredentials[0]);
            Assert.Empty(vm.StoredCredentials);
            Dispatcher.UIThread.RunJobs();
        }
        finally { vm.LockVaultCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public void AutoReferenceMatchesEveryRuntimePlatformAndPreservesCustomDraft()
    {
        using var fixture = new UiFixture();
        var vm = fixture.ViewModel;
        Assert.Equal(5, vm.SelectedProfileIndex);
        Assert.Equal(6, vm.ProfileNames.Count);
        Assert.Contains("Dynamic", vm.CommandSummary);
        Assert.Equal(DevicePlatforms.All.Length, vm.SupportedPlatforms.Count);
        foreach (var platform in DevicePlatforms.All)
        {
            vm.SelectedPreviewPlatform = platform;
            Assert.Equal(platform.Commands.Where(c => c != platform.Paging && !NetworkEngine.IsConfigurationPushCommand(c)).Distinct(), vm.PreviewCommands);
            Assert.Contains(platform.Configuration, vm.PreviewCommandsText);
            Assert.Contains(platform.Paging, vm.PreviewPaging);
        }
        vm.SelectedProfileIndex = 4;
        vm.CustomCommandsText = "show version\nshow version\nshow running-config";
        vm.SelectedProfileIndex = 5;
        vm.SelectedProfileIndex = 4;
        Assert.Equal("show version\nshow version\nshow running-config", vm.CustomCommandsText);
        Assert.Equal("3 commands", vm.CommandSummary);
    }

    [AvaloniaFact]
    public void CommandReferenceShowsAutoPlatformsAndOnlyCustomModeIsEditable()
    {
        using var fixture = new UiFixture();
        var vm = fixture.ViewModel;
        var view = new CommandProfilesView(vm);
        var window = new Window { Width = 1260, Height = 850, Content = view };
        try
        {
            window.Show();
            var platforms = view.FindControl<ListBox>("PlatformList")!;
            Assert.Equal(29, platforms.ItemCount);
            vm.SelectedPreviewPlatform = DevicePlatforms.Find("junos")!;
            Dispatcher.UIThread.RunJobs();
            var preview = view.FindControl<TextBox>("PlatformCommands")!;
            Assert.True(preview.IsReadOnly);
            Assert.Contains("show configuration | no-more", preview.Text);
            var browser = view.FindControl<Grid>("AutoCommandBrowser")!;
            Assert.True(browser.IsVisible);
            Assert.True(platforms.Bounds.Width >= 300);
            Capture(window, "command-reference");
            vm.SelectedProfileIndex = 0;
            var editor = view.FindControl<TextBox>("CustomCommandsEditor")!;
            Dispatcher.UIThread.RunJobs();
            Assert.False(browser.IsVisible);
            Assert.True(editor.IsReadOnly);
            vm.SelectedProfileIndex = 4;
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.IsReadOnly);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MonitorHasAutoSelectedAndProgressDoesNotOverlapItsPercentage()
    {
        using var fixture = new UiFixture();
        var vm = fixture.ViewModel;
        vm.Devices.Add(new DeviceModel
        {
            IpAddress = "192.0.2.15", Hostname = "ACCESS-SWITCH-LONG-NAME", DeviceType = "Switch",
            UplinkDevice = "DISTRIBUTION-SWITCH", Port = "GigabitEthernet1/0/48 → TenGigabitEthernet1/1/1",
            Progress = 100, Status = "Completed with warnings (21 accepted, 2 rejected commands)",
            IsCompleted = true, Protocol = "SSH", ElapsedTime = "00:17"
        });
        vm.Devices.Add(new DeviceModel
        {
            IpAddress = "192.0.2.16", Hostname = "SERVER-SWITCH", Progress = 0,
            Status = "Failed: No usable configuration backup. Required configuration command was rejected.",
            IsFailed = true, Protocol = "ERR", ElapsedTime = "00:08"
        });
        vm.Devices.Add(new DeviceModel
        {
            IpAddress = "192.0.2.17", Hostname = "CORE-SWITCH", Progress = 57,
            Status = "Capturing show running-config", IsInProgress = true, Protocol = "SSH", ElapsedTime = "00:21"
        });
        var view = new RunMonitorView(vm);
        var window = new Window { Width = 1640, Height = 920, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var selector = view.FindControl<ProfileComboBox>("RunProfileSelector")!;
            Assert.Equal(5, selector.SelectedIndex);
            Assert.Equal(vm.ProfileNames[5], selector.SelectedItem);
            var popup = selector.GetVisualDescendants().OfType<Popup>().First();
            var grid = view.FindControl<DataGrid>("DeviceResults")!;
            Assert.Equal(2, grid.FrozenColumnCount);
            var bar = view.GetVisualDescendants().OfType<ProgressBar>().First(control => control.Name == "DeviceProgressBar");
            var label = view.GetVisualDescendants().OfType<TextBlock>().First(control => control.Name == "DeviceProgressLabel");
            Assert.True(bar.Bounds.Width >= 80);
            Assert.True(label.Bounds.Width >= 35);
            Assert.True(bar.Bounds.Right < label.Bounds.Left, $"Bar: {bar.Bounds}; percentage: {label.Bounds}");
            Capture(window, "run-monitor");
            selector.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.MinWidth >= 520);
            Assert.True(popup.Child!.Bounds.Width >= 500);
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
            Assert.Equal(0, popup.HorizontalOffset);
            selector.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
            var horizontal = grid.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Horizontal && b.Maximum > 0);
            double initialOffset = horizontal.Value;
            var wheelPoint = horizontal.TranslatePoint(new Point(horizontal.Bounds.Width / 2, horizontal.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(wheelPoint, new Vector(0, -1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(horizontal.Value > initialOffset, "Wheel over the horizontal scrollbar must move the table.");
            double afterBar = horizontal.Value;
            var cellPoint = grid.TranslatePoint(new Point(500, 70), window)!.Value;
            window.MouseWheel(cellPoint, new Vector(0, -1), RawInputModifiers.Shift);
            Dispatcher.UIThread.RunJobs();
            Assert.True(horizontal.Value > afterBar, "Shift+wheel must scroll the table horizontally.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FailedCommandViewFiltersDevicesAndShowsRawRejection()
    {
        using var fixture = new UiFixture();
        var vm = fixture.ViewModel;
        vm.RecordCommandFailure(new(DateTimeOffset.Now, "192.0.2.1", "ACCESS", "show running-config", "Rejected", "% Authorization failed"));
        vm.RecordCommandFailure(new(DateTimeOffset.Now, "192.0.2.2", "CORE", "show vlan", "Rejected", "% Invalid input"));
        var view = new CommandResultsView(vm);
        var window = new Window { Width = 1200, Height = 800, Content = view };
        try
        {
            window.Show();
            Assert.Equal(2, vm.CommandFailureCount);
            vm.CommandFailureFilter = "192.0.2.2";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(vm.VisibleCommandFailures);
            Assert.Equal("% Invalid input", view.FindControl<TextBox>("FailedCommandResponse")!.Text);
            Capture(window, "command-results");
            vm.CommandFailureFilter = "running-config";
            Assert.Equal("192.0.2.1", Assert.Single(vm.VisibleCommandFailures).IpAddress);
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("BULK_BACKUP_UI_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, $"{name}-{theme}.png"));
        }
    }

    private sealed class UiFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "bulk-backup-ui-" + Guid.NewGuid());
        public MainViewModel ViewModel { get; }
        public UiFixture() => ViewModel = new MainViewModel(new CredentialStore(_directory), new NoDialogs(), new AppState());
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task<string?> ShowOpenFolderDialogAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> ShowOpenFileDialogAsync(string title, string filterName, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> ShowOpenFilesDialogAsync(string title, string filterName, params string[] extensions) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string extension, string filterName) => Task.FromResult<string?>(null);
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(false);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
