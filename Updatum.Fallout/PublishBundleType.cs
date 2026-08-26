using System;

namespace Updatum.Fallout;

/// <summary>
/// Specifies the type of bundle to publish.
/// </summary>
[Flags]
public enum PublishBundleTypes
{
    /// <summary>
    /// Publish the complete multi-file release as a portable ZIP archive.
    /// </summary>
    Portable = 1 << 0,

    /// <summary>
    /// Publish a .NET single executable.
    /// </summary>
    DotNetSingleExecutable = 1 << 1,

    /// <summary>
    /// Publish a Windows installer.
    /// </summary>
    WindowsInstaller = 1 << 2,

    /// <summary>
    /// Publish a Linux AppImage.
    /// </summary>
    LinuxAppImage = 1 << 3,

    /// <summary>
    /// Publish a macOS app bundle.
    /// </summary>
    MacOSAppBundle = 1 << 4
}
