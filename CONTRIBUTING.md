# Contributing

Anyone is welcome to contribute to Updatum. This document covers everything you need to get started.

## Table of Contents

- [Getting Started](#getting-started)
- [Building](#building)
- [Code Guidelines](#code-guidelines)
- [Submitting Changes](#submitting-changes)
- [Reporting Bugs](#reporting-bugs)

---

## Getting Started

**Requirements:**
- [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
- Any IDE with C# support (Visual Studio 2022, Rider, VS Code + C# extension)

**Clone and restore:**
```bash
git clone https://github.com/sn4k3/Updatum.git
cd Updatum
dotnet restore
```

---

## Building

```bash
dotnet build                                            # Build the solution
dotnet run --project Updatum.FakeApp                    # Run the example app
dotnet pack Updatum --configuration Release --output .  # Pack the NuGet package
```

The solution file is `Updatum.slnx` (XML-based `.slnx` format). The library version is defined in `Directory.Build.props` under `<Version>`.

---

## Code Guidelines

### Style and conventions

- Use **C# latest** language features with nullable reference types enabled (`#nullable enable` is set globally).
- Use **file-scoped namespaces**: `namespace Updatum;`
- Use **`PascalCase`** for types, properties, methods, and constants; **`_camelCase`** for private fields.
- Use **`#region` blocks** to organise code sections (Events, Constants, Properties, Constructor, Methods).
- Use **source-generated regex** via `[GeneratedRegex]` on `partial` methods — never `new Regex(...)` for static patterns.
- Use **`ConfigureAwait(false)`** on every `await` inside the library.
- Use **`INotifyPropertyChanged`** with `RaiseAndSetIfChanged(ref field, value)` for all bindable properties.

### Documentation

- Add **XML doc comments** (`///`) on all `public` members — the library generates a documentation file.
- Keep doc comments accurate. If a method's behaviour changes, update its `<summary>` and `<remarks>`.

### Comments

- Only add inline comments when the **why** is non-obvious (hidden constraint, workaround, subtle invariant).
- Don't explain what the code does — well-named identifiers already do that.
- Don't leave large blocks of commented-out code.

### Error handling

- Use `ArgumentException` / `ArgumentNullException` for invalid parameters.
- Use `InvalidOperationException` for invalid object state.
- Use `OperationCanceledException` for cancellation. Never use `ArgumentOutOfRangeException` for property values.

### Security

- **Never** add raw installer keyword strings to source. Installer signatures are XOR-obfuscated with `ObfuscationKey` in `Utilities.cs` to avoid false positives in single-file apps. Follow the same pattern for any new signatures.

### Dependencies

Updatum is intentionally lightweight. Do not add new NuGet dependencies without discussion. The only runtime dependency is `Octokit`.

---

## Submitting Changes

1. **Fork** the repository and create a branch from `main`.
2. Make your changes following the code guidelines above.
3. Update `CHANGELOG.md` with a short description of what changed.
4. Open a **Pull Request** against `main` with a clear title and description.

If your change is non-trivial, open an issue first to discuss the approach before investing time in the implementation.

---

## Reporting Bugs

Open an issue on [GitHub Issues](https://github.com/sn4k3/Updatum/issues) and include:

- A clear description of the problem.
- Steps to reproduce.
- Expected vs actual behaviour.
- Your OS, .NET version, and Updatum version.
