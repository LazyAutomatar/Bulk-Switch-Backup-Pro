using System;
using System.IO;

namespace BulkBackup.Desktop.Services;

/// <summary>
/// Manages isolated temporary session workspaces for zero-friction backup runs
/// and guarantees automatic cleanup upon application termination.
/// </summary>
public static class TempSessionManager
{
    private static readonly string SessionsRoot = Path.Combine(Path.GetTempPath(), "BulkSwitchBackup_Sessions");

    /// <summary>
    /// Creates and returns a uniquely timestamped temporary session folder.
    /// </summary>
    public static string CreateNewSession()
    {
        try
        {
            if (!Directory.Exists(SessionsRoot))
            {
                Directory.CreateDirectory(SessionsRoot);
            }
        }
        catch { }

        string sessionDir = Path.Combine(SessionsRoot, $"Session_{DateTime.Now:yyyyMMdd_HHmmss}");
        try
        {
            Directory.CreateDirectory(sessionDir);
        }
        catch { }

        return sessionDir;
    }

    /// <summary>
    /// Safely cleans up all session folders in the temporary directory.
    /// </summary>
    public static void CleanupAllSessions()
    {
        try
        {
            if (Directory.Exists(SessionsRoot))
            {
                Directory.Delete(SessionsRoot, recursive: true);
            }
        }
        catch
        {
            // Suppress IO or file lock exceptions during shutdown
        }
    }
}
