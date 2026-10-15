using BulkSwitchBackupPro.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BulkSwitchBackupPro.Services
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class CredentialStore
    {
        private readonly string _storagePath;

        public CredentialStore()
        {
            _storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BulkSwitchBackupPro",
                "Credentials"
            );
            Directory.CreateDirectory(_storagePath);
        }

        public void SaveCredential(string name, string username, string password, string secret = "")
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name required", nameof(name));
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username required", nameof(username));
            if (password == null) throw new ArgumentNullException(nameof(password));

            string plainText = $"{name}:::{username}:::{password}:::{secret}";
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            // Encrypt using DPAPI (CurrentUser scope ensures only the current Windows user on this machine can decrypt)
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            string filePath = GetFilePath(name);
            File.WriteAllBytes(filePath, encryptedBytes);
        }

        public List<CredentialEntry> LoadAllCredentials()
        {
            var list = new List<CredentialEntry>();
            if (!Directory.Exists(_storagePath)) return list;

            foreach (var file in Directory.GetFiles(_storagePath, "*.cred"))
            {
                try
                {
                    byte[] encryptedBytes = File.ReadAllBytes(file);
                    byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    string plainText = Encoding.UTF8.GetString(plainBytes);

                    var parts = plainText.Split(new[] { ":::" }, 4, StringSplitOptions.None);
                    if (parts.Length >= 3)
                    {
                        list.Add(new CredentialEntry
                        {
                            Name = parts[0],
                            Username = parts[1],
                            Password = parts[2],
                            Secret = parts.Length > 3 ? parts[3] : string.Empty
                        });
                    }
                }
                catch
                {
                    // Skip corrupt or unreadable entries
                }
            }
            return list;
        }

        public void DeleteCredential(string name)
        {
            string filePath = GetFilePath(name);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private string GetFilePath(string name)
        {
            string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_storagePath, $"{safeName}.cred");
        }
    }
}
