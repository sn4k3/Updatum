using System;
using System.Collections.Generic;

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

/// <summary>
/// Parses and validates the runtime identifiers targeted by a publish operation.
/// </summary>
internal static class PublishRid
{
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

        PublishUtilities.ValidatePathLeafName(runtimeIdentifier, $"Runtime identifier '{runtimeIdentifier}'");

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
}
