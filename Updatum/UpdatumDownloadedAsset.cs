using System.IO;
using Octokit;
using Updatum.Extensions;

namespace Updatum;

/// <summary>
/// Represents a download of a release asset.
/// </summary>
/// <param name="Release"></param>
/// <param name="ReleaseAsset"></param>
/// <param name="FilePath"></param>
public record UpdatumDownloadedAsset(Release Release, ReleaseAsset ReleaseAsset, string FilePath)
{
    /// <summary>
    /// Gets the release tag version, excluding the v prefix if present.
    /// </summary>
    public string TagVersionStr => Release.GetTagVersionStr();

    /// <summary>
    /// Checks if the downloaded file exists at the <see cref="FilePath"/> path.
    /// </summary>
    public bool FileExists => File.Exists(FilePath);

    /// <summary>
    /// Perform a safe <see cref="FilePath"/> file deletion.
    /// </summary>
    public void SafeFileDelete()
    {
        if (!FileExists) return;
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
            // Ignore file deletion errors
        }

    }
}