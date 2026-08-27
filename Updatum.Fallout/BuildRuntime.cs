using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using StageKit.Runtime;

namespace Updatum.Fallout;

/// <summary>
/// Describes the runtime and packaging metadata for a published build.
/// </summary>
public record BuildRuntime
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuildRuntime"/> class.
    /// </summary>
    public BuildRuntime()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildRuntime"/> class with build values.
    /// </summary>
    /// <param name="runtime">The runtime identifier for the build.</param>
    /// <param name="buildVersion">The application version used for the build.</param>
    /// <param name="isBundle">Whether the build is a bundle.</param>
    /// <param name="bundleType">The type of bundle represented by the build.</param>
    [SetsRequiredMembers]
    public BuildRuntime(string runtime, string buildVersion, bool isBundle = false,
        ApplicationBundleType bundleType = ApplicationBundleType.Unknown)
    {
        Runtime = runtime;
        BuildVersion = buildVersion;
        IsBundle = isBundle;
        BundleType = bundleType;
    }

    /// <summary>
    /// Gets or sets the runtime identifier for the build.
    /// </summary>
    public required string Runtime { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the build is a bundle.
    /// </summary>
    public bool IsBundle { get; init; }

    /// <summary>
    /// Gets or sets the type of bundle represented by the build.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApplicationBundleType BundleType { get; init; }

    /// <summary>
    /// Gets or sets the UTC date and time when the build metadata was created.
    /// </summary>
    public DateTime BuildDateTimeUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the operating system description of the build environment.
    /// </summary>
    public string BuildOSDescription { get; init; } = RuntimeInformation.OSDescription;

    /// <summary>
    /// Gets or sets the application version used for the build.
    /// </summary>
    public required string BuildVersion { get; init; }
}