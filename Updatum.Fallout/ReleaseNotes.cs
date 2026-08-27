using System;
using System.IO;
using Fallout.Common.IO;

namespace Updatum.Fallout;

/// <summary>
/// Extracts the most recent release section from a Keep a Changelog style changelog.
/// </summary>
internal static class ReleaseNotes
{
    internal static string? ExtractLatestReleaseNotes(string changelog)
    {
        ArgumentNullException.ThrowIfNull(changelog);

        var lines = changelog.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var releaseHeadingIndex = Array.FindIndex(lines, IsLevelTwoHeading);

        if (releaseHeadingIndex < 0)
            return null;

        var nextReleaseHeadingIndex = Array.FindIndex(lines, releaseHeadingIndex + 1, IsLevelTwoHeading);
        if (nextReleaseHeadingIndex < 0)
            nextReleaseHeadingIndex = lines.Length;

        var notesStartIndex = releaseHeadingIndex + 1;
        while (notesStartIndex < nextReleaseHeadingIndex && string.IsNullOrWhiteSpace(lines[notesStartIndex]))
            notesStartIndex++;

        return string.Join("\n", lines[notesStartIndex..nextReleaseHeadingIndex]).TrimEnd();
    }

    internal static void WriteLatestReleaseNotes(AbsolutePath changelogFile, AbsolutePath releaseNotesFile)
    {
        if (!File.Exists(changelogFile))
            return;

        var releaseNotes = ExtractLatestReleaseNotes(File.ReadAllText(changelogFile));
        if (releaseNotes is not null)
            File.WriteAllText(releaseNotesFile, releaseNotes);
    }

    private static bool IsLevelTwoHeading(string line)
    {
        return line.Length == 2 && line == "##" ||
               line.Length > 2 && line.StartsWith("##", StringComparison.Ordinal) && char.IsWhiteSpace(line[2]);
    }
}
