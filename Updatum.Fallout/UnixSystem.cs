using System;
using System.IO;
using Fallout.Common.IO;

namespace Updatum.Fallout;

/// <summary>
/// Provides Unix-specific system utilities.
/// </summary>
public static class UnixSystem
{
    /// <summary>
    /// Marks a Unix launcher as executable.
    /// </summary>
    /// <param name="executablePath">The launcher path.</param>
    public static void SetUnixExecutable(AbsolutePath executablePath)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Unix executable permissions are unavailable on Windows.");

        File.SetUnixFileMode(executablePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}