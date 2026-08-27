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
    /// <remarks>
    /// Windows has no Unix mode bits, so the call is a no-op there. Bundle creation that depends on the
    /// execute bit is already gated on a matching host, and a cross-built payload staged on Windows simply
    /// carries no mode to set.
    /// </remarks>
    public static void SetUnixExecutable(AbsolutePath executablePath)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(executablePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}