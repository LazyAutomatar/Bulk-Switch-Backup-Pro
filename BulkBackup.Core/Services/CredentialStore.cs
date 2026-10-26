using BulkBackup.Core.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BulkBackup.Core.Services;

/// <summary>Portable password-protected vault. No key or master password is persisted.</summary>
public sealed class CredentialStore : IDisposable
{
    private const int Iterations = 600_000;
    private static readonly byte[] Header = Encoding.UTF8.GetBytes("BulkSwitchBackupPro vault v1");
    private readonly string _storagePath;
    private readonly object _gate = new();
    private byte[]? _key, _salt, _lastFileHash;
    private List<CredentialEntry> _entries = [];
    private string VaultPath => Path.Combine(_storagePath, "vault.json");
    public bool IsUnlocked { get { lock (_gate) return _key != null; } }
    public bool Exists => File.Exists(VaultPath);
    public CredentialStore(string? storagePath = null) => _storagePath = storagePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BulkSwitchBackupPro", "Credentials");

    public void Create(string password)
    {
        if (password.Length < 12) throw new ArgumentException("Use a master password of at least 12 characters.");
        lock (_gate)
        {
            if (Exists) throw new InvalidOperationException("A vault already exists. Unlock it instead.");
            Lock();
            _salt = RandomNumberGenerator.GetBytes(32);
            _key = Derive(password, _salt);
            try { Persist(create: true); } catch { Lock(); throw; }
        }
    }
    public void Unlock(string password)
    {
        lock (_gate)
        {
            Lock();
            byte[]? key = null, plain = null;
            try
            {
                if (new FileInfo(VaultPath).Length > 16 * 1024 * 1024) throw new InvalidDataException("Vault is too large.");
                var bytes = File.ReadAllBytes(VaultPath);
                var file = JsonSerializer.Deserialize<VaultFile>(bytes) ?? throw new InvalidDataException("Invalid vault.");
                if (file.Version != 1 || file.Iterations != Iterations || file.Salt?.Length != 32 || file.Nonce?.Length != 12 || file.Tag?.Length != 16 || file.Data == null)
                    throw new InvalidDataException("Unsupported or damaged vault format.");
                key = Derive(password, file.Salt);
                plain = new byte[file.Data.Length];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(file.Nonce, file.Data, file.Tag, plain, Header);
                var entries = JsonSerializer.Deserialize<List<CredentialEntry>>(plain) ?? throw new InvalidDataException("Invalid vault contents.");
                if (entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Name) || e.Username == null || e.Password == null || e.Secret == null))
                    throw new InvalidDataException("Invalid credential record.");
                _entries = entries; _salt = file.Salt; _lastFileHash = SHA256.HashData(bytes); _key = key; key = null;
            }
            catch (CryptographicException) { throw new CryptographicException("Wrong master password or damaged vault. Existing data was preserved."); }
            finally
            {
                if (key != null) CryptographicOperations.ZeroMemory(key);
                if (plain != null) CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
    public void Lock()
    {
        lock (_gate)
        {
            if (_key != null) CryptographicOperations.ZeroMemory(_key);
            _key = null; _salt = null; _lastFileHash = null; _entries.Clear();
        }
    }
    public List<CredentialEntry> LoadAllCredentials()
    {
        lock (_gate) return _key == null ? [] : _entries.Select(Copy).ToList();
    }
    public void SaveCredential(string name, string username, string password, string secret = "")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Profile name and username are required.");
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(secret);
        lock (_gate)
        {
            RequireUnlocked();
            var previous = _entries;
            _entries = _entries.Where(e => e.Name != name).Select(Copy).ToList();
            _entries.Add(new() { Name = name, Username = username, Password = password, Secret = secret });
            try { Persist(); } catch { _entries = previous; throw; }
        }
    }
    public void DeleteCredential(string name)
    {
        lock (_gate)
        {
            RequireUnlocked();
            var previous = _entries;
            _entries = _entries.Where(e => e.Name != name).Select(Copy).ToList();
            try { Persist(); } catch { _entries = previous; throw; }
        }
    }
    // Explicit migration on the original Windows account. Legacy encrypted files are preserved.
    public int ImportLegacyWindowsCredentials()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Import legacy credentials on the original Windows account, then copy the encrypted vault to your other computer.");
        lock (_gate)
        {
            RequireUnlocked();
            var previous = _entries;
            var imported = _entries.Select(Copy).ToList();
            int count = 0;
            foreach (var path in Directory.EnumerateFiles(_storagePath, "*.cred"))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                try
                {
                    var fields = Encoding.UTF8.GetString(plain).Split([":::"], 4, StringSplitOptions.None);
                    if (fields.Length < 3 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[1]))
                        throw new InvalidDataException("A legacy credential is damaged; import was cancelled.");
                    if (imported.Any(e => e.Name == fields[0])) continue;
                    imported.Add(new() { Name = fields[0], Username = fields[1], Password = fields[2], Secret = fields.Length == 4 ? fields[3] : "" });
                    count++;
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            _entries = imported;
            try { Persist(); } catch { _entries = previous; throw; }
            return count;
        }
    }
    private void RequireUnlocked()
    {
        if (_key == null) throw new InvalidOperationException("Unlock the credential vault first.");
    }
    private void Persist(bool create = false)
    {
        RequireUnlocked();
        Directory.CreateDirectory(_storagePath);
        using var lease = new FileStream(Path.Combine(_storagePath, "vault.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (create ? Exists : !Exists || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(VaultPath)), _lastFileHash!))
            throw new IOException("Vault changed in another session. Lock and unlock it before saving.");
        var plain = JsonSerializer.SerializeToUtf8Bytes(_entries);
        var temp = Path.Combine(_storagePath, $"vault.{Guid.NewGuid():N}.tmp");
        try
        {
            var file = new VaultFile { Salt = _salt!, Nonce = RandomNumberGenerator.GetBytes(12), Tag = new byte[16], Data = new byte[plain.Length] };
            using var aes = new AesGcm(_key!, 16);
            aes.Encrypt(file.Nonce, plain, file.Data, file.Tag, Header);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(file);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var output = new FileStream(temp, options)) { output.Write(bytes); output.Flush(true); }
            File.Move(temp, VaultPath, overwrite: !create);
            _lastFileHash = SHA256.HashData(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temp)) File.Delete(temp); }
    }
    private static byte[] Derive(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
    private static CredentialEntry Copy(CredentialEntry e) => new() { Name = e.Name, Username = e.Username, Password = e.Password, Secret = e.Secret };
    public void Dispose() => Lock();
    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public int Iterations { get; set; } = CredentialStore.Iterations;
        public byte[] Salt { get; set; } = [];
        public byte[] Nonce { get; set; } = [];
        public byte[] Tag { get; set; } = [];
        public byte[] Data { get; set; } = [];
    }
}
