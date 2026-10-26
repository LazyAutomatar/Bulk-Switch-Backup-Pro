using System.Net.Sockets;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;
using Renci.SshNet.Common;

namespace BulkBackup.Tests;

public sealed class LoginAndFallbackTests
{
    private static readonly CredentialEntry Credential = new() { Username = "operator", Password = "test-password" };

    [Fact]
    public async Task Repeated_and_fragmented_legacy_login_challenges_reach_a_device_prompt()
    {
        using var shell = new ScriptedShell(["lo", "gin:"],
            ["Pass", "word:"], ["\r\nUser", "name:"], ["Password:"], ["\r\nSG350#\r\n"]);
        Assert.Equal("SG350#", await LoginBackupTransport.CompleteLoginAsync(shell, Credential, CancellationToken.None));
        Assert.Equal(["operator", "test-password", "operator", "test-password"], shell.Writes);
    }

    [Theory]
    [InlineData("Login incorrect")]
    [InlineData("New Password:")]
    [InlineData("Your password expired")]
    public async Task Failed_login_or_password_change_is_not_a_successful_session(string response)
    {
        using var shell = new ScriptedShell([response]);
        await Assert.ThrowsAsync<SshAuthenticationException>(() => LoginBackupTransport.CompleteLoginAsync(shell, Credential, CancellationToken.None));
        Assert.Empty(shell.Writes);
    }

    [Fact]
    public async Task Endless_challenges_are_bounded_and_cancellation_is_observed()
    {
        using var shell = new ScriptedShell(["Username:"], ["Username:"], ["Username:"], ["Username:"]);
        await Assert.ThrowsAsync<SshAuthenticationException>(() => LoginBackupTransport.CompleteLoginAsync(shell, Credential, CancellationToken.None));
        Assert.Equal(3, shell.Writes.Count);
        using var silent = new ScriptedShell([]);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoginBackupTransport.CompleteLoginAsync(silent, Credential, cancellation.Token));
        await Assert.ThrowsAsync<TimeoutException>(() => LoginBackupTransport.CompleteLoginAsync(silent, Credential, CancellationToken.None, 20));
    }

    [Fact]
    public async Task Closed_ssh_port_falls_back_to_telnet_and_reports_the_protocol()
    {
        var ssh = new StubTransport(false, new SocketException((int)SocketError.ConnectionRefused));
        var telnet = new StubTransport(true);
        var fallback = new FallbackBackupTransport(ssh, telnet);
        Assert.True(await fallback.IsReachableAsync("192.0.2.1", CancellationToken.None));
        Assert.Equal(1, telnet.Probes);
        using var session = await new LoginBackupTransport(fallback).ConnectAsync("192.0.2.1", Credential, CancellationToken.None);
        Assert.Equal("Telnet", session.Protocol);
        Assert.Equal("SW#", session.OpenShell().Read());
        Assert.Equal(1, telnet.Connections);
    }

    [Fact]
    public async Task Ssh_authentication_rejection_does_not_downgrade_and_cancellation_does_not_connect()
    {
        var telnet = new StubTransport(true);
        var fallback = new FallbackBackupTransport(new StubTransport(true, new SshAuthenticationException("rejected")), telnet);
        await Assert.ThrowsAsync<SshAuthenticationException>(() => fallback.ConnectAsync("192.0.2.1", Credential, CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        fallback = new FallbackBackupTransport(new StubTransport(false, new OperationCanceledException(cancellation.Token)), telnet);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fallback.ConnectAsync("192.0.2.1", Credential, cancellation.Token));
        Assert.Equal(0, telnet.Connections);
    }

    [Fact]
    public void Telnet_negotiation_survives_fragmented_packets_and_escapes_iac()
    {
        var codec = new TelnetCodec();
        var replies = new List<byte[]>();
        Assert.Equal("", codec.Decode([255], replies.Add));
        Assert.Equal("", codec.Decode([251], replies.Add));
        Assert.Equal("lo", codec.Decode([1, (byte)'l', (byte)'o', 255, 250, 24], replies.Add));
        Assert.Equal("gin:", codec.Decode([1, 255, 240, (byte)'g', (byte)'i', (byte)'n', 58], replies.Add));
        Assert.Equal(new byte[] { 255, 253, 1 }, Assert.Single(replies));
        Assert.Equal(new byte[] { 255, 255 }, TelnetCodec.Encode("ÿ"));
        Assert.Equal("ÿ", codec.Decode([255, 255], replies.Add));
    }

    private sealed class ScriptedShell(string[] initial, params string[][] replies) : IBackupShell
    {
        private readonly Queue<string> _pending = new(initial);
        private readonly Queue<string[]> _replies = new(replies);
        public List<string> Writes { get; } = [];
        public bool DataAvailable => _pending.Count > 0;
        public string Read() => _pending.Dequeue();
        public void Write(string text) => WriteLine(text);
        public void WriteLine(string text) { Writes.Add(text); if (_replies.TryDequeue(out var chunks)) foreach (var chunk in chunks) _pending.Enqueue(chunk); }
        public void Dispose() { }
    }

    private sealed class StubTransport(bool reachable, Exception? failure = null) : IBackupTransport
    {
        public int Connections, Probes;
        public Task<bool> IsReachableAsync(string host, CancellationToken token) { Probes++; return Task.FromResult(reachable); }
        public Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token)
        {
            Connections++; if (failure != null) throw failure;
            return Task.FromResult<IBackupSession>(new Session());
        }
        private sealed class Session : IBackupSession
        {
            public string Protocol => "Telnet";
            public IBackupShell OpenShell() => new ScriptedShell(["Username:"], ["Password:"], ["SW#"]);
            public void Disconnect() { }
            public void Dispose() { }
        }
    }
}
