# v1.3.2 (30/12/2025)
- Add `InstallUpdateSingleFileExecutableName` property to specify the name of the single file executable when installing updates for single file applications (#6)
- Fix possible path bug when using `InstallUpdateSingleFileExecutableName` with `{0}` when required and trying to create base directory
- Set and flag the auto updater script for single file applications on Linux as executable
- Improve the upgrade script for deleting files, now using functions for better code reuse
- Improve process start for the upgrade scripts

# v1.3.1 (29/12/2025)
- Add `InstallUpdateWindowsExeType` property to strict indicate the type of .exe (Auto, Installer, SingleFileApp) (#6)
- Only remove the self upgrade script on Release builds
- Fix unable to remove the self upgrade script under windows
- Fix and encode the raw strings of setup signatures to not show under single-file apps

# v1.3.0 (28/12/2025)
- Add `GetApplicationInfoString` and `GetApplicationInfoDict` methods to `EntryApplication`
- Add `ProcessId` property to `EntryApplication`
- Add disposal pattern to `UpdatumManager` to properly dispose the internal timer
- Add `CheckForUpdatesAsync(Version)` overload to check for updates against a specific version
- Add `ConfigureAwait(false)` to all awaited calls
- Improvements and rewrites on upgrade script (#6)
- Improvements on better detection of .exe installer types on Windows to avoid false positives as single app file executables (#6)
- Changed `EntryApplication` to lazy cache all valeus instead of querying every time
- Fix the windows batch script when attempt to move directory (fail) because of space in argument `/ Y`
- Fix macOS `EntryApplication.MacOSAppBundlePath` due inverted if condition
- Fix the `EntryApplication.LaunchNewInstance` method for macOS, which was failing to launch in case of a App Bundle

# v1.2.1 (28/11/2025)
- Fixes the auto upgrade for single file executables contained on the zip files, which was replacing the program with the downloaded zip file instead of the extracted executable

# v1.2.0 (16/11/2025)
- Add NET 10 support

# v1.1.6 (28/07/2025)
- Improves the regex for extracting the version from file name to support more complex version formats, such as `1.2.3-alpha5`, `1.2.3-beta`, supported terms: dev|alpha|beta|preview|rc|nightly|canary
- Fixes the rename of single file executable and directories when containing the version in the name, it was keeping the last version digit appended to the new version

# v1.1.5 (19/07/2025)
- Improves the `InstallUpdate` for windows installers, by generating and run a upgrade batch script, which in the end calls the installer with the provided arguments (same as before), but also provides more validation checks, better process termination, custom script injection and clean up
- Improve the documentation for the `InstallUpdateCompleted` event
- Fixes the `InstallUpdateCompleted` event being triggered after executing the installer on Windows, when it should trigger before executing the installer
- Fixes the auto upgrade bat script for windows which had a leftover `pause` in the end of the script, which was causing the script to wait for user input before closing and then leaving the process open

# v1.1.4 (19/07/2025)
- Changes the `DownloadProgressUpdateFrequencySeconds` default from `0.5s` to `0.1s` to a more fluid progress update
- Improve the accuracy of download progress frequency check by using `StopWatch.GetElapsedTime()` instead of `DateTime`
- Improve the `GetCompatibleReleaseAsset` method when found multiple matching assets, instead of return the very first, it will now try to infer based on `EntryApplication` bundle type, which now searches and defaults to:
  - Windows: 
    - `.exe` if running under single-file (`PublishSingleFile`)
    - Otherwise, defaults to `.msi`
  - Linux: 
    - `AppImage` if running under AppImage
    - `Flatpak` if running under Flatpak
    - Otherwise, defaults to `.zip`
  - If none of the above matches, it will fallback to the first matching asset

# v1.1.3 (28/06/2025)
- Fixes the `AssemblyAuthors` to use the correct entry assembly reference

# v1.1.2 (30/05/2025)
- Add `AssemblyAuthors` property
- Sets the progress of `DownloadedBytes` to 0 when operation is cancelled (Fixes #4)

# v1.1.1 (24/05/2025)
- Improve the macOS codesign to add the `--deep` flag
- Add `AssemblyVersionString` property
- Transform all `EntryApplication` members into properties

# v1.1.0 (18/05/2025)
- Rename the `Updatum` class to `UpdatumManager` to simplify the name and avoid the requirement for fully qualifying name (#2)
- Add a new constructor to pass the repository url, if null or empty it will try to infer from the RepositoryUrl attribute (#3)
- Add more useful properties to the `EntryApplication` class
- Add Linux flatpak support

# v1.0.1 (11/05/2025)
- Fixes the lack of regex pattern when using full constructor
- Fixes the ForceTriggerUpdateFromRelease not triggering the UpdateFound event
- Improve AutoUpdateCheckTimer by only initialize it when accessed

# v1.0.0 (10/05/2025)
- First release of the project.