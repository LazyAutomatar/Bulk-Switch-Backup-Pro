using BulkBackup.Core.Models;
using Renci.SshNet.Common;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BulkBackup.Core.Services;

/// <summary>Completes terminal authentication before a credential attempt is accepted by the engine.</summary>
public sealed class LoginBackupTransport(IBackupTransport inner) : IBackupTransport
{
    public Task<bool> IsReachableAsync(string host, CancellationToken token) => inner.IsReachableAsync(host, token);
    public async Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token)
    {
        var session = await inner.ConnectAsync(host, credential, token);
        IBackupShell? shell = null;
        try
        {
            shell = session.OpenShell();
            string prompt = await CompleteLoginAsync(shell, credential, token);
            return new ReadySession(session, new ReadyShell(shell, prompt));
        }
        catch { shell?.Dispose(); session.Dispose(); throw; }
    }
    public static async Task<string> CompleteLoginAsync(IBackupShell shell, CredentialEntry credential, CancellationToken token, int timeoutMs = 10000)
    {
        var pending = new StringBuilder();
        var timer = Stopwatch.StartNew();
        int usernames = 0, passwords = 0, enters = 0;
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            if (!shell.DataAvailable) { await Task.Delay(10, token); continue; }
            pending.Append(shell.Read());
            if (pending.Length > 65536) throw new SshAuthenticationException("Login banner exceeded the supported size.");
            string clean = Regex.Replace(pending.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]|\x08", "");
            string tail = clean.Replace("\r", "\n").Split('\n').LastOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? "";
            if (Regex.IsMatch(clean, @"(?i)(?:authentication|login)\s+(?:failed|incorrect)|access denied|bad passwords?|password\s+(?:expired|must be changed)|(?:new|current|old)\s+password\s*:"))
                throw new SshAuthenticationException("Device rejected login or requires an interactive password change.");
            if (DevicePlatforms.IsPrompt(tail)) return tail;
            if (Regex.IsMatch(tail, @"(?i)(?:user\s*name|login|user)\s*:\s*$"))
            {
                if (++usernames > 3) throw new SshAuthenticationException("Repeated username prompts; credential rejected.");
                shell.WriteLine(credential.Username); pending.Clear();
            }
            else if (Regex.IsMatch(tail, @"(?i)password\s*:\s*$"))
            {
                if (++passwords > 3) throw new SshAuthenticationException("Repeated password prompts; credential rejected.");
                shell.WriteLine(credential.Password); pending.Clear();
            }
            else if (Regex.IsMatch(tail, @"(?i)press (?:return|enter|any key).*(?:continue|start|begin)"))
            {
                if (++enters > 2) throw new SshAuthenticationException("Login did not advance after the welcome prompt.");
                shell.WriteLine(""); pending.Clear();
            }
        }
        throw new TimeoutException("No device prompt received before the login timeout.");
    }
    private sealed class ReadySession(IBackupSession session, ReadyShell shell) : IBackupSession
    {
        public string Protocol => session.Protocol;
        public IBackupShell OpenShell() => shell;
        public void Disconnect() => session.Disconnect();
        public void Dispose() { shell.Dispose(); session.Dispose(); }
    }
    private sealed class ReadyShell(IBackupShell inner, string prompt) : IBackupShell
    {
        private string? _initial = prompt;
        private bool _disposed;
        public bool DataAvailable => _initial != null || inner.DataAvailable;
        public string Read() { if (_initial is not null) { var value = _initial; _initial = null; return value; } return inner.Read(); }
        public void Write(string text) => inner.Write(text);
        public void WriteLine(string text) => inner.WriteLine(text);
        public void Dispose() { if (!_disposed) { _disposed = true; inner.Dispose(); } }
    }
}
