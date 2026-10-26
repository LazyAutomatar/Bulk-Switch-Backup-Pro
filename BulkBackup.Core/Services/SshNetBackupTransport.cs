using BulkBackup.Core.Models;
using Renci.SshNet;
using System.Net.Sockets;

namespace BulkBackup.Core.Services;

/// <summary>Production SSH.NET implementation; no sockets are opened by constructing this object.</summary>
public sealed class SshNetBackupTransport : IBackupTransport
{
    public Task<bool> IsReachableAsync(string host, CancellationToken token) =>
        IsPortReachableAsync(host, 22, 2500, token);

    public static async Task<bool> IsPortReachableAsync(string host, int port, int timeoutMs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token)
    {
        var keyboard = new KeyboardInteractiveAuthenticationMethod(credential.Username);
        keyboard.AuthenticationPrompt += (_, args) =>
        {
            foreach (var prompt in args.Prompts)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(prompt.Request, @"(?i)^(?:\s*password\s*:|.*'s password\s*:)") && !prompt.IsEchoed)
                    prompt.Response = credential.Password;
                else if (System.Text.RegularExpressions.Regex.IsMatch(prompt.Request, @"(?i)^\s*(?:username|login)\s*:"))
                    prompt.Response = credential.Username;
            }
        };
        var client = new SshClient(new ConnectionInfo(host, credential.Username,
            new PasswordAuthenticationMethod(credential.Username, credential.Password), keyboard));
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(7);
        try
        {
            await client.ConnectAsync(token).ConfigureAwait(false);
            return new Session(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class Session(SshClient client) : IBackupSession
    {
        public IBackupShell OpenShell() => new Shell(client.CreateShellStream("vt100", 255, 50, 800, 600, 4096));
        public void Disconnect() => client.Disconnect();
        public void Dispose() => client.Dispose();
    }

    private sealed class Shell(ShellStream stream) : IBackupShell
    {
        public bool DataAvailable => stream.DataAvailable;
        public string Read() => stream.Read();
        public void Write(string text) => stream.Write(text);
        public void WriteLine(string text) => stream.WriteLine(text);
        public void Dispose() => stream.Dispose();
    }
}
