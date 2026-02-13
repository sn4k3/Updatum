# AGENTS.md — Updatum

## Overview

Updatum is a lightweight, cross-platform C# NuGet library that automates application updates using GitHub Releases. It provides update checking, changelog retrieval, asset downloading with progress, and auto-upgrade support for portable apps, single-file executables, installers, Linux AppImage, and macOS app bundles.

- **Repository:** https://github.com/sn4k3/Updatum
- **License:** MIT
- **Author:** Tiago Conceição (PTRTECH)

## Setup

### Prerequisites

- .NET SDK 8.0 or later (the project multi-targets net8.0, net9.0, net10.0)

### Build & Test

```bash
dotnet restore
dotnet build
dotnet test
```

### Pack & Run

```bash
dotnet pack Updatum --configuration Release --output .
dotnet run --project Updatum.FakeApp
```

## Structure

```
Updatum.slnx                        # Solution (XML-based slnx format)
Directory.Build.props               # Central version, metadata, signing config

Updatum/                            # Library project (NuGet package)
  Updatum.csproj                    # Multi-targets: net8.0;net9.0;net10.0
  UpdatumManager.cs                 # Core class: check, download, install (~2300 lines)
  EntryApplication.cs               # Entry assembly and platform detection (~760 lines)
  UpdatumDownloadedAsset.cs         # Downloaded asset record
  UpdatumEnums.cs                   # UpdatumState, UpdatumWindowsExeType, UpdatumSingleFileExecutableNameStrategy
  Extensions/
    ArchiveExtensions.cs            # ZIP helpers
    GitHubExtensions.cs             # Octokit Release extensions
    Utilities.cs                    # Process, temp folder, installer detection, script helpers

Updatum.FakeApp/                    # Example console app
  Program.cs                        # Full usage example against sn4k3/UVtools

.github/workflows/
  dotnet.yml                        # CI: build + test
  release.yml                       # Publish: NuGet + GitHub Release
```

## Conventions

### Code Style

- **C# latest** with `<Nullable>enable</Nullable>`
- **File-scoped namespaces**: `namespace Updatum;`
- **XML doc comments** (`///`) required on all public members (documentation file is generated)
- **`ConfigureAwait(false)`** on every `await` in library code
- **`INotifyPropertyChanged`** pattern using `RaiseAndSetIfChanged(ref field, value)`
- **`#region`** blocks for organization: Events, Constants, Properties, Constructor, Methods
- **Naming**: Properties/Methods/Constants → `PascalCase`, private fields → `_camelCase`
- **Internal debug visibility**: `Utilities` class is `public` under `#if DEBUG`

### Exception Patterns

- `ArgumentException` / `ArgumentNullException` → invalid method parameters
- `InvalidOperationException` → invalid object state (never `ArgumentOutOfRangeException` for property values)
- `OperationCanceledException` → cancelled async operations

### Security

- Installer signature detection uses XOR-obfuscated byte arrays to prevent false positives when the library is embedded in single-file apps. Do not add raw installer keyword strings.

## Architecture

### UpdatumManager Lifecycle

1. **Check**: `CheckForUpdatesAsync()` — Fetches releases via Octokit, filters by version/platform/pre-release, populates `ReleasesAhead`
2. **Changelog**: `GetChangelog()` — Formats markdown changelog from releases ahead
3. **Download**: `DownloadUpdateAsync()` — Downloads compatible asset to temp with progress tracking (`DownloadedBytes`, `DownloadedPercentage`)
4. **Install**: `InstallUpdateAsync()` — Handles zip extraction, single-file replacement, installer execution, macOS bundle upgrade. Generates platform-specific upgrade scripts and terminates the running process.

### Events

| Event | When |
|---|---|
| `CheckForUpdateCompleted` | After every update check |
| `UpdateFound` | When `ReleasesAhead` is non-empty |
| `DownloadCompleted` | After successful asset download |
| `InstallUpdateCompleted` | Before process termination (save state here) |

### Platform Scripts

- **Windows**: Batch `.bat` scripts (escaped via `BatchSetValue()`)
- **Linux/macOS**: Bash scripts (escaped via `BashAnsiCString()`)

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| Octokit | 14.0.0 | GitHub API client |
| Microsoft.SourceLink.GitHub | 10.0.103 | Source Link debugger integration |

## Versioning

1. Version lives in `Directory.Build.props` → `<Version>` tag
2. Every release needs a `CHANGELOG.md` entry
3. Release workflow (`release.yml`): pack → git tag → push NuGet → create GitHub Release
