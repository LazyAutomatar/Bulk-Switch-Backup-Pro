using System;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using BulkBackup.Desktop.Services;

namespace BulkBackup.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppState _appState;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _runningStatusSummary = "Ready";

    [ObservableProperty] private string _applicationTitle = BuildChannel.IsLocalBeta
        ? "Bulk Switch Backup Pro · Local beta" : "Bulk Switch Backup Pro";
    [ObservableProperty] private Symbol _themeSymbol = Symbol.DarkTheme;
    [ObservableProperty] private string _themeButtonText = "Dark Mode";

    [ObservableProperty] private double _uiScale = 1.0;
    [ObservableProperty] private string _uiScaleText = "100%";

    private bool _isDark = true;

    public MainWindowViewModel(AppState appState)
    {
        _appState = appState;
        _appState.RunningStatusUpdated += SyncRunningStatus;
        _appState.ThemeChanged += OnThemeChanged;
        _appState.UiScaleChanged += OnAppStateUiScaleChanged;
        SyncRunningStatus();
        UpdateThemeState(_appState.CurrentThemeIndex);
        UpdateUiScale(_appState.CurrentUiScale);

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (s, e) =>
            {
                if (_appState.CurrentThemeIndex == 0)
                {
                    Dispatcher.UIThread.Post(() => UpdateThemeState(0));
                }
            };
        }
    }

    private void OnAppStateUiScaleChanged(double scale)
    {
        Dispatcher.UIThread.Post(() => UpdateUiScale(scale));
    }

    private void UpdateUiScale(double scale)
    {
        UiScale = scale;
        UiScaleText = _appState.CurrentUiScaleText;
    }

    [RelayCommand]
    private void ZoomIn() => _appState.StepUiScale(true);

    [RelayCommand]
    private void ZoomOut() => _appState.StepUiScale(false);

    [RelayCommand]
    private void ResetZoom() => _appState.SetUiScale(0); // 0 = Auto

    private void OnThemeChanged(int themeIndex)
    {
        Dispatcher.UIThread.Post(() => UpdateThemeState(themeIndex));
    }

    private void UpdateThemeState(int themeIndex)
    {
        if (themeIndex == 2)
        {
            _isDark = false;
            ThemeSymbol = Symbol.Highlight;
            ThemeButtonText = "Light Mode";
        }
        else if (themeIndex == 1)
        {
            _isDark = true;
            ThemeSymbol = Symbol.DarkTheme;
            ThemeButtonText = "Dark Mode";
        }
        else // 0: System Default
        {
            bool isActuallyDark = Application.Current?.ActualThemeVariant != ThemeVariant.Light;
            _isDark = isActuallyDark;
            ThemeSymbol = isActuallyDark ? Symbol.DarkTheme : Symbol.Highlight;
            ThemeButtonText = isActuallyDark ? "Dark (System)" : "Light (System)";
        }
    }

    private void SyncRunningStatus()
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = _appState.IsRunning;
            OverallProgress = _appState.OverallProgress;
            RunningStatusSummary = _appState.StatusSummary;
        });
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        int newTheme = _isDark ? 2 : 1;
        _appState.SetTheme(newTheme);
    }
}
