# Copilot Instructions — Updatum

## Project Summary

Updatum is a lightweight, cross-platform C# NuGet library that automates application updates via GitHub Releases using the Octokit library. It supports checking for updates, downloading assets with progress, and auto-upgrading (portable, single-file, installer, AppImage, macOS bundle).

## Tech Stack

- **Language:** C# (latest version features)
- **Frameworks:** .NET 8.0 / 9.0 / 10.0 (multi-target)
- **GitHub API:** Octokit 14.0.0
- **Build:** MSBuild with `Directory.Build.props` for shared configuration
- **CI/CD:** GitHub Actions (build on push/PR, NuGet publish on manual dispatch)
- **Solution format:** `.slnx` (XML-based)

## Solution Layout

- `Updatum/` — Main library project (NuGet package)
  - `UpdatumManager.cs` — Core class (~2300 lines): update check, download, install logic
  - `EntryApplication.cs` — Static utility for entry assembly/platform detection
  - `UpdatumDownloadedAsset.cs` — Record wrapping downloaded asset info
  - `UpdatumEnums.cs` — `UpdatumState`, `UpdatumWindowsExeType`, `UpdatumSingleFileExecutableNameStrategy`
  - `Extensions/` — `ArchiveExtensions.cs`, `GitHubExtensions.cs`, `Utilities.cs`
- `Updatum.FakeApp/` — Example console app demonstrating full usage
- `Directory.Build.props` — Central version (`<Version>`), metadata, signing, build config

## Coding Style Rules

1. **Nullable reference types** are enabled — always handle nullability properly.
2. **File-scoped namespaces** — use `namespace Updatum;` (not block-scoped).
3. **XML doc comments** (`///`) are required on all public members — the project generates documentation files.
4. **`ConfigureAwait(false)`** on every `await` inside the library.
5. **`INotifyPropertyChanged`** pattern — use `RaiseAndSetIfChanged(ref field, value)` for all bindable properties.
6. **Naming:**
   - Properties/Methods/Constants: `PascalCase`
   - Private fields: `_camelCase`
   - Follow the existing code patterns strictly
7. **`#region` blocks** — maintain existing region organization (Events, Constants, Properties, Constructor, Methods).
8. **Exception usage:**
   - `ArgumentException` / `ArgumentNullException` for invalid method parameters
   - `InvalidOperationException` for invalid object state
   - `OperationCanceledException` for cancelled operations
   - Never use `ArgumentOutOfRangeException` for property value validation
9. **Installer signature bytes** are XOR-obfuscated — do not add raw installer keyword strings to avoid false positives in single-file apps.

## Build Commands

```bash
dotnet restore
dotnet build
dotnet test
dotnet pack Updatum --configuration Release --output .
dotnet run --project Updatum.FakeApp
```

## Key Architecture

`UpdatumManager` lifecycle:
1. `CheckForUpdatesAsync()` → fetches releases, filters, populates `ReleasesAhead`
2. `GetChangelog()` → formats markdown changelog
3. `DownloadUpdateAsync()` → downloads asset to temp with progress reporting
4. `InstallUpdateAsync()` → extracts/replaces/installs, creates platform scripts, terminates process

Events: `CheckForUpdateCompleted`, `UpdateFound`, `DownloadCompleted`, `InstallUpdateCompleted`

## Dependencies

Only two NuGet dependencies (intentionally lightweight):
- `Octokit` — GitHub API
- `Microsoft.SourceLink.GitHub` — Source Link

## Release Workflow

1. Bump `<Version>` in `Directory.Build.props`
2. Add entry to `CHANGELOG.md`
3. Trigger `release.yml` workflow (creates tag, publishes NuGet, creates GitHub Release)
