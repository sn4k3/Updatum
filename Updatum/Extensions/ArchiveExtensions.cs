using System.IO.Compression;

namespace Updatum.Extensions;

/// <summary>
/// Extensions for <see cref="ZipArchiveEntry"/>.
/// </summary>
internal static class ArchiveExtensions
{
    /// <summary>
    /// Checks if the entry is a file.
    /// </summary>
    /// <param name="entry"></param>
    /// <returns>True if is a file, otherwise false.</returns>
    public static bool IsFile(this ZipArchiveEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Name)
            && !entry.FullName.EndsWith('\\')
            && !entry.FullName.EndsWith('/');
    }
}