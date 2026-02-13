# CLAUDE.md — Updatum Project Guide

## Project Overview

**Updatum** is a lightweight, cross-platform C# library (NuGet package) that automates application updates via **GitHub Releases**. It checks for new versions, retrieves changelogs, downloads assets with progress tracking, and can auto-upgrade the running application (portable, single-file, installer, AppImage, macOS bundle).

- **Repository:** <https://github.com/sn4k3/Updatum>
- **Author:** Tiago Conceição (PTRTECH)
- **License:** MIT
- **Current Version:** defined in `Directory.Build.props` (`<Version>`)

---

## Solution Structure

```
Updatum/
├── Updatum.slnx                    # Solution file (XML-based slnx format)
├── Directory.Build.props           # Shared build properties (version, metadata, signing)
├── Updatum.snk                     # Strong-name key for assembly signing
│
├── Updatum/                        # Main library project (NuGet package)
│   ├── Updatum.csproj              # Multi-targets: net8.0, net9.0, net10.0
│   ├── UpdatumManager.cs           # Core class — update check, download, install (~2300 lines)
│   ├── EntryApplication.cs         # Static utility class for entry assembly info (~760 lines)
│   ├── UpdatumDownloadedAsset.cs   # Record for downloaded asset metadata
│   ├── UpdatumEnums.cs             # Enums: UpdatumState, UpdatumWindowsExeType, UpdatumSingleFileExecutableNameStrategy
│   └── Extensions/
│       ├── ArchiveExtensions.cs    # ZIP extraction helpers
│       ├── GitHubExtensions.cs     # Octokit Release extension methods
│       └── Utilities.cs           # Process launch, temp folders, installer detection, script helpers
│
├── Updatum.FakeApp/                # Example/test console app (net10.0)
│   └── Program.cs                  # Complete usage example against sn4k3/UVtools
│
├── .github/workflows/
│   ├── dotnet.yml                  # CI: build + test on push/PR to main
│   └── release.yml                 # Publish: pack → tag → push NuGet + GitHub Packages → create Release
│
├── CHANGELOG.md                    # Version history
├── README.md                       # Full documentation with usage examples and FAQs
├── CONTRIBUTING.md                 # Contribution guidelines
├── CODE_OF_CONDUCT.md
└── LICENSE                         # MIT
```

---

## Build & Run

### Prerequisites

- **.NET SDK** 8.0+ (project multi-targets net8.0 / net9.0 / net10.0).
- The CI workflow uses .NET 10.0.

### Commands

```bash
# Restore dependencies
dotnet restore

# Build the entire solution
dotnet build

# Run tests (currently no test project, but wired in CI)
dotnet test

# Pack the NuGet package (Release config)
dotnet pack Updatum --configuration Release --output .

# Run the example app
dotnet run --project Updatum.FakeApp
```

> **Note:** There is no test project yet. `dotnet test` is a no-op but is expected by CI.

---

## Architecture & Key Concepts

### `UpdatumManager` (core class)

- Implements `INotifyPropertyChanged` and `IDisposable`.
- Uses **Octokit** (`GitHubClient`) for GitHub API calls.
- Uses a static `HttpClient` for asset downloads (with proper User-Agent).
- Supports `SynchronizationContext` dispatching for UI thread events (`EventSynchronizationContext`).
- Constructors accept `owner/repository`, repository URL, or auto-infer from assembly `RepositoryUrl` metadata.

#### Lifecycle

1. **Check** → `CheckForUpdatesAsync()` fetches releases, filters by version/platform/pre-release, populates `ReleasesAhead`.
2. **Changelog** → `GetChangelog()` formats markdown from `ReleasesAhead`.
3. **Download** → `DownloadUpdateAsync()` downloads the compatible asset to temp, reports progress via `DownloadedBytes`/`DownloadedPercentage`.
4. **Install** → `InstallUpdateAsync()` handles zip extraction, single-file replacement, installer execution, or macOS bundle upgrade. Creates platform-specific upgrade scripts (batch/bash) and terminates the running process.

#### Key properties for configuration

