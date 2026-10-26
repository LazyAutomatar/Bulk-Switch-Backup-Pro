using BulkBackup.Core.Models;
using Renci.SshNet.Common;
using System.Net.Sockets;
using System.Text;

namespace BulkBackup.Core.Services;

/// <summary>Prefer SSH; fall back to Telnet for connectivity/negotiation failures, never SSH credential rejection.</summary>
public sealed class FallbackBackupTransport(IBackupTransport? ssh = null, IBackupTransport? telnet = null) : IBackupTransport
{
    private readonly IBackupTransport _ssh = ssh ?? new SshNetBackupTransport();
    private readonly IBackupTransport _telnet = telnet ?? new TelnetBackupTransport();
    public async Task<bool> IsReachableAsync(string host, CancellationToken token) =>
        await _ssh.IsReachableAsync(host, token) || await _telnet.IsReachableAsync(host, token);
    public async Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token)
    {
        try { return await _ssh.ConnectAsync(host, credential, token); }
        catch (SshAuthenticationException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is SocketException or IOException or SshException or TimeoutException or OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return await _telnet.ConnectAsync(host, credential, token);
        }
    }
}

public sealed class TelnetBackupTransport : IBackupTransport
{
    public Task<bool> IsReachableAsync(string host, CancellationToken token) => SshNetBackupTransport.IsPortReachableAsync(host, 23, 2500, token);
    public async Task<IBackupSession> ConnectAsync(string host, CredentialEntry credential, CancellationToken token)
    {
        var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(7));
            await client.ConnectAsync(host, 23, timeout.Token);
            return new Session(client);
        }
        catch { client.Dispose(); throw; }
    }
    private sealed class Session(TcpClient client) : IBackupSession
    {
        public string Protocol => "Telnet";
        public IBackupShell OpenShell() => new Shell(client);
        public void Disconnect() => client.Close();
        public void Dispose() => client.Dispose();
    }
    private sealed class Shell : IBackupShell
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly TelnetCodec _codec = new();
        public Shell(TcpClient client) { _client = client; _stream = client.GetStream(); _stream.WriteTimeout = 5000; }
        public bool DataAvailable
        {
            get
            {
                if (_client.Client.Poll(0, SelectMode.SelectRead) && _client.Available == 0) throw new IOException("Telnet peer closed the connection.");
                return _stream.DataAvailable;
            }
        }
        public string Read()
        {
            var buffer = new byte[Math.Min(8192, Math.Max(1, _client.Available))];
            int count = _stream.Read(buffer);
            if (count == 0) throw new IOException("Telnet peer closed the connection.");
            return _codec.Decode(buffer.AsSpan(0, count), reply => _stream.Write(reply));
        }
        public void Write(string text) => _stream.Write(TelnetCodec.Encode(text));
        public void WriteLine(string text) => Write(text + "\r\n");
        public void Dispose() => _stream.Dispose();
    }
}

/// <summary>Stateful Telnet negotiation parser; IAC and subnegotiations may span TCP reads.</summary>
public sealed class TelnetCodec
{
    private int _state;
    private byte _command;
    private readonly HashSet<(byte, byte)> _answered = [];
    public string Decode(ReadOnlySpan<byte> bytes, Action<byte[]> reply)
    {
        var text = new List<byte>();
        foreach (byte b in bytes)
        {
            switch (_state)
            {
                case 0: if (b == 255) _state = 1; else if (b != 0) text.Add(b); break;
                case 1:
                    if (b == 255) { text.Add(b); _state = 0; }
                    else if (b == 250) _state = 3;
                    else if (b is 251 or 252 or 253 or 254) { _command = b; _state = 2; }
                    else _state = 0;
                    break;
                case 2:
                    if (_answered.Add((_command, b)))
                    {
                        if (_command == 251) reply([255, (byte)(b is 1 or 3 ? 253 : 254), b]);
                        else if (_command == 253) reply([255, 252, b]);
                    }
                    _state = 0;
                    break;
                case 3: if (b == 255) _state = 4; break;
                case 4: _state = b == 240 ? 0 : 3; break;
            }
        }
        return Encoding.Latin1.GetString(text.ToArray());
    }
    public static byte[] Encode(string text) => Encoding.Latin1.GetBytes(text).SelectMany(b => b == 255 ? new byte[] { 255, 255 } : [b]).ToArray();
}
