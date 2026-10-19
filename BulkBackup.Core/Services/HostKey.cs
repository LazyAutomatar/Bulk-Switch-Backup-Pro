namespace BulkBackup.Core.Services;

/// <summary>
/// Canonical hostname normalization and comparison helper for switch discovery.
/// Ensures consistent matching across domain suffixes, serial numbers in parentheses, and case.
/// E.g. 'SW1', 'sw1', 'SW1.corp.local', and 'SW1(FOC1234ABCD)' all resolve to 'SW1'.
/// </summary>
public static class HostKey
{
    public static string Of(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return string.Empty;

        var h = hostname.Trim();

        // CDP sometimes appends serial or switch number: SW1(FOC1234ABCD)
        int paren = h.IndexOf('(');
        if (paren > 0) h = h[..paren];

        // Drop the domain suffix (.company.com), but keep IP-looking names whole
        int dot = h.IndexOf('.');
        if (dot > 0 && !char.IsDigit(h[0])) h = h[..dot];

        return h.ToUpperInvariant();
    }

    public static bool Same(string? a, string? b)
    {
        var ka = Of(a);
        return ka.Length > 0 && ka == Of(b);
    }
}
