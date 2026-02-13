# GEMINI.md — Updatum Project Context

## What is this project?

Updatum is a lightweight, cross-platform C# library distributed as a NuGet package. It automates application updates using GitHub Releases — checking for new versions, retrieving changelogs, downloading assets with progress tracking, and auto-upgrading the running application across Windows, Linux, and macOS.

**Key facts:**
- Repository: https://github.com/sn4k3/Updatum
- Author: Tiago Conceição (PTRTECH)
- License: MIT
- Version: centrally defined in `Directory.Build.props` (`<Version>` element)
- Multi-target: net8.0, net9.0, net10.0
- Only NuGet dependency: Octokit (GitHub API client)

## Project structure

| Path | Description |
|---|---|
| `Updatum.slnx` | Solution file (XML-based slnx format) |
| `Directory.Build.props` | Shared version, metadata, signing, build configuration |
| `Updatum/Updatum.csproj` | Library project — multi-targets net8.0/net9.0/net10.0 |
| `Updatum/UpdatumManager.cs` | **Core class** (~2300 lines) — update check, download, install |
| `Updatum/EntryApplication.cs` | Static utility (~760 lines) — assembly info, OS/runtime/bundle detection |
| `Updatum/UpdatumDownloadedAsset.cs` | Record wrapping downloaded asset metadata |
| `Updatum/UpdatumEnums.cs` | Enums: `UpdatumState`, `UpdatumWindowsExeType`, `UpdatumSingleFileExecutableNameStrategy` |
| `Updatum/Extensions/ArchiveExtensions.cs` | ZIP extraction helpers |
| `Updatum/Extensions/GitHubExtensions.cs` | Octokit Release extension methods |
| `Updatum/Extensions/Utilities.cs` | Process launch, temp folders, installer detection, script escaping |
| `Updatum.FakeApp/Program.cs` | Complete usage example (console app targeting sn4k3/UVtools) |
| `.github/workflows/dotnet.yml` | CI: build + test on push/PR to main |
| `.github/workflows/release.yml` | Publish: pack → tag → NuGet push → GitHub Release |
| `CHANGELOG.md` | Version history — must be updated with each release |

## How to build

```bash
dotnet restore        # Restore dependencies
dotnet build          # Build the solution
dotnet test           # Run tests (currently no test project)
dotnet pack Updatum --configuration Release --output .  # Create NuGet package
dotnet run --project Updatum.FakeApp                    # Run example app
```

## Architecture

### UpdatumManager — the core class

Implements `INotifyPropertyChanged` and `IDisposable`. Uses Octokit's `GitHubClient` for API calls and a static `HttpClient` for downloads. Supports `SynchronizationContext` dispatching for UI thread events.

**Lifecycle flow:**
1. `CheckForUpdatesAsync()` — fetches releases, filters by version/platform/pre-release, populates `ReleasesAhead`
2. `GetChangelog()` — formats markdown changelog from `ReleasesAhead`
3. `DownloadUpdateAsync()` — downloads the compatible asset to a temp folder with progress reporting
4. `InstallUpdateAsync()` — handles zip extraction, single-file replacement, installer execution, or macOS bundle upgrade; creates platform-specific upgrade scripts (batch for Windows, bash for Linux/macOS) and terminates the process

**Key events:** `CheckForUpdateCompleted`, `UpdateFound`, `DownloadCompleted`, `InstallUpdateCompleted` (fired before process kill — use for saving state)

### EntryApplication

Static utility class with lazily cached properties for: assembly info, executable paths, OS detection, runtime identifiers, bundle type detection (AppImage, Flatpak, macOS bundle, .NET single-file).

### Platform-specific scripts

- **Windows:** Batch (`.bat`) — uses `Utilities.BatchSetValue()` for safe escaping
- **Linux/macOS:** Bash — uses `Utilities.BashAnsiCString()` for ANSI-C quoting
- **macOS:** Optional local codesigning with `codesign --force --deep -s -`
- **Linux AppImage:** Move + rename + `chmod 755`

## Coding conventions you MUST follow

### Style
- C# latest with **nullable reference types** enabled
- **File-scoped namespaces**: `namespace Updatum;`
- **XML doc comments** (`///`) on all public members — documentation file is generated
- `ConfigureAwait(false)` on **every** `await` in library code
- `INotifyPropertyChanged` via `RaiseAndSetIfChanged(ref _field, value)` for bindable properties
- `#region` blocks for code organization: Events, Constants, Properties, Constructor, Methods

### Naming
- Properties, methods, constants: `PascalCase`
- Private fields: `_camelCase` prefix
- Follow existing code patterns (see `CONTRIBUTING.md`)

### Exceptions
- `ArgumentException` / `ArgumentNullException` → invalid method parameters
- `InvalidOperationException` → invalid object state (NEVER use `ArgumentOutOfRangeException` for property-level validation)
- `OperationCanceledException` → cancelled async operations

### Security
- Installer signature detection uses **XOR-obfuscated byte arrays** to prevent false positives in single-file apps — never add raw installer keyword strings to the codebase

### Visibility
- `Utilities` class is `public` under `#if DEBUG`, `internal` in Release — used for testability

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| `Octokit` | 14.0.0 | GitHub API client for release fetching |
| `Microsoft.SourceLink.GitHub` | 10.0.103 | Source Link for debugger integration |

The library intentionally has minimal dependencies.

## Release process

1. Update `<Version>` in `Directory.Build.props`
2. Add a new entry to `CHANGELOG.md`
3. Trigger the `release.yml` GitHub Actions workflow (manual dispatch):
   - Packs the NuGet package
   - Creates a Git tag
   - Pushes to nuget.org and GitHub Packages
   - Creates a GitHub Release with the changelog body
   - Supports dry-run mode
