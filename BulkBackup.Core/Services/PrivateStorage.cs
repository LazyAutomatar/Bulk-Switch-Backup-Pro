using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace BulkBackup.Core.Services;

public static class PrivateStorage
{
    public static void CreateDirectory(string path)
    {
        if (Directory.Exists(path)) return;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            RestrictWindows(new DirectoryInfo(path));
        }
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static FileStream CreateFile(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var stream = new FileStream(path, options);
        try
        {
            if (OperatingSystem.IsWindows()) RestrictWindows(new FileInfo(path));
            return stream;
        }
        catch { stream.Dispose(); File.Delete(path); throw; }
    }

    public static async Task WriteTextAsync(string path, string text, CancellationToken token = default)
    {
        await using var stream = CreateFile(path);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text.AsMemory(), token);
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictWindows(FileSystemInfo item)
    {
        var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("Cannot determine the current Windows user.");
        if (item is DirectoryInfo directory)
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);
        }
        else
        {
            var acl = new FileSecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            ((FileInfo)item).SetAccessControl(acl);
        }
    }
}
