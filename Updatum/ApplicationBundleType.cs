namespace Updatum;

/// <summary>
/// Defines the application bundle type.
/// </summary>
public enum ApplicationBundleType
{
    /// <summary>
    /// The application bundle type is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// The application is not bundled and run under a raw folder.
    /// </summary>
    None,

    /// <summary>
    /// The application is bundled as a dotnet single-file application.
    /// </summary>
    DotNetSingleFile,

    /// <summary>
    /// The application is bundled as a Linux AppImage.
    /// </summary>
    LinuxAppImage,

    /// <summary>
    /// The application is bundled as a macOS app bundle.
    /// </summary>
    MacOSAppBundle,
}