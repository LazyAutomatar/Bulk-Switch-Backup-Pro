using BulkBackup.Core.Services;

namespace BulkBackup.Tests;

public sealed class HostKeyTests
{
    [Theory]
    [InlineData(" access-01.example.test ", "ACCESS-01")]
    [InlineData("access-01(FOC1234ABCD)", "ACCESS-01")]
    [InlineData("192.0.2.1", "192.0.2.1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizes_discovery_identifiers(string? value, string expected) => Assert.Equal(expected, HostKey.Of(value));

    [Theory]
    [InlineData("access-01.example.test", "ACCESS-01", true)]
    [InlineData("access-01", "access-02", false)]
    [InlineData(null, "", false)]
    public void Matches_only_nonempty_canonical_names(string? first, string? second, bool expected) => Assert.Equal(expected, HostKey.Same(first, second));
}
