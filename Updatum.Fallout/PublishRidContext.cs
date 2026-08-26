using System.Diagnostics.CodeAnalysis;
using Fallout.Common.IO;

namespace Updatum.Fallout;

/// <summary>
/// Provides the build and output information for one runtime publish operation.
/// </summary>
public sealed record PublishRidContext
{
    /// <summary>
    /// Gets the build that owns the publish operation.
    /// </summary>
    public required UpdatumBuild Build { get; init; }

    /// <summary>
    /// Gets the runtime identifier being published.
    /// </summary>
    public required string RuntimeIdentifier { get; init; }

    /// <summary>
    /// Gets the output path for the runtime publish operation.
    /// </summary>
    public required AbsolutePath PublishPath { get; init; }

    /// <summary>
    /// Gets the base output path for bundle artifacts created from this publish output.
    /// </summary>
    [field: MaybeNull]
    [field: AllowNull]
    internal AbsolutePath BundleOutputPath
    {
        get => field ?? PublishPath;
        init;
    }
}