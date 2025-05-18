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
    /// Gets the file name of the downloaded asset.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Gets the file name without extension of the downloaded asset.
    /// </summary>
    public string FileNameNoExt => Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>
    /// Gets the file extension of the downloaded asset.
    /// </summary>
    public string FileExtension => Path.GetExtension(FilePath);

    /// <summary>
    /// Perform a safe <see cref="FilePath"/> file deletion.
    /// </summary>
    public void SafeDeleteFile()
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