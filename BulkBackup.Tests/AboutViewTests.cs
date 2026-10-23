using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Styling;
using BulkBackup.Desktop.ViewModels;
using BulkBackup.Desktop.Views;

[assembly: AvaloniaTestApplication(typeof(BulkBackup.Tests.UiTestApplication))]

namespace BulkBackup.Tests;

public class UiTestApplication : BulkBackup.Desktop.App
{
    // Load production styles without starting services or touching temporary backup sessions.
    public override void OnFrameworkInitializationCompleted() { }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<UiTestApplication>()
        .UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class AboutViewTests
{
    [AvaloniaFact]
    public async Task ChangelogAndReviewBindingsWorkBeforeAnyDownload()
    {
        var updater = new ChangelogAndUpdateTests.FakeUpdater();
        var vm = new AboutViewModel(updater);
        var view = new AboutView(vm);
        var window = new Window { Content = view, Width = 1100, Height = 900 };
        try
        {
            window.Show();
            var expander = view.FindControl<Expander>("ChangelogExpander")!;
            Assert.False(expander.IsExpanded);
            expander.IsExpanded = true;
            Assert.NotEmpty(vm.ChangelogHistory);
            Assert.Equal("v" + BulkBackup.Desktop.Services.UpdateService.DefaultCurrentVersion, vm.ChangelogHistory[0].VersionTag);
            await vm.CheckForUpdatesCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("New features", view.FindControl<SelectableTextBlock>("IncomingReleaseNotes")!.Text);
            var review = view.FindControl<Border>("UpdateReviewPanel")!;
            Assert.False(review.IsVisible);
            vm.ReviewUpdateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(review.IsVisible);
            Assert.Equal(0, updater.Downloads);
            string? snapshots = Environment.GetEnvironmentVariable("BULK_BACKUP_UI_SNAPSHOTS");
            if (!string.IsNullOrWhiteSpace(snapshots))
            {
                Directory.CreateDirectory(snapshots);
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = theme;
                    review.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(snapshots, $"update-review-{theme}.png"));
                    expander.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    using var historyFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(historyFrame);
                    historyFrame.Save(Path.Combine(snapshots, $"changelog-{theme}.png"));
                }
            }
            vm.CancelUpdateReviewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(review.IsVisible);
        }
        finally { window.Close(); }
    }
}
