using System;
using Octokit;

namespace Updatum.Extensions;

internal static class GitHubExtensions
{
    /// <summary>
    /// Gets the release tag version, excluding the v prefix if present.
    /// </summary>
    /// <param name="release"></param>
    /// <returns></returns>
    public static string GetTagVersionStr(this Release release)
    {
        return release.TagName[0] is 'v' or 'V'
            ? release.TagName[1..]
            : release.TagName;
    }

    /// <summary>
    /// Gets the release tag version
    /// </summary>
    /// <param name="release"></param>
    /// <returns></returns>
    public static Version? GetTagVersion(this Release release)
    {
        var versionStr = release.GetTagVersionStr();
        var index = versionStr.IndexOfAny(['-', '_']);
        if (index > 0) versionStr = versionStr[..index];
        _ = Version.TryParse(versionStr.AsSpan(), out var version);
        return version;
    }
}