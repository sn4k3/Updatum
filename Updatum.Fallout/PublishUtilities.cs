using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Fallout.Common.IO;

namespace Updatum.Fallout;

internal enum PublishRidFamily
{
    Windows,
    MacOS,
    Linux
}

internal sealed class PublishRidInfo
{
    internal required string RuntimeIdentifier { get; init; }
    internal required PublishRidFamily Family { get; init; }
    internal required string Architecture { get; init; }
    internal string? InstallerPlatform { get; init; }
    internal string? AppImageArchitecture { get; init; }
}

internal static class PublishUtilities
{
    private static readonly JsonSerializerOptions RuntimeManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    internal static IReadOnlyList<PublishRidInfo> ValidateRuntimeIdentifiers(IEnumerable<string>? runtimeIdentifiers)
    {
        if (runtimeIdentifiers is null)
            throw new InvalidOperationException("At least one runtime identifier must be configured.");

        var parsedRuntimeIdentifiers = new List<PublishRidInfo>();
        var knownRuntimeIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var runtimeIdentifier in runtimeIdentifiers)
        {
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
                throw new InvalidOperationException("A runtime identifier cannot be blank.");

            if (!knownRuntimeIdentifiers.Add(runtimeIdentifier))
                throw new InvalidOperationException($"Runtime identifier '{runtimeIdentifier}' is duplicated.");

            parsedRuntimeIdentifiers.Add(ParseRuntimeIdentifier(runtimeIdentifier));
        }

        if (parsedRuntimeIdentifiers.Count == 0)
            throw new InvalidOperationException("At least one runtime identifier must be configured.");

