# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Updatum** is a lightweight, cross-platform C# NuGet library that automates application updates via GitHub Releases (using Octokit). It checks for new versions, downloads assets with progress tracking, and auto-upgrades running applications (portable, single-file, installer, AppImage, macOS bundle).

- **Repository:** https://github.com/sn4k3/Updatum
- **Author:** Tiago Conceicao (PTRTECH)
- **License:** MIT
- **Version:** defined in `Directory.Build.props` (`<Version>`)

## Build Commands

```bash
dotnet restore                                          # Restore dependencies
dotnet build                                            # Build entire solution
dotnet run --project Updatum.FakeApp                    # Run example app
dotnet pack Updatum --configuration Release --output .  # Pack NuGet package
dotnet test Updatum.Fallout.Tests                        # Run the Fallout build-tooling unit tests
```

Requires .NET SDK 8.0+. Multi-targets net8.0/net9.0/net10.0 for the `Updatum` library itself; the build-tooling projects (`Updatum.Build`, `Updatum.Fallout`, `Updatum.Fallout.Tests`) target net10.0 only. CI uses .NET 10.0. Solution file is `Updatum.slnx` (XML-based `.slnx` format, not classic `.sln`).

### Custom build pipeline (Fallout)

`./build.cmd` / `./build.sh` / `./build.ps1` bootstrap a pinned .NET SDK and run **`Updatum.Build`**, a build automation script analogous to a Nuke build, but built on the author's own **Fallout.Common** package instead of Nuke. Targets are invoked positionally, e.g.:

```bash
./build.cmd Compile               # default target
./build.cmd Publish --configuration Release --r-ids win-x64 linux-x64
./build.cmd Print                 # dumps resolved build variables for debugging
```

