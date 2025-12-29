namespace Updatum;

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