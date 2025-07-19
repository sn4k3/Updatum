# v1.1.4 (19/07/2025)
- Changes the `DownloadProgressUpdateFrequencySeconds` default from `0.5s` to `0.1s` to a more fluid progress update
- Improve the accuracy of download progress frequency check by using `StopWatch.GetElapsedTime()` instead of `DateTime`
- Improve the `GetCompatibleReleaseAsset` method when found multiple matching assets, instead of return the very first,
it will now try to infer based on `EntryApplication` bundle type, which now searches and defaults to:
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