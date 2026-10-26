using System.Text.RegularExpressions;

namespace BulkBackup.Core.Services;

public static class AssessmentProfiles
{
    // Network Health Reporter Commands screen, pinned at 5c87329a2d896271b7cb4efee87296f8a396f3fc.
    public static readonly string[] Catalyst =
    [
        "show version", "show running-config", "show inventory",
        "show processes cpu sorted", "show processes memory sorted",
        "show environment power", "show environment temperature",
        "show switch detail", "show switch stack-ring speed",
        "show spanning-tree summary", "show vtp status", "show interfaces counters errors",
        "show interfaces status", "show platform hardware fed switch active qos queue stats internal cpu policer",
        "show cdp neighbors detail", "show cdp neighbors", "show ip interface brief"
    ];

    public static bool SupportsStack(string identity) => Regex.IsMatch(identity,
        @"\b(?:C9200|C9300|WS-C(?:2960[XS]|3650|3750|3850))[\w-]*\b", RegexOptions.IgnoreCase);

    public static string[] For(DevicePlatform platform, string identity, bool assessment)
    {
        var commands = assessment && platform.Id == "ios" ? Catalyst : platform.Commands;
        return commands.Where(c => !c.StartsWith("show switch", StringComparison.OrdinalIgnoreCase) ||
            platform.Id == "ios" && SupportsStack(identity)).ToArray();
    }

    public static string[] CountersFor(DevicePlatform platform) => platform.Id switch
    {
        "ios" => ["show interfaces counters errors"],
        "nxos" => ["show interface counters errors"],
        _ => [] // Never substitute a different metric or guess a vendor dialect.
    };

    public static string Canonical(string command) => command.Trim().ToLowerInvariant() switch
    {
        "show run" or "sh run" => "show running-config",
        "sh ver" => "show version",
        "show int status" => "show interfaces status",
        _ => command.Trim().ToLowerInvariant()
    };
}
