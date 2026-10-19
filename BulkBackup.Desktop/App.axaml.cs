using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using BulkBackup.Core.Services;
using BulkBackup.Desktop.Services;
using BulkBackup.Desktop.ViewModels;
using BulkBackup.Desktop.Views;

namespace BulkBackup.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Purge any stale session files from prior executions
        TempSessionManager.CleanupAllSessions();

        AppDomain.CurrentDomain.ProcessExit += (s, e) => TempSessionManager.CleanupAllSessions();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
            desktop.Exit += (s, e) => TempSessionManager.CleanupAllSessions();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core State & Services
        services.AddSingleton<AppState>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<AboutViewModel>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<RunMonitorView>();
        services.AddTransient<CredentialsView>();
        services.AddTransient<CommandProfilesView>();
        services.AddTransient<AboutView>();
    }
}
