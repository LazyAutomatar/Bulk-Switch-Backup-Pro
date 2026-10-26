using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using BulkBackup.Desktop.Services;
using BulkBackup.Desktop.ViewModels;
using BulkBackup.Desktop.Views;

namespace BulkBackup.Tests;

public class WindowZoomTests
{
    [AvaloniaFact]
    public void ThemeToggleMatchesActualLightStartupAndBothToggleDirections()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        try
        {
            app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            var state = new AppState();
            var vm = new MainWindowViewModel(state);
            Assert.Equal("Light (System)", vm.ThemeButtonText);
            vm.ToggleThemeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, app.ActualThemeVariant);
            Assert.Equal("Dark Mode", vm.ThemeButtonText);
            vm.ToggleThemeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.ActualThemeVariant);
            Assert.Equal("Light Mode", vm.ThemeButtonText);
        }
        finally { app.RequestedThemeVariant = original; Dispatcher.UIThread.RunJobs(); }
    }

    [Theory]
    [InlineData(1366, 728, 1.5)]
    [InlineData(1920, 1040, 2.0)]
    [InlineData(1024, 728, 1.0)]
    public void StartupFitsHighDpiWorkingArea(double width, double height, double density)
    {
        var size = MainWindow.FitStartupSize(new Size(width, height), density);
        Assert.True(size.Width < width / density);
        Assert.True(size.Height < height / density);
        Assert.True(size.Width <= 1380 && size.Height <= 820);
    }

    [AvaloniaFact]
    public void ZoomButtonsDoNotMaximizeOrResizeWindow()
    {
        var state = new AppState();
        var vm = new MainWindowViewModel(state);
        var window = new MainWindow(new FakePages(), vm, new NavigationService(), state) { Width = 900, Height = 620 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var original = window.ClientSize;
            var position = window.Position;
            var button = window.FindControl<Button>("ZoomInButton")!;
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            for (int i = 0; i < 2; i++)
            {
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(state.CurrentUiScale > 1);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(original, window.ClientSize);
            Assert.Equal(position, window.Position);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SmallWindowKeepsHeaderVisibleAtMaximumZoomAndScrollsOnlyBody()
    {
        var state = new AppState();
        for (int i = 0; i < 6; i++) state.StepUiScale(true);
        var window = new MainWindow(new FakePages(), new MainWindowViewModel(state), new NavigationService(), state)
            { Width = 640, Height = 420 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var header = window.FindControl<Grid>("WindowHeader")!;
            var viewport = window.FindControl<ScrollViewer>("ContentViewport")!;
            var button = window.FindControl<Button>("ZoomOutButton")!;
            var point = button.TranslatePoint(default, window)!.Value;
            Assert.True(point.X >= 0 && point.X + button.Bounds.Width <= window.ClientSize.Width);
            Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= 38);
            Assert.True(viewport.Extent.Width > viewport.Viewport.Width);
            Assert.True(viewport.Extent.Height > viewport.Viewport.Height);
            var navigation = window.FindControl<FluentAvalonia.UI.Controls.NavigationView>("RootNavigation")!;
            Assert.True(double.IsFinite(navigation.Width) && double.IsFinite(navigation.Height));
            var originalHeader = header.Bounds;
            viewport.Offset = new Vector(200, 200);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(originalHeader, header.Bounds);
            Assert.Equal(point, button.TranslatePoint(default, window)!.Value);
            Assert.Equal(640, window.ClientSize.Width);
            string? output = Environment.GetEnvironmentVariable("BULK_BACKUP_UI_SNAPSHOTS");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(output, "small-screen-130-percent.png"));
            }
        }
        finally { window.Close(); }
    }

    private sealed class FakePages : IServiceProvider
    {
        public object? GetService(Type serviceType) => new Border
        {
            MinWidth = 1000, MinHeight = 600,
            Child = new TextBlock { Text = "Scrollable application content at 130% zoom", FontSize = 30 }
        };
    }
}
