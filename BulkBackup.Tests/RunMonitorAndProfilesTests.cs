using Avalonia.Controls;
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
            selector.IsDropDownOpen = false;
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
        public void Dispose() => Directory.Delete(_directory, true);
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
