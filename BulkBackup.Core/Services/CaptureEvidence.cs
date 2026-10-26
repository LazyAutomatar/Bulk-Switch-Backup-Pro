using System.Diagnostics;
using System.Text.RegularExpressions;
using BulkBackup.Core.Models;

namespace BulkBackup.Core.Services;

internal sealed class IncompleteCommandException(string message, string output, string outcome) : TimeoutException(message)
{
    public string Output { get; } = output;
    public string Outcome { get; } = outcome;
}

internal static class CaptureEvidence
{
    public static async Task<string> ExecuteAsync(CommandCapture capture, string command, Func<Task<string>> execute)
    {
        var start = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        string output = "";
        Exception? error = null;
        try { output = await execute(); return output; }
        catch (Exception ex)
        {
            error = ex;
            if (ex is IncompleteCommandException incomplete) output = incomplete.Output;
            throw;
        }
        finally { Record(capture, command, output, error, start, timer.Elapsed.TotalMilliseconds); }
    }

    internal static void Record(CommandCapture capture, string command, string output, Exception? error,
        DateTimeOffset start, double duration)
    {
        string body = DevicePlatforms.Body(output, command);
        bool truncated = Regex.IsMatch(output, @"(?:\[.*truncat.*\]|output\s+(?:was\s+)?truncated)", RegexOptions.IgnoreCase);
        string outcome = error switch
        {
            IncompleteCommandException incomplete => incomplete.Outcome,
            OperationCanceledException => "cancelled",
            TimeoutException => "timedOut",
            not null => "failed",
            _ when Regex.IsMatch(body, @"authorization failed|not authorized|permission denied|access denied|insufficient privilege", RegexOptions.IgnoreCase) => "unauthorized",
            _ when DevicePlatforms.IsUnsupported(body) => "unsupported",
            _ when DevicePlatforms.IsRejected(body) => "failed",
            _ when string.IsNullOrWhiteSpace(body) => "empty",
            _ when truncated => "truncated",
            _ => "accepted"
        };
        // A recognized prompt, non-empty body and no truncation/error marker are all required.
        bool missingEnd = capture.CompletionCheck == "promptAndEndMarker" &&
            !Regex.IsMatch(body, @"(?:^|\n)end\s*$", RegexOptions.IgnoreCase);
        string completeness = outcome == "accepted" && !missingEnd ? "complete" : "incomplete";
        string status = completeness == "complete" ? "success" : outcome == "unsupported" ? "unsupported" : "failure";
        string? reason = error == null ? null : error.GetType().Name + ": " + error.Message;
        if (error == null) reason = outcome == "accepted" ? null : outcome == "empty" ? "Empty command response." : body;
        if (outcome == "accepted" && missingEnd) reason = "Prompt returned, but the expected configuration end marker was absent.";
        if (reason?.Length > 1000) reason = reason[..1000];
        var end = DateTimeOffset.UtcNow;
        capture.RequestedCommand = capture.Command;
        capture.CanonicalCommand = AssessmentProfiles.Canonical(capture.Command);
        capture.StartedAtUtc ??= start;
        capture.CompletedAtUtc = end;
        capture.DurationMilliseconds = (capture.DurationMilliseconds ?? 0) + duration;
        capture.Attempts.Add(new CommandAttempt(command, end, status, reason)
        {
            StartedAtUtc = start, CompletedAtUtc = end, DurationMilliseconds = duration,
            Outcome = outcome, Completeness = completeness, PromptReceived = error == null, RawOutput = output
        });
        capture.Status = status;
        capture.Outcome = outcome;
        capture.Completeness = completeness;
        capture.Reason = reason;
    }

    public static void Identify(DeviceCapture device)
    {
        string Output(string command) => string.Join("\n", device.Commands.Where(c => c.Command == command)
            .SelectMany(c => c.Attempts).Where(a => a.Outcome == "accepted").Select(a => a.RawOutput));
        string version = Output("show version");
        string inventory = Output("show inventory");
        string evidence = version + "\n" + device.IdentityEvidence;
        string? Match(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        device.Family = device.DeviceType?.ToLowerInvariant();
        if (device.Platform == "ios" && Regex.IsMatch(evidence, @"\b(?:ISR\d+|ASR\d+|C8[23]00|C11\d\d|CISCO(?:18|19|28|29|38|39)\d\d)[\w-]*\b", RegexOptions.IgnoreCase))
            device.Family = "router";
        device.SerialNumber = Match(evidence, @"(?:Processor board ID|(?:System|Chassis) Serial (?:Number|Num))\s*(?:[:=.]+\s*)?(\S+)")
            ?? Match(inventory, @"\bSN:\s*([\w-]+)");
        device.Model = Match(inventory, @"\bPID:\s*([^,\r\n]+)")
            ?? Match(evidence, @"Model [Nn]umber\s*[:=.]+\s*([\w-]+)")
            ?? Match(evidence, @"\b((?:C\d{4}|WS-C\d+|AIR-(?:AP|CAP)|ISR\d+|ASR\d+)[\w-]*)\b");
        device.ChassisId = Match(evidence, @"(?:Base [Ee]thernet MAC [Aa]ddress|Base MAC [Aa]ddress|Burned-in MAC Address)\s*[:=.]+\s*([\w:.-]+)");
        device.Role = Match(Output("show redundancy") + "\n" + Output("show redundancy summary"),
            @"(?:Local State|Current Software state|My State)\s*(?:[=:]|\.+)\s*(?:\d+\s*-\s*)?(ACTIVE|STANDBY(?: HOT| COLD)?)\b")?.ToLowerInvariant();
        device.DeviceId = "management-ip:" + device.IpAddress.ToLowerInvariant();
        device.IdentityBasis = "managementIp"; // Never collapse paired controllers or stack members by hostname.
        // Chassis inventory entries describe physical members independently of the shared hostname.
        foreach (Match m in Regex.Matches(inventory,
            @"NAME:\s*""(?:Switch|Chassis)\s+(\d+)""[^\r\n]*[\s\S]*?PID:[^\r\n]*SN:\s*([\w-]+)", RegexOptions.IgnoreCase))
        {
            if (!device.Members.Any(x => x.MemberId == m.Groups[1].Value))
                device.Members.Add(new DeviceMember(m.Groups[1].Value, null, m.Groups[2].Value, null));
        }
        foreach (Match m in Regex.Matches(Output("show switch detail"),
            @"^\s*\*?(\d+)\s+(Active|Standby|Member|Master)\s+([\da-f.:-]+)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            var member = new DeviceMember(m.Groups[1].Value, m.Groups[2].Value.ToLowerInvariant(), null, m.Groups[3].Value);
            int index = device.Members.FindIndex(x => x.MemberId == member.MemberId);
            if (index < 0) device.Members.Add(member);
            else device.Members[index] = member with { SerialNumber = device.Members[index].SerialNumber };
        }
    }
}
