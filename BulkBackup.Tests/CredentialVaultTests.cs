using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class CredentialVaultTests : IDisposable
{
    private const string Password = "test master password 123";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "BulkBackup.VaultTests", Guid.NewGuid().ToString("N"));
    private string Vault => Path.Combine(_folder, "vault.json");

    [Fact]
    public void Portable_roundtrip_keeps_secrets_encrypted_and_supports_delimiters_and_unicode()
    {
        using var store = new CredentialStore(_folder);
        Assert.False(Directory.Exists(_folder));
        Assert.Empty(store.LoadAllCredentials());
        Assert.Throws<InvalidOperationException>(() => store.SaveCredential("test", "user", "secret"));
        store.Create(Password);
        store.SaveCredential("Switch:::日本", "user:::name", "unique-device-password", "unique-enable-secret");
        var contents = File.ReadAllText(Vault);
        Assert.DoesNotContain("unique-device-password", contents);
        Assert.DoesNotContain("unique-enable-secret", contents);
        Assert.DoesNotContain("user:::name", contents);
        Assert.DoesNotContain(Password, contents);
        var returned = Assert.Single(store.LoadAllCredentials());
        returned.Password = "mutated copy";
        Assert.Equal("unique-device-password", Assert.Single(store.LoadAllCredentials()).Password);
        store.Lock();
        Assert.Empty(store.LoadAllCredentials());
        Assert.False(store.IsUnlocked);
        var copy = Path.Combine(_folder, "another-computer");
        Directory.CreateDirectory(copy);
        File.Copy(Vault, Path.Combine(copy, "vault.json"));
        using var other = new CredentialStore(copy);
        other.Unlock(Password);
        var restored = Assert.Single(other.LoadAllCredentials());
        Assert.Equal("Switch:::日本", restored.Name);
        Assert.Equal("unique-device-password", restored.Password);
        Assert.Equal("unique-enable-secret", restored.Secret);
        other.DeleteCredential(restored.Name);
        other.Lock(); other.Unlock(Password);
        Assert.Empty(other.LoadAllCredentials());
    }

    [Fact]
    public void Wrong_password_and_tampering_fail_closed_without_overwriting_vault()
    {
        using var store = new CredentialStore(_folder);
        store.Create(Password);
        store.SaveCredential("profile", "user", "device-secret");
        var original = File.ReadAllBytes(Vault);
        Assert.Throws<CryptographicException>(() => store.Unlock("incorrect password"));
        Assert.False(store.IsUnlocked);
        Assert.Equal(original, File.ReadAllBytes(Vault));
        Assert.Throws<InvalidOperationException>(() => store.Create(Password));
        var json = JsonNode.Parse(original)!;
        var data = Convert.FromBase64String(json["Data"]!.GetValue<string>());
        data[0] ^= 1;
        json["Data"] = Convert.ToBase64String(data);
        File.WriteAllText(Vault, json.ToJsonString());
        Assert.Throws<CryptographicException>(() => store.Unlock(Password));
        Assert.Empty(store.LoadAllCredentials());
    }

    [Fact]
    public void Concurrent_session_cannot_silently_overwrite_newer_credentials()
    {
        using var first = new CredentialStore(_folder);
        using var second = new CredentialStore(_folder);
        first.Create(Password); second.Unlock(Password);
        first.SaveCredential("new profile", "user", "password");
        Assert.Throws<IOException>(() => second.SaveCredential("stale profile", "user", "password"));
        Assert.Empty(second.LoadAllCredentials());
        second.Unlock(Password);
        Assert.Equal("new profile", Assert.Single(second.LoadAllCredentials()).Name);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void Failed_creation_leaves_store_locked()
    {
        Directory.CreateDirectory(_folder);
        var invalidPath = Path.Combine(_folder, "file");
        File.WriteAllText(invalidPath, "blocked directory");
        using var store = new CredentialStore(invalidPath);
        Assert.ThrowsAny<IOException>(() => store.Create(Password));
        Assert.False(store.IsUnlocked);
        Assert.Empty(store.LoadAllCredentials());
    }

    [Fact]
    public void Legacy_import_preserves_original_files_and_is_repeatable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var store = new CredentialStore(_folder);
        store.Create(Password);
        var legacy = Path.Combine(_folder, "old.cred");
        var bytes = ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes("legacy:::user:::password:::enable"), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(legacy, bytes);
        Assert.Equal(1, store.ImportLegacyWindowsCredentials());
        Assert.Equal(0, store.ImportLegacyWindowsCredentials());
        Assert.Equal(bytes, File.ReadAllBytes(legacy));
        store.Lock(); store.Unlock(Password);
        Assert.Equal("enable", Assert.Single(store.LoadAllCredentials()).Secret);
    }

    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
}
