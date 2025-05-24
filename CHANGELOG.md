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