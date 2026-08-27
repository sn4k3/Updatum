using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Updatum.Fallout;

/// <summary>
/// Defines the metadata and runtime layout used to generate a macOS application bundle.
/// </summary>
public class MacAppBundleOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MacAppBundleOptions"/> class.
    /// </summary>
    public MacAppBundleOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MacAppBundleOptions"/> class using the specified build information.
    /// </summary>
    /// <param name="build">The build information.</param>
    [SetsRequiredMembers]
    public MacAppBundleOptions(UpdatumBuild build)
    {
        ProductName = build.SoftwareName;
        BundleIdentifier = build.SoftwareRDNS;
        Version = build.SoftwareVersion;
        Copyright = build.SoftwareCopyright;
        ExecutableName = build.SoftwareName;
    }

    /// <summary>
    /// Gets the product name displayed by macOS.
    /// </summary>
    public required string ProductName { get; set; }

    /// <summary>
    /// Gets the reverse-DNS bundle identifier.
    /// </summary>
    /// <example><c>com.example.product</c></example>
    public required string BundleIdentifier { get; set; }

    /// <summary>
    /// Gets the bundle version.
    /// </summary>
    public required string Version { get; set; }

    /// <summary>
    /// Gets the human-readable copyright notice.
    /// </summary>
    public string Copyright { get; set; } = string.Empty;

    /// <summary>
    /// Gets the bundle development region.
    /// </summary>
    /// <value>The development region. The default is <c>en</c>.</value>
    public string DevelopmentRegion { get; set; } = "en";

    /// <summary>
    /// Gets the icon file name stored in the bundle resources directory.
    /// </summary>
    /// <value>The icon file name, or <see langword="null"/> to use <c>{ProductName}.icns</c>.</value>
    public string? IconFileName { get; set; }

    /// <summary>
    /// Gets the executable file name stored in the bundle's <c>Contents/MacOS</c> directory.
    /// </summary>
    /// <value>The executable file name, or <see langword="null"/> to use <see cref="ProductName"/>.</value>
    public string? ExecutableName { get; set; }

    /// <summary>
    /// Gets the minimum supported macOS version.
    /// </summary>
    /// <value>The minimum system version. The default is <c>13.0</c>.</value>
    public string MinimumSystemVersion { get; set; } = "13.0";

    /// <summary>
    /// Gets the macOS application category identifier.
    /// </summary>
    /// <value>The application category. The default is <c>public.app-category.utilities</c>.</value>
    public string ApplicationCategory { get; set; } = "public.app-category.utilities";

    /// <summary>
    /// Gets a value that indicates whether the bundle supports high-resolution displays.
    /// </summary>
    /// <value><see langword="true"/> if high-resolution display support is enabled; otherwise, <see langword="false"/>. The default is <see langword="true"/>.</value>
    public bool HighResolutionCapable { get; set; } = true;

    /// <summary>
    /// Gets additional key/value entries inserted into the generated <c>Info.plist</c> dictionary.
    /// </summary>
    /// <value>The plist XML fragment, or <see langword="null"/> to insert nothing.</value>
    /// <remarks>The fragment must contain complete, unique plist key/value pairs without a surrounding <c>dict</c> element.</remarks>
    public string? ExtraInfoPListEntries { get; set; }

    /// <summary>
    /// Gets the code-signing entitlement keys and their Boolean values.
    /// </summary>
    /// <value>The custom entitlements, or <see langword="null"/> to use <see cref="MacAppBundle.DefaultEntitlements"/>.</value>
    public Dictionary<string, bool>? Entitlements { get; set; }

    /// <summary>
    /// Gets the runtime directory selected on Apple Silicon systems.
    /// </summary>
    /// <value>The ARM64 runtime directory. The default is <c>osx-arm64</c>.</value>
    public string Arm64RuntimeIdentifier { get; set; } = "osx-arm64";

    /// <summary>
    /// Gets the runtime directory selected on Intel systems.
    /// </summary>
    /// <value>The x64 runtime directory. The default is <c>osx-x64</c>.</value>
    public string X64RuntimeIdentifier { get; set; } = "osx-x64";

    /// <summary>
    /// Gets custom Bash code inserted immediately before the final <c>exec</c> command in the multi-architecture entry script.
    /// </summary>
    /// <value>The custom Bash code, or <see langword="null"/> to insert nothing.</value>
    /// <remarks>The value is inserted without escaping and must use valid Bash syntax.</remarks>
    public string? MultiArchEntryScriptBeforeExec { get; set; }
}
