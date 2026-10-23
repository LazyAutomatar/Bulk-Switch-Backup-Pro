namespace BulkBackup.Core.Models;

public sealed record CommandFailure(DateTimeOffset Time, string IpAddress, string Hostname,
    string Command, string Error, string Response);