| Property | Purpose |
|---|---|
| `AssetRegexPattern` | Regex to match release assets (default: `EntryApplication.GenericRuntimeIdentifier`) |
| `AssetExtensionFilter` | Extension filter for disambiguation (e.g., `.msi`) |
| `FetchOnlyLatestRelease` | Fetch only latest vs. all releases |
| `AllowPreReleases` | Include pre-release versions |
| `InstallUpdateWindowsExeType` | `Auto`, `Installer`, or `SingleFileApp` |
| `InstallUpdateWindowsInstallerArguments` | Arguments for MSI/EXE installer (e.g., `/qb`) |
| `InstallUpdateSingleFileExecutableNameStrategy` | How to name the single-file exe after upgrade |
| `InstallUpdateCodesignMacOSApp` | Locally codesign macOS .app bundles |
| `AutoUpdateCheckTimer` | Built-in timer for periodic checks (default: 12h) |

#### Events

- `CheckForUpdateCompleted` — fired after every check.
- `UpdateFound` — fired when `ReleasesAhead` is non-empty.
- `DownloadCompleted` — fired after successful download.
- `InstallUpdateCompleted` — fired *before* process termination (save state here).

### `EntryApplication` (static utility)

- Lazily cached properties for the running application: assembly info, paths, OS detection, runtime identifiers, bundle type (AppImage, Flatpak, macOS bundle, .NET single-file).

### `UpdatumDownloadedAsset` (record)

- Wraps `Release`, `ReleaseAsset`, and `FilePath`. Provides helpers like `TagVersionStr`, `FileExists`, `SafeDeleteFile()`.

---

## Coding Conventions

- **C# latest** with nullable reference types enabled (`<Nullable>enable</Nullable>`).
- **File-scoped namespaces** (`namespace Updatum;`).
- XML documentation comments (`///`) on all public members — the library generates a documentation file.
- `INotifyPropertyChanged` pattern with `RaiseAndSetIfChanged` helper for all bindable properties.
- `ConfigureAwait(false)` on all awaited calls inside the library.
- `#region` blocks to organize code sections (Events, Constants, Properties, Constructor, Methods).
- Internal `Utilities` class is `public` under `#if DEBUG` for testability.
- Installer signature detection uses XOR-obfuscated byte arrays to avoid false positives in single-file apps.

### Naming

- Properties: PascalCase.
- Private fields: `_camelCase` prefix.
- Constants: PascalCase.
- Follow existing code style, semantics, and naming patterns (see `CONTRIBUTING.md`).

### Exception conventions

- Use `ArgumentException` / `ArgumentNullException` for invalid method parameters.
- Use `InvalidOperationException` for invalid object state (not `ArgumentOutOfRangeException` for property values).
- Use `OperationCanceledException` for cancellation.

---

## Dependencies

| Package | Purpose |
|---|---|
| `Octokit` (14.0.0) | GitHub API client for fetching releases |
| `Microsoft.SourceLink.GitHub` (10.0.103) | Source Link for debugger integration |

No other external dependencies — the library is intentionally lightweight.

---

## Versioning & Release Process

1. Version is centrally defined in `Directory.Build.props` → `<Version>`.
2. Update `CHANGELOG.md` with the new version entry.
3. The `release.yml` workflow (manual dispatch):
   - Packs the NuGet package.
   - Creates a Git tag.
   - Pushes to **nuget.org** and **GitHub Packages**.
   - Creates a **GitHub Release** with changelog body.
   - Supports dry-run mode.

---

## Platform-Specific Behavior

The auto-updater generates platform-specific upgrade scripts:

- **Windows:** Batch (`.bat`) scripts — uses `BatchSetValue()` for safe escaping.
- **Linux/macOS:** Bash scripts — uses `BashAnsiCString()` for ANSI-C quoting.
- **macOS:** Optional local codesigning with `codesign --force --deep -s -`.
- **Linux AppImage:** Moves and renames the AppImage, sets `chmod 755`.

---

## Important Files to Know

| File | Why it matters |
|---|---|
| `Directory.Build.props` | Central version, metadata, signing, and build configuration |
| `UpdatumManager.cs` | The entire library logic in one file (~2300 lines) |
| `EntryApplication.cs` | OS/runtime/bundle detection logic |
| `Extensions/Utilities.cs` | Installer detection, script helpers, process utilities |
| `Updatum.FakeApp/Program.cs` | Reference implementation / usage example |
| `CHANGELOG.md` | Must be updated with every release |
