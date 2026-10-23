using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using BulkBackup.Desktop.Services;
using BulkBackup.Desktop.ViewModels;

namespace BulkBackup.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INavigationService _navigationService;
    private readonly AppState? _appState;
    private readonly Dictionary<string, Type> _pageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Run", typeof(RunMonitorView) },
        { "Credentials", typeof(CredentialsView) },
        { "Profiles", typeof(CommandProfilesView) },
        { "Results", typeof(CommandResultsView) },
        { "About", typeof(AboutView) }
    };

    public MainWindow()
    {
        InitializeComponent();
        _serviceProvider = null!;
        _navigationService = null!;
        UpdateTitleBarMetrics();
    }

    public MainWindow(IServiceProvider serviceProvider, MainWindowViewModel viewModel, INavigationService navigationService, AppState appState)
    {
        InitializeComponent();
        DataContext = viewModel;
        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
        _appState = appState;

        _navigationService.Navigated += OnNavigated;
        RootNavigation.SelectionChanged += OnNavigationSelectionChanged;

        UpdateTitleBarMetrics();

        // High-DPI auto detection & scaling subscription
        ScalingChanged += (s, e) =>
        {
            _appState.SetDetectedRenderScaling(RenderScaling);
        };
        Loaded += (s, e) =>
        {
            _appState.SetDetectedRenderScaling(RenderScaling);
        };

        // Keyboard zoom shortcuts (Ctrl +, Ctrl -, Ctrl 0)
        KeyDown += (s, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.OemPlus || e.Key == Key.Add)
                {
                    viewModel.ZoomInCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                {
                    viewModel.ZoomOutCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.D0 || e.Key == Key.NumPad0)
                {
                    viewModel.ResetZoomCommand.Execute(null);
                    e.Handled = true;
                }
            }
        };

        // Responsive NavigationView Auto-Collapse
        _appState.UiScaleChanged += _ => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateResponsiveLayout);

        // Navigate to Run & Monitor initially
        NavigateToType(typeof(RunMonitorView));
    }

    private void OnNavigationSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem item && item.Tag is string tag && _pageMap.TryGetValue(tag, out var viewType))
        {
            NavigateToType(viewType);
        }
    }

    private void OnNavigated(Type pageType)
    {
        NavigateToType(pageType);
    }

    private void NavigateToType(Type pageType)
    {
        if (_serviceProvider == null) return;
        var view = _serviceProvider.GetService(pageType) as Control;
        if (view != null)
        {
            RootNavigation.Content = view;

            // Synchronize sidebar highlight
            foreach (var item in RootNavigation.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag is string tag && _pageMap.TryGetValue(tag, out var t) && t == pageType)
                {
                    RootNavigation.SelectedItem = navItem;
                    return;
                }
            }
            foreach (var item in RootNavigation.FooterMenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag is string tag && _pageMap.TryGetValue(tag, out var t) && t == pageType)
                {
                    RootNavigation.SelectedItem = navItem;
                    return;
                }
            }
        }
    }

    private void UpdateTitleBarMetrics()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: Caption buttons take ~138px in top-right. 142px provides a clean ~10-12px gutter.
            TitleBarLeadingContent.Margin = new Thickness(14, 0, 0, 0);
            TitleBarTrailingContent.Margin = new Thickness(0, 0, 142, 0);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Apple macOS: Traffic lights (Close/Minimize/Zoom) are on the top-left (0..72px).
            // 78px clearance prevents any overlap with the traffic lights.
            // Right side has no window controls, so standard 14px padding.
            TitleBarLeadingContent.Margin = new Thickness(78, 0, 0, 0);
            TitleBarTrailingContent.Margin = new Thickness(0, 0, 14, 0);
        }
        else
        {
            // Linux: Standard symmetric 14px padding.
            TitleBarLeadingContent.Margin = new Thickness(14, 0, 0, 0);
            TitleBarTrailingContent.Margin = new Thickness(0, 0, 14, 0);
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsTitleBarButton(e.Source)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsTitleBarButton(e.Source))
        {
            e.Handled = true;
            return;
        }
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private static bool IsTitleBarButton(object? source) => source is Visual visual &&
        visual.GetSelfAndVisualAncestors().Any(control => control is Button);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen != null && WindowState == WindowState.Normal)
        {
            var size = FitStartupSize(new Size(screen.WorkingArea.Width, screen.WorkingArea.Height), screen.Scaling);
            MinWidth = Math.Min(640, size.Width);
            MinHeight = Math.Min(420, size.Height);
            Width = Math.Min(Width, size.Width);
            Height = Math.Min(Height, size.Height);
            Position = new PixelPoint(screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - (int)(Width * screen.Scaling)) / 2),
                screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - (int)(Height * screen.Scaling)) / 2));
        }
        UpdateResponsiveLayout();
    }

    public static Size FitStartupSize(Size workingAreaPixels, double scaling)
    {
        double density = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        return new Size(Math.Min(1380, Math.Max(1, workingAreaPixels.Width / density - 32)),
            Math.Min(820, Math.Max(1, workingAreaPixels.Height / density - 32)));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (RootNavigation != null) UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        double width = ClientSize.Width > 0 ? ClientSize.Width : Width;
        DeveloperBadge.IsVisible = width >= 1050;
        ThemeToggleLabel.IsVisible = width >= 850;
        double scale = _appState?.CurrentUiScale ?? (DataContext as MainWindowViewModel)?.UiScale ?? 1;
        double contentWidth = width / scale;
        // Keep page measurement finite so device tables retain row virtualization inside the scrollable shell.
        RootNavigation.Width = Math.Max(1000, contentWidth);
        RootNavigation.Height = Math.Max(600, ((ClientSize.Height > 0 ? ClientSize.Height : Height) - 38) / scale);
        RootNavigation.PaneDisplayMode = contentWidth < 900 ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.LeftCompact;
        RootNavigation.IsPaneOpen = contentWidth >= 1120;
    }
}
