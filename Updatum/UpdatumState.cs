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