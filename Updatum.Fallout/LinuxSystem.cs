using System;
using Fallout.Common.Tooling;

namespace Updatum.Fallout;

/// <summary>
/// Provides information about the Linux system environment.
/// </summary>
public static class LinuxSystem
{
    private static bool? _isFuseAvailable;

    /// <summary>
    /// Determines whether the FUSE 2 library required by traditional AppImage mounting is available.
    /// </summary>
    /// <remarks>
    /// The result is diagnostic only: appimagetool is always extracted before use, so a missing FUSE 2 is
    /// reported but never blocks a build.
    /// </remarks>
    /// <returns><see langword="true"/> when FUSE 2 is available; otherwise, <see langword="false"/>.</returns>
    public static bool IsFuseAvailable
    {
        get
        {
            if (_isFuseAvailable is null)
            {
                if (!OperatingSystem.IsLinux())
                {
                    _isFuseAvailable = false;
                }
                else
                {
                    try
                    {
                        using var result = ProcessTasks.StartShell("ldconfig -p | grep libfuse.so.2", timeout: 2000);
                        result.WaitForExit();
                        _isFuseAvailable = result.ExitCode == 0;
                    }
                    catch
                    {
                        _isFuseAvailable = false;
                    }
                }
            }

            return _isFuseAvailable.GetValueOrDefault();
        }
    }
}