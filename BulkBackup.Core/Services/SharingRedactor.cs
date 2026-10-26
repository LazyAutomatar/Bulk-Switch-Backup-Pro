using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BulkBackup.Core.Services;

/// <summary>Secret redaction only: topology identifiers, ports, VLANs and diagnostic settings remain stable.</summary>
internal sealed class SharingRedactor
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private string Token(string value)
    {
        if (value.StartsWith("[REDACTED-", StringComparison.Ordinal)) return value;
        if (!_tokens.TryGetValue(value, out var token))
        {
            token = "[REDACTED-" + Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value)))[..12] + "]";
            _tokens[value] = token;
        }
        return token;
    }

    public string Redact(string text)
    {
        text = Regex.Replace(text, @"-----BEGIN (?<kind>[^\r\n]*PRIVATE KEY)-----[\s\S]*?(?:-----END \k<kind>-----|$)",
            m => Token(m.Value), RegexOptions.IgnoreCase);
        // Quoted secrets may contain spaces. Keep the command prefix and unrelated suffix.
        const string value = @"(""[^""\r\n]*""|'[^'\r\n]*'|\S+)";
        text = Regex.Replace(text,
            @"(?im)(\b(?:password|secret|community|key-string|passphrase|pre-shared-key|psk|wpa-psk|shared-secret|auth-password|priv-password)[ \t]+(?!(?:policy|complexity|minimum-length|history|strength|recovery|lifetime|aging)\b)(?:(?:ascii|hex|encrypted|clear|ENC|aes|[0-9])[ \t]+)*)" + value,
            m => m.Groups[1].Value + Token(m.Groups[2].Value));
        text = Regex.Replace(text,
            @"(?im)((?:^[ \t]*key[ \t]+(?!chain\b|\d+[ \t]*\r?$)|\b(?:tacacs-server|radius-server)[^\r\n]*?\bkey[ \t]+|\bencryption-key[ \t]+)(?:[0-9][ \t]+)?)" + value,
            m => m.Groups[1].Value + Token(m.Groups[2].Value));
        text = Regex.Replace(text,
            @"(?im)(\b(?:auth[ \t]+(?:md5|sha(?:-?\d+)?)|priv[ \t]+(?:aes(?:[ \t]+\d+)?|des|3des))[ \t]+(?:[0-9][ \t]+)?)" + value,
            m => m.Groups[1].Value + Token(m.Groups[2].Value));
        text = Regex.Replace(text, @"(?im)(\b(?:password|secret|token|api-key|community)[ \t]*[=:][ \t]*)" + value,
            m => m.Groups[1].Value + Token(m.Groups[2].Value));
        text = Regex.Replace(text, @"(?im)(Auth User[ \t]*:[ \t]*)([^\r\n]+)",
            m => m.Groups[1].Value + Token(m.Groups[2].Value));
        return text;
    }
}