        return parsedRuntimeIdentifiers;
    }

    internal static PublishRidInfo ParseRuntimeIdentifier(string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            throw new InvalidOperationException("A runtime identifier cannot be blank.");

        ValidatePathLeafName(runtimeIdentifier, $"Runtime identifier '{runtimeIdentifier}'");

        var architectureSeparator = runtimeIdentifier.LastIndexOf('-');
        if (architectureSeparator <= 0 || architectureSeparator == runtimeIdentifier.Length - 1)
            throw new InvalidOperationException($"Runtime identifier '{runtimeIdentifier}' is invalid.");

        var platform = runtimeIdentifier[..architectureSeparator];
        var architecture = runtimeIdentifier[(architectureSeparator + 1)..].ToLowerInvariant();
        var platformFamily = platform.Split('-', 2)[0];

        return platformFamily.ToLowerInvariant() switch
        {
            "win" => CreateWindowsRidInfo(runtimeIdentifier, architecture),
            "osx" => CreateMacOSRidInfo(runtimeIdentifier, architecture),
            "linux" or "unix" => CreateLinuxRidInfo(runtimeIdentifier, architecture),
            _ => throw new InvalidOperationException($"Runtime identifier '{runtimeIdentifier}' is not supported.")
        };
    }

    internal static string ValidatePathLeafName(string? value, string valueName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathRooted(value) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException($"{valueName} must be a nonblank simple leaf name.");
        }

        return value;
    }

    internal static AbsolutePath GetDirectChildPath(AbsolutePath directory, string childName, string valueName)
    {
        ValidatePathLeafName(childName, valueName);

        var directoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath((string)directory));
        var childPath = Path.GetFullPath(Path.Combine(directoryPath, childName));
        var childDirectory = Path.GetDirectoryName(childPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(directoryPath, childDirectory, comparison))
            throw new InvalidOperationException($"{valueName} must resolve directly below '{directoryPath}'.");

        return (AbsolutePath)childPath;
    }

    internal static string? ExtractLatestReleaseNotes(string changelog)
    {
        ArgumentNullException.ThrowIfNull(changelog);

        var lines = changelog.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var releaseHeadingIndex = Array.FindIndex(lines, IsLevelTwoHeading);

        if (releaseHeadingIndex < 0)
            return null;

        var nextReleaseHeadingIndex = Array.FindIndex(lines, releaseHeadingIndex + 1, IsLevelTwoHeading);
        if (nextReleaseHeadingIndex < 0)
            nextReleaseHeadingIndex = lines.Length;

        var notesStartIndex = releaseHeadingIndex + 1;
        while (notesStartIndex < nextReleaseHeadingIndex && string.IsNullOrWhiteSpace(lines[notesStartIndex]))
            notesStartIndex++;

        return string.Join("\n", lines[notesStartIndex..nextReleaseHeadingIndex]).TrimEnd();
    }

    internal static void WriteLatestReleaseNotes(AbsolutePath changelogFile, AbsolutePath releaseNotesFile)
    {
        if (!File.Exists((string)changelogFile))
            return;

        var releaseNotes = ExtractLatestReleaseNotes(File.ReadAllText((string)changelogFile));
        if (releaseNotes is not null)
            File.WriteAllText((string)releaseNotesFile, releaseNotes);
    }

    internal static void DeleteFilesByExtension(
        AbsolutePath directory,
        IEnumerable<string> extensions,
        string extensionsPropertyName)
    {
        if (!Directory.Exists((string)directory))
            return;

        var cleanupExtensions = extensions
            .Select(extension => NormalizeExtension(extension, extensionsPropertyName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(
                     (string)directory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            if (cleanupExtensions.Contains(Path.GetExtension(filePath).TrimStart('.')))
                File.Delete(filePath);
        }
    }

    internal static bool IsWixProject(string projectPath)
    {
        return string.Equals(Path.GetExtension(projectPath), ".wixproj", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string extension, string extensionsPropertyName)
    {
        var normalizedExtension = extension?.Trim().TrimStart('.') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedExtension))
        {
            throw new InvalidOperationException(
                $"{extensionsPropertyName} cannot contain blank extensions.");
        }

        return normalizedExtension;
    }

    internal static bool ShouldCreatePortableZip(PublishRidInfo runtime, PublishBundleTypes bundleTypes)
    {
        return runtime.Family is not PublishRidFamily.MacOS ||
               !bundleTypes.HasFlag(PublishBundleTypes.MacOSAppBundle);
    }

    internal static void CreateZip(AbsolutePath source, AbsolutePath destination)
    {
        ZipFile.CreateFromDirectory((string)source, (string)destination, CompressionLevel.SmallestSize,
            includeBaseDirectory: false);
    }

    internal static void WriteRuntimeManifest(AbsolutePath directory, string fileName, BuildRuntime runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(runtime);

        if (Path.IsPathRooted(fileName) ||
            fileName is "." or ".." ||
            fileName.IndexOf('/') >= 0 ||
            fileName.IndexOf('\\') >= 0 ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The runtime manifest path must be a file name.", nameof(fileName));
        }

        var directoryPath = Path.GetFullPath((string)directory);
        var manifestPath = Path.GetFullPath(Path.Combine(directoryPath, fileName));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(Path.GetDirectoryName(manifestPath), directoryPath, pathComparison))
            throw new ArgumentException("The runtime manifest path must be inside the staging directory.",
                nameof(fileName));

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(runtime, RuntimeManifestJsonOptions));
    }

    private static PublishRidInfo CreateWindowsRidInfo(string runtimeIdentifier, string architecture)
    {
        if (architecture is not ("x64" or "arm64" or "x86"))
            throw new InvalidOperationException($"Runtime identifier '{runtimeIdentifier}' has an unsupported Windows architecture.");

        return new PublishRidInfo
        {
            RuntimeIdentifier = runtimeIdentifier,
            Family = PublishRidFamily.Windows,
            Architecture = architecture,
            InstallerPlatform = architecture
        };
    }

    private static PublishRidInfo CreateLinuxRidInfo(string runtimeIdentifier, string architecture)
    {
        var appImageArchitecture = architecture switch
        {
            "x64" => "x86_64",
            "arm64" => "aarch64",
            _ => throw new InvalidOperationException(
                $"Runtime identifier '{runtimeIdentifier}' has an unsupported Linux architecture.")
        };

        return new PublishRidInfo
        {
            RuntimeIdentifier = runtimeIdentifier,
            Family = PublishRidFamily.Linux,
            Architecture = architecture,
            AppImageArchitecture = appImageArchitecture
        };
    }

    private static PublishRidInfo CreateMacOSRidInfo(string runtimeIdentifier, string architecture)
    {
        if (architecture is not ("x64" or "arm64"))
            throw new InvalidOperationException(
                $"Runtime identifier '{runtimeIdentifier}' has an unsupported macOS architecture.");

        return new PublishRidInfo
        {
            RuntimeIdentifier = runtimeIdentifier,
            Family = PublishRidFamily.MacOS,
            Architecture = architecture
        };
    }

    private static bool IsLevelTwoHeading(string line)
    {
        return line.Length == 2 && line == "##" ||
               line.Length > 2 && line.StartsWith("##", StringComparison.Ordinal) && char.IsWhiteSpace(line[2]);
    }
}
