using System.ComponentModel;
using Fallout.Common.Tooling;

namespace Updatum.Fallout;

/// <summary>
/// Represents the build configuration for the Nuke build process, such as Debug or Release.
/// </summary>
[TypeConverter(typeof(TypeConverter<BuildConfiguration>))]
public sealed class BuildConfiguration : Enumeration
{
    /// <summary>
    /// Gets the Debug Configuration
    /// </summary>
    public static readonly BuildConfiguration Debug = new() { Value = nameof(Debug) };

    /// <summary>
    /// Gets the Release Configuration
    /// </summary>
    public static readonly BuildConfiguration Release = new() { Value = nameof(Release) };

    /// <summary>
    /// Implicitly converts a Configuration instance to a string by returning its Value property.
    /// </summary>
    /// <param name="buildConfiguration">The Configuration instance to convert.</param>
    /// <returns>The Value property of the Configuration instance.</returns>
    public static implicit operator string(BuildConfiguration buildConfiguration)
    {
        return buildConfiguration.Value;
    }
}