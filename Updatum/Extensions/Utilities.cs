using System.Diagnostics;
using System.Threading;
using System;
using System.IO;

namespace Updatum.Extensions;

internal static class Utilities
{
    /// <summary>
    /// Gets the unix file mode for 775 permissions.
    /// </summary>
    public const UnixFileMode Unix755FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute  |
                                                UnixFileMode.GroupRead                         | UnixFileMode.GroupExecute |
                                                UnixFileMode.OtherRead                         | UnixFileMode.OtherExecute;
    /// <summary>
    /// Gets the default applications directory for linux.
    /// </summary>
    public static string LinuxDefaultApplicationDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");

    /// <summary>
    /// Gets the default applications directory for macOS.
    /// </summary>
    public const string MacOSDefaultApplicationDirectory = "/Applications";

    /// <summary>
    /// Gets the default applications directory that is common to all systems if the others are not available.
    /// </summary>
    public static string CommonDefaultApplicationDirectory => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// Starts a process with the given name and arguments.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="arguments"></param>
    /// <param name="waitForCompletion"></param>
    /// <param name="waitTimeout"></param>
    public static void StartProcess(string name, string? arguments, bool waitForCompletion = false, int waitTimeout = Timeout.Infinite)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(name, arguments ?? string.Empty) { UseShellExecute = true });
            if (process is null) return;
            if (waitForCompletion) process.WaitForExit(waitTimeout);
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }

    /// <summary>
    /// Gets a temporary folder with the given name.
    /// </summary>
    /// <param name="name">Child folder name</param>
    /// <param name="init">True to delete existence directory and create a new.</param>
    public static string GetTemporaryFolder(string name, bool init = false)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), name);

        // Delete if it was a file
        if (File.Exists(name))
            File.Delete(name);

        if (init)
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, true); // Delete existing directory

            Directory.CreateDirectory(tmpDir); // Create new directory
        }

        return tmpDir;
    }
}