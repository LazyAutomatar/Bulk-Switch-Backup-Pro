using BulkBackup.Core.Models;

namespace BulkBackup.Core.Services;

/// <summary>Connection boundary shared by discovery and backups. Tests supply an offline transport.</summary>
public interface IBackupTransport
{
    Task<bool> IsReachableAsync(string host, CancellationToken token);
    Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token);
}

public interface IBackupSession : IDisposable
{
    string Protocol => "SSH";
    IBackupShell OpenShell();
    void Disconnect();
}

public interface IBackupShell : IDisposable
{
    bool DataAvailable { get; }
    string Read();
    void Write(string text);
    void WriteLine(string text);
}
