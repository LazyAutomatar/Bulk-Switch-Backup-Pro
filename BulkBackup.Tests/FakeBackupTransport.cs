using System.Collections.Concurrent;
using BulkBackup.Core.Models;
using BulkBackup.Core.Services;
using Renci.SshNet.Common;

namespace BulkBackup.Tests;

/// <summary>Entirely in-memory transport. Never creates a socket or reads stored credentials.</summary>
internal sealed class FakeBackupTransport : IBackupTransport
{
    public ConcurrentDictionary<string, FakeDevice> Devices { get; } = new();
    public ConcurrentQueue<(string Host, string Username)> Attempts { get; } = new();
    public ConcurrentQueue<(string Host, string Command)> Commands { get; } = new();
    public Func<string, CancellationToken, Task>? BeforeConnect { get; set; }
    public Func<string, CancellationToken, Task>? BeforeProbe { get; set; }
    private int _active;
    private int _maximumActive;
    private int _disposed;
    private int _shellsDisposed;
    public int Active => _active;
    public int MaximumActive => _maximumActive;
    public int Disposed => _disposed;
    public int ShellsDisposed => _shellsDisposed;

    public FakeDevice Add(string host, string? hostname = null)
    {
        var device = new FakeDevice(hostname ?? "SW-" + host.Replace('.', '-'));
        device.Outputs["show version"] = "Cisco IOS XE Software, Version 17.12.4";
        Devices[host] = device;
        return device;
    }

    public async Task<bool> IsReachableAsync(string host, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (BeforeProbe is not null) await BeforeProbe(host, token);
        return Devices.TryGetValue(host, out var device) && device.Reachable;
    }

    public async Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Attempts.Enqueue((host, credential.Username));
        var device = Devices[host];
        if (device.RejectedUsers.Contains(credential.Username))
            throw new SshAuthenticationException("Rejected test credential");
        var active = Interlocked.Increment(ref _active);
        int maximum;
        do { maximum = _maximumActive; }
        while (active > maximum && Interlocked.CompareExchange(ref _maximumActive, active, maximum) != maximum);
        try
        {
            if (BeforeConnect is not null) await BeforeConnect(host, token);
            return new Session(this, host, device);
        }
        catch
        {
            Interlocked.Decrement(ref _active);
            throw;
        }
    }

    private sealed class Session(FakeBackupTransport transport, string host, FakeDevice device) : IBackupSession
    {
        private bool _disposed;
        public IBackupShell OpenShell() => new Shell(transport, host, device);
        public void Disconnect() { }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Decrement(ref transport._active);
            Interlocked.Increment(ref transport._disposed);
        }
    }

    private sealed class Shell : IBackupShell
    {
        private readonly FakeBackupTransport _transport;
        private readonly string _host;
        private readonly FakeDevice _device;
        private readonly Queue<string> _responses = new();
        private bool _awaitingSecret;
        private bool _privileged;

        public Shell(FakeBackupTransport transport, string host, FakeDevice device)
        {
            (_transport, _host, _device) = (transport, host, device);
            _privileged = !device.RequiresEnable;
            if (!device.Silent) _responses.Enqueue(Prompt);
        }

        private string Prompt => _device.PromptOverride ?? $"{_device.Hostname}{(_privileged ? '#' : '>')}";
        public bool DataAvailable => _responses.Count > 0;
        public string Read() => _responses.Dequeue();
        public void Write(string text)
        {
            _transport.Commands.Enqueue((_host, text));
            if (text is " " or "\r" or "y") _responses.Enqueue($"\r\nremaining page\r\n{Prompt}");
        }
        public void WriteLine(string text)
        {
            _transport.Commands.Enqueue((_host, text));
            if (_awaitingSecret)
            {
                _awaitingSecret = false;
                _privileged = text == "test-enable-secret";
                _responses.Enqueue(Prompt);
                return;
            }
            if (text == "enable")
            {
                _awaitingSecret = true;
                _responses.Enqueue("Password:");
                return;
            }
            _device.OnCommand?.Invoke(text);
            if (_device.FailedCommands.Contains(text)) throw new TimeoutException("Simulated command timeout");
            if (_device.PagedCommands.Contains(text))
            {
                _responses.Enqueue($"{text}\r\nfirst page\r\n{_device.PagerMarker[..(_device.PagerMarker.Length / 2)]}");
                _responses.Enqueue(_device.PagerMarker[(_device.PagerMarker.Length / 2)..]);
                return;
            }
            var output = _device.Outputs.GetValueOrDefault(text, "captured " + text);
            _responses.Enqueue($"{text}\r\n{output}\r\n{Prompt}");
        }
        public void Dispose() => Interlocked.Increment(ref _transport._shellsDisposed);
    }
}

internal sealed class FakeDevice(string hostname)
{
    public string Hostname { get; } = hostname;
    public string? PromptOverride { get; set; }
    public string PagerMarker { get; set; } = "--More--";
    public bool Reachable { get; set; } = true;
    public bool RequiresEnable { get; set; }
    public bool Silent { get; set; }
    public Dictionary<string, string> Outputs { get; } = new();
    public HashSet<string> RejectedUsers { get; } = new();
    public HashSet<string> FailedCommands { get; } = new();
    public HashSet<string> PagedCommands { get; } = new();
    public Action<string>? OnCommand { get; set; }
}

