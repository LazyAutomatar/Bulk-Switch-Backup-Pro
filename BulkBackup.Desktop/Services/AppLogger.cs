using System;
using System.IO;

namespace BulkBackup.Desktop.Services;

/// <summary>
/// Centralized thread-safe local file logging service for Bulk Switch Backup Pro.
/// Writes comprehensive operational and diagnostic logs to %APPDATA%\BulkSwitchBackup\Logs.
/// </summary>
public static class AppLogger
{
    private static readonly object LogLock = new();

    public static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BulkSwitchBackup",
        "Logs"
    );

    public static readonly string MainLogFilePath = Path.Combine(LogDirectory, "bulkswitchbackup.log");
    public static readonly string UpdateLogFilePath = Path.Combine(LogDirectory, "updates.log");

    public static event Action<string, string>? LogAppended;

    static AppLogger()
    {
        EnsureDirectory();
    }

    private static void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
        }
        catch { }
    }

    public static void Log(string category, string message)
    {
        WriteEntry(category, message, isError: false, ex: null);
    }

    public static void LogInfo(string category, string message) => Log(category, message);

    public static void LogWarning(string category, string message)
    {
        WriteEntry(category, $"[WARNING] {message}", isError: false, ex: null);
    }

    public static void LogError(string category, string message, Exception? ex = null)
    {
        WriteEntry(category, message, isError: true, ex: ex);
    }

    private static void WriteEntry(string category, string message, bool isError, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            string timestamp = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string level = isError ? "ERROR" : "INFO";
            string text = $"[{timestamp}] [{level}] [{category}] {message}";

            if (ex != null)
            {
                text += $"{Environment.NewLine}Exception: {ex.GetType().FullName}: {ex.Message}";
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                {
                    text += $"{Environment.NewLine}Stack Trace:{Environment.NewLine}{ex.StackTrace}";
                }
                if (ex.InnerException != null)
                {
                    text += $"{Environment.NewLine}Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}";
                    if (!string.IsNullOrWhiteSpace(ex.InnerException.StackTrace))
                    {
                        text += $"{Environment.NewLine}{ex.InnerException.StackTrace}";
                    }
                }
            }

            text += Environment.NewLine;

            lock (LogLock)
            {
                EnsureDirectory();
                RotateIfTooLarge(MainLogFilePath);
                File.AppendAllText(MainLogFilePath, text);

                // If this is update- or download-related, also write to dedicated updates.log for easy isolation
                if (category.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                    category.Contains("Download", StringComparison.OrdinalIgnoreCase) ||
                    category.Contains("Install", StringComparison.OrdinalIgnoreCase))
                {
                    RotateIfTooLarge(UpdateLogFilePath);
                    File.AppendAllText(UpdateLogFilePath, text);
                }
            }

            LogAppended?.Invoke(category, message);
        }
        catch { }
    }

    private static void RotateIfTooLarge(string path, long maxBytes = 10 * 1024 * 1024)
    {
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if (fi.Length > maxBytes)
                {
                    string oldPath = path + ".old";
                    if (File.Exists(oldPath))
                    {
                        File.Delete(oldPath);
                    }
                    File.Move(path, oldPath);
                }
            }
        }
        catch { }
    }
}
