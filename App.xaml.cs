using System.Windows;

namespace BulkSwitchBackupPro;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.DispatcherUnhandledException += (s, ev) => 
        {
            System.IO.File.WriteAllText("crash.log", ev.Exception.ToString());
            MessageBox.Show("Fatal error: " + ev.Exception.Message);
            ev.Handled = true;
        };

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}

