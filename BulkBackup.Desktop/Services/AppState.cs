using System;
using Avalonia;
using Avalonia.Styling;

namespace BulkBackup.Desktop.Services;

public class AppState
{
    // Theme Management: 0 = System Default, 1 = Dark Mode, 2 = Light Mode
    public int CurrentThemeIndex { get; private set; } = 0;
    public event Action<int>? ThemeChanged;

    public void SetTheme(int themeIndex)
    {
        CurrentThemeIndex = themeIndex;
        if (Application.Current != null)
        {
            switch (themeIndex)
            {
                case 0:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Default;
                    break;
                case 1:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                    break;
                case 2:
                    Application.Current.RequestedThemeVariant = ThemeVariant.Light;
                    break;
            }
        }
        ThemeChanged?.Invoke(themeIndex);
    }

    // Dynamic UI Scaling & Display Density Management
    public double DetectedRenderScaling { get; private set; } = 1.0;
    public int CurrentUiScaleIndex { get; private set; } = 0; // 0 = Auto
    public double CurrentUiScale { get; private set; } = 1.0;
    public string CurrentUiScaleText => CurrentUiScaleIndex == 0 
        ? $"{(int)Math.Round(CurrentUiScale * 100)}% (Auto)" 
        : $"{(int)Math.Round(CurrentUiScale * 100)}%";

    public event Action<double>? UiScaleChanged;

    public void SetDetectedRenderScaling(double renderScaling)
    {
        DetectedRenderScaling = renderScaling;
        if (CurrentUiScaleIndex == 0) // Auto
        {
            ApplyAutoScaling();
        }
    }

    public void SetUiScale(int scaleIndex)
    {
        CurrentUiScaleIndex = scaleIndex;
        if (scaleIndex == 0)
        {
            ApplyAutoScaling();
        }
        else
        {
            CurrentUiScale = scaleIndex switch
            {
                1 => 0.75, // Ultra Compact
                2 => 0.80, // Very Compact
                3 => 0.85, // Compact
                4 => 0.90, // Sleek
                5 => 1.00, // Standard 1:1
                6 => 1.10, // Comfortable
                7 => 1.25, // Large
                _ => 1.00
            };
            UiScaleChanged?.Invoke(CurrentUiScale);
        }
    }

    public void StepUiScale(bool zoomIn)
    {
        CurrentUiScaleIndex = -1; // Custom
        double next = zoomIn ? CurrentUiScale + 0.05 : CurrentUiScale - 0.05;
        CurrentUiScale = Math.Clamp(Math.Round(next, 2), 0.70, 1.30);
        UiScaleChanged?.Invoke(CurrentUiScale);
    }

    private void ApplyAutoScaling()
    {
        if (DetectedRenderScaling >= 1.75)
        {
            CurrentUiScale = 0.75;
        }
        else if (DetectedRenderScaling >= 1.35)
        {
            CurrentUiScale = 0.85;
        }
        else
        {
            CurrentUiScale = 1.00;
        }
        UiScaleChanged?.Invoke(CurrentUiScale);
    }

    // Global Backup Engine Execution Tracking (Header Glow / Progress Bar)
    public bool IsRunning { get; private set; }
    public double OverallProgress { get; private set; } // 0 to 100
    public string StatusSummary { get; private set; } = "Ready";

    public event Action? RunningStatusUpdated;

    public void UpdateRunningStatus(bool isRunning, double progress, string status)
    {
        IsRunning = isRunning;
        OverallProgress = Math.Clamp(progress, 0, 100);
        StatusSummary = status;
        RunningStatusUpdated?.Invoke();
    }
}