`Updatum.Build/Build.cs` is the thin, repo-specific `Build : UpdatumBuild` entry point (`Updatum.Build.csproj`). `Updatum.Fallout` is the reusable class library holding the actual Updatum-specific build/publish/bundle logic (`UpdatumBuild` partial class, split by platform: `UpdatumBuild.cs`, `.Windows.cs`, `.Linux.cs`, `.MacOS.cs`, `.Publish.cs`). `Updatum.Fallout.Tests` is an xUnit v3 test project covering that logic (`InternalsVisibleTo` from `Updatum.Fallout`). It runs on Microsoft.Testing.Platform rather than VSTest, which the .NET 10 SDK removed: `global.json` carries the `"test": { "runner": "Microsoft.Testing.Platform" }` opt-in and the project references only `xunit.v3` (no `Microsoft.NET.Test.Sdk`, no VSTest adapter). Build state/logs land in `.fallout/` (gitignored working directory, analogous to Nuke's `.nuke/`).

Key `UpdatumBuild` targets: `Restore`, `Compile`, `Run`, `Publish`, `Clean`, `Print`. `Publish` publishes every configured RID (`RIds` parameter) and, unless `--publish-no-bundles`, creates bundles per `PublishBundles` flags (`DotNetSingleExecutable` zip, `WindowsInstaller`, `MacOSAppBundle`, `LinuxAppImage`) via `CreateBundles`/`CreatePortableZip`/platform-specific creators.

## Architecture

### `UpdatumManager` (core class, ~2260 lines)

The entire library logic lives in `Updatum/UpdatumManager.cs`. It is a `partial class` implementing `INotifyPropertyChanged` and `IDisposable`. The `partial` is used for `[GeneratedRegex]` source-generated regex members.

**Lifecycle:** CheckForUpdatesAsync() -> GetChangelog() -> DownloadUpdateAsync() -> InstallUpdateAsync()

1. **Check** fetches releases via Octokit, filters by version/platform/pre-release, populates `ReleasesAhead`.
2. **Changelog** formats markdown from `ReleasesAhead`.
3. **Download** downloads the matching asset to temp, reports progress via `DownloadedBytes`/`DownloadedPercentage`.
4. **Install** handles zip extraction, single-file replacement, installer execution, or macOS bundle upgrade. Creates platform-specific upgrade scripts (batch on Windows, bash on Linux/macOS) and terminates the running process.

Uses a static `HttpClient` for downloads. Supports `SynchronizationContext` dispatching for UI thread events via `EventSynchronizationContext`.

**Events:** `CheckForUpdateCompleted`, `UpdateFound`, `DownloadCompleted`, `InstallUpdateCompleted`.

### `EntryApplication` (from StageKit.Runtime)

`EntryApplication` is a static utility class provided by `StageKit.Runtime`. It exposes lazily cached properties (`Lazy<T>`) for the running application: assembly info, paths, OS detection, runtime identifiers, and bundle type detection (AppImage, Flatpak, macOS bundle, .NET single-file). `ApplicationBundleType` enum also lives in `StageKit.Runtime`.

### Supporting types

- `UpdatumDownloadedAsset.cs` — `record` wrapping Release, ReleaseAsset, and FilePath for a completed download.
- `UpdatumEnums.cs` — `UpdatumState`, `UpdatumWindowsExeType`, `UpdatumSingleFileExecutableNameStrategy`.
- `Extensions/Utilities.cs` — installer signature detection (XOR-obfuscated), script generation helpers (`BatchSetValue()`, `BashAnsiCString()`), process launch utilities. Public under `#if DEBUG` for testability.
- `Extensions/GitHubExtensions.cs` — internal Octokit `Release` extension methods (tag version parsing).
- `Extensions/ArchiveExtensions.cs` — internal `ZipArchiveEntry` helpers.

### Dependencies

The `Updatum` library is intentionally lightweight — three NuGet packages:
- `Octokit` — GitHub API client
- `StageKit.Runtime` — provides `EntryApplication` static utility and `ApplicationBundleType` enum
- `Microsoft.SourceLink.GitHub` — Source Link (build-time only)

Assembly is strong-named via `Updatum.snk`. The `Updatum.Fallout` build-tooling library depends separately on `Fallout.Common` — do not conflate this with the runtime library's dependency set above.

## Coding Conventions

- **C# latest** with nullable reference types enabled.
- **File-scoped namespaces** (`namespace Updatum;`).
- **XML doc comments** (`///`) on all public members — the library generates documentation files (`GenerateDocumentationFile`).
- **`ConfigureAwait(false)`** on every `await` inside the library.
- **`INotifyPropertyChanged`** pattern with `RaiseAndSetIfChanged(ref field, value)` for all bindable properties.
- **`#region` blocks** to organize code sections (Events, Constants, Properties, Constructor, Methods).
- **Naming:** Properties/Methods/Constants: `PascalCase`. Private fields: `_camelCase`.
- **Exceptions:** `ArgumentException`/`ArgumentNullException` for invalid parameters. `InvalidOperationException` for invalid object state (never `ArgumentOutOfRangeException` for property values). `OperationCanceledException` for cancellation.
- **Installer signature bytes** are XOR-obfuscated — never add raw installer keyword strings to source to avoid false positives in single-file apps.
- **Source-generated regex** — use `[GeneratedRegex]` on `partial` methods rather than `new Regex()`.
- Do not add new NuGet dependencies to the `Updatum` library without discussion; keep it lightweight.
- Follow existing code style, semantics, and naming patterns.

## Platform-Specific Behavior

The auto-updater generates platform-specific upgrade scripts:

- **Windows:** Batch (`.bat`) scripts via `BatchSetValue()` for safe escaping.
- **Linux/macOS:** Bash scripts via `BashAnsiCString()` for ANSI-C quoting.
- **macOS:** Optional local codesigning with `codesign --force --deep -s -`.
- **Linux AppImage:** Moves/renames the AppImage, sets `chmod 755`.

## Release Process

1. Bump `<Version>` in `Directory.Build.props`.
2. Add entry to `CHANGELOG.md`.
3. Trigger `release.yml` workflow (manual dispatch) — packs NuGet, creates git tag, publishes to nuget.org and GitHub Packages, creates GitHub Release. Supports dry-run mode.
