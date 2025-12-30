namespace Updatum;

/// <summary>
/// Represents the state of the Updatum process.
/// </summary>
public enum UpdatumState
{
    /// <summary>
    /// None
    /// </summary>
    None,

    /// <summary>
    /// Checking for updates
    /// </summary>
    CheckingForUpdate,

    /// <summary>
    /// Downloading update
    /// </summary>
    DownloadingUpdate,

    /// <summary>
    /// Installing update
    /// </summary>
    InstallingUpdate,
}

/// <summary>
/// Specifies the type of Windows executable in the assets (.exe) for update purposes.
/// </summary>
/// <remarks>Use this enumeration to indicate whether the update process should expect an installer, a single-file
/// executable, or automatically select the most appropriate type based on context. The selected value may affect
/// the upgrade experience. If your assets only include a singe type of executable it's better to select a strict value.</remarks>
public enum UpdatumWindowsExeType
{
    /// <summary>
    /// Specifies that the executable type should be determined automatically based on context or default logic.<br/>
    /// It will determine whether to use the executable as an installer package or a single-file executable based on the available assets.
    /// </summary>
    Auto,

    /// <summary>
    /// Indicates that the executable is an installer package, such as an MSI or EXE installer.
    /// </summary>
    /// <remarks>Use this type to identify or work with installer files that are intended for application
    /// installation or deployment. The specific format (MSI or EXE) may affect how the installer is executed or
    /// managed.</remarks>
    Installer,

    /// <summary>
    /// Indicates that the executable is a single-file application that does not require installation.
    /// </summary>
    /// <remarks>Use this type to identify or work with standalone executables that can be run directly.</remarks>
    SingleFileApp
}

/// <summary>
/// Specifies the strategy used to determine the name of the single-file executable when creating or updating an
/// application package.
/// </summary>
/// <remarks>Use this enumeration to control how the single-file executable is named during deployment or update
/// operations. The selected strategy affects how users and systems identify and launch the application after
/// installation.</remarks>
public enum UpdatumSingleFileExecutableNameStrategy
{
    /// <summary>
    /// Uses the same entry application's name <see cref="EntryApplication.ExecutableName"/> as the single-file executable name.<br/>
    /// If the entry application's name is not available, it will fall back to using the custom specified name,
    /// and if that is also unavailable, it will use the download file name from the release asset.
    /// </summary>
    /// <remarks>It handles and upgrades version numbers if present in name</remarks>
    EntryApplicationName,
    /// <summary>
    /// Uses a custom specified name <see cref="UpdatumManager.InstallUpdateSingleFileExecutableName"/> for the single-file executable.<br/>
    /// If the custom name is not provided, it will fall back to using the entry application's name, and if that is also unavailable, it will use the download file name from the release asset.
    /// </summary>
    CustomName,
    /// <summary>
    /// Uses the download file name from the release asset as the single-file executable name.
    /// </summary>
    DownloadName
}