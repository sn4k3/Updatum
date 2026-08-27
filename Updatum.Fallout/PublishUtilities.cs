using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fallout.Common.IO;

namespace Updatum.Fallout;

internal static class PublishUtilities
{
    private static readonly JsonSerializerOptions RuntimeManifestJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Determines whether a value names a single file-system entry rather than a path.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns><c>true</c> when the value is a nonblank simple leaf name; otherwise, <c>false</c>.</returns>
    internal static bool IsPathLeafName([NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               !Path.IsPathRooted(value) &&
               !value.Contains('/') &&
               !value.Contains('\\') &&
               value is not ("." or "..") &&
               value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    internal static string ValidatePathLeafName(string? value, string valueName)
    {
        if (!IsPathLeafName(value))
            throw new InvalidOperationException($"{valueName} must be a nonblank simple leaf name.");

        return value;
    }

    internal static AbsolutePath GetDirectChildPath(AbsolutePath directory, string childName, string valueName)
    {
        ValidatePathLeafName(childName, valueName);

        var directoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var childPath = Path.GetFullPath(Path.Combine(directoryPath, childName));
        var childDirectory = Path.GetDirectoryName(childPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(directoryPath, childDirectory, comparison))
            throw new InvalidOperationException($"{valueName} must resolve directly below '{directoryPath}'.");

        return (AbsolutePath)childPath;
    }

    internal static void DeleteFilesByExtension(
        AbsolutePath directory,
        IEnumerable<string> extensions,
        string extensionsPropertyName)
    {
        if (!Directory.Exists(directory))
            return;

        var cleanupExtensions = extensions
            .Select(extension => NormalizeExtension(extension, extensionsPropertyName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(
                     directory,
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

    internal static void CreateZip(AbsolutePath source, AbsolutePath destination)
    {
        ZipFile.CreateFromDirectory(source, destination, CompressionLevel.SmallestSize,
            includeBaseDirectory: false);
    }

    internal static void WriteRuntimeManifest(AbsolutePath directory, string fileName, BuildRuntime runtime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(runtime);

        if (!IsPathLeafName(fileName))
            throw new ArgumentException("The runtime manifest path must be a file name.", nameof(fileName));

        // A validated leaf name always resolves directly below the directory, so this cannot throw.
        var manifestPath = GetDirectChildPath(directory, fileName, nameof(fileName));

        Directory.CreateDirectory(manifestPath.Parent);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(runtime, RuntimeManifestJsonOptions));
    }
}
