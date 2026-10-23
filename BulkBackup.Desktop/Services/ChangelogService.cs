using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BulkBackup.Desktop.Services;

public sealed record ChangelogEntry(string VersionTag, string ReleaseDate, string Notes);

public static class ChangelogService
{
    private static readonly Regex Heading = new(
        @"^## \[(?<version>v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)\](?:\s*-\s*(?<date>\d{4}-\d{2}-\d{2}))?\s*$",
        RegexOptions.Multiline);

    public static IReadOnlyList<ChangelogEntry> LoadInstalledHistory()
    {
        using var stream = typeof(ChangelogService).Assembly.GetManifestResourceStream("BulkBackup.Changelog.md")
            ?? throw new InvalidOperationException("The packaged release changelog is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var entries = new List<ChangelogEntry>();
        var matches = Heading.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            int start = match.Index + match.Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            // Stop at any next level-two heading, including an Unreleased section.
            var nextHeading = Regex.Match(text[start..end], @"^## ", RegexOptions.Multiline);
            if (nextHeading.Success) end = start + nextHeading.Index;
            entries.Add(new ChangelogEntry("v" + match.Groups["version"].Value.TrimStart('v'),
                match.Groups["date"].Value, text[start..end].Trim()));
        }
        return entries;
    }

    public static string NotesForVersion(string version, string notes)
    {
        string tag = "v" + version.Trim().TrimStart('v', 'V');
        foreach (var entry in Parse(notes))
            if (string.Equals(entry.VersionTag, tag, StringComparison.OrdinalIgnoreCase)) return entry.Notes;
        return string.IsNullOrWhiteSpace(notes) ? "Release notes were not provided for this update." : notes.Trim();
    }
}
