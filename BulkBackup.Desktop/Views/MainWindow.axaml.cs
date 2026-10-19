using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        { "About", typeof(AboutView) }
    };

    public MainWindow()
    {
        InitializeComponent();
        _serviceProvider = null!;
        _navigationService = null!;
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

        if (OperatingSystem.IsWindows())
        {
            TitleBarTrailingContent.Margin = new Thickness(0, 0, 140, 0);
        }
        else if (OperatingSystem.IsMacOS())
        {
            TitleBarLeadingContent.Margin = new Thickness(76, 0, 0, 0);
            TitleBarTrailingContent.Margin = new Thickness(0, 0, 12, 0);
        }
        else
        {
            TitleBarTrailingContent.Margin = new Thickness(0, 0, 12, 0);
        }

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
        SizeChanged += (s, e) =>
        {
            RootNavigation.IsPaneOpen = e.NewSize.Width >= 1120;
        };

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

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
