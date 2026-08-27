using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Updatum.Fallout;

/// <summary>
/// Defines the metadata and runtime settings used to generate Linux application bundles.
/// </summary>
public class LinuxAppBundleOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxAppBundleOptions"/> class.
    /// </summary>
    public LinuxAppBundleOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxAppBundleOptions"/> class using the specified build information.
    /// </summary>
    /// <param name="build">The build information.</param>
    [SetsRequiredMembers]
    public LinuxAppBundleOptions(UpdatumBuild build)
    {
        ApplicationId = build.SoftwareRDNS;
        ProductName = build.SoftwareName;
        Summary = build.SoftwareSummary;
        Description = build.SoftwareDescription;
        License = build.SoftwareLicense;
        RepositoryUrl = build.SoftwareRepositoryUrl;
        Authors = build.SoftwareAuthors;
        Keywords = build.SoftwarePackageTagsList.ToList();
    }

    /// <summary>
    /// Gets the reverse-DNS application identifier.
    /// </summary>
    /// <example><c>org.example.product</c></example>
    public required string ApplicationId { get; set; }

    /// <summary>
    /// Gets the product name displayed by Linux desktop environments.
    /// </summary>
    public required string ProductName { get; set; }

    /// <summary>
    /// Gets the short product summary.
    /// </summary>
    public required string Summary { get; set; }

    /// <summary>
    /// Gets the full product description.
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Gets the SPDX project license expression.
    /// </summary>
    public required string License { get; set; }

    /// <summary>
    /// Gets the project homepage or repository URL.
    /// </summary>
    public required string RepositoryUrl { get; set; }

    /// <summary>
    /// Gets the developer or author name displayed in application stores.
    /// </summary>
    public required string Authors { get; set; }

    /// <summary>
    /// Gets the executable file name.
    /// </summary>
    /// <value>The executable name, or <see langword="null"/> to use <see cref="ProductName"/>.</value>
    public string? ExecutableName { get; set; }

    /// <summary>
    /// Gets the desktop icon name.
    /// </summary>
    /// <value>The icon name, or <see langword="null"/> to use <see cref="ProductName"/>.</value>
    public string? IconName { get; set; }

    /// <summary>
    /// Gets the desktop menu categories.
    /// </summary>
    /// <value>The categories. The default contains <c>Utility</c>.</value>
    public List<string> Categories { get; set; } = ["Utility"];

    /// <summary>
    /// Gets the terms used when searching for the application in desktop menus.
    /// </summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>
    /// Gets a value that indicates whether the application runs in a terminal.
    /// </summary>
    public bool Terminal { get; set; }

    /// <summary>
    /// Gets a value that indicates whether the application uses one main window.
    /// </summary>
    public bool SingleMainWindow { get; set; } = true;

    /// <summary>
    /// Gets additional lines appended to the generated desktop entry.
    /// </summary>
    /// <value>The custom desktop entry lines, or <see langword="null"/> to append nothing.</value>
    /// <remarks>The value is inserted without escaping and must use valid desktop entry syntax.</remarks>
    public string? ExtraDesktopEntry { get; set; }

    /// <summary>
    /// Gets custom Bash code inserted immediately before the final <c>exec</c> command in the AppRun script.
    /// </summary>
    /// <value>The custom Bash code, or <see langword="null"/> to insert nothing.</value>
    /// <remarks>The value is inserted without escaping and must use valid Bash syntax.</remarks>
    public string? AppRunScriptBeforeExec { get; set; }

    /// <summary>
    /// Gets the SPDX license expression for the AppStream metadata file.
    /// </summary>
    /// <value>The metadata license. The default is <c>FSFAP</c>.</value>
    public string MetadataLicense { get; set; } = "FSFAP";

    /// <summary>
    /// Gets the AppStream content rating specification type.
    /// </summary>
    /// <value>The content rating type. The default is <c>oars-1.0</c>.</value>
    public string ContentRatingType { get; set; } = "oars-1.0";

    /// <summary>
    /// Gets the email address used for AppStream update notifications.
    /// </summary>
    /// <value>The update contact email, or <see langword="null"/> to omit it.</value>
    public string? UpdateContact { get; set; }

    /// <summary>
    /// Gets the ordered screenshot URLs included in AppStream metadata.
    /// </summary>
    /// <remarks>The first URL is marked as the default screenshot.</remarks>
    public List<string> ScreenshotUrls { get; set; } = [];

    /// <summary>
    /// Gets the input controls supported by the application.
    /// </summary>
    public List<string> Controls { get; set; } = ["pointing", "keyboard", "touch"];

    /// <summary>
    /// Gets the minimum recommended display length in logical pixels.
    /// </summary>
    /// <value>The minimum display length, or <see langword="null"/> to omit the recommendation.</value>
    public int? MinimumDisplayLength { get; set; } = 760;

    /// <summary>
    /// Gets the Flatpak runtime identifier.
    /// </summary>
    public string FlatpakRuntime { get; set; } = "org.freedesktop.Platform";

    /// <summary>
    /// Gets the Flatpak runtime version.
    /// </summary>
    public string FlatpakRuntimeVersion { get; set; } = "23.08";

    /// <summary>
    /// Gets the Flatpak SDK identifier.
    /// </summary>
    public string FlatpakSdk { get; set; } = "org.freedesktop.Sdk";

    /// <summary>
    /// Gets the Flatpak sandbox permissions.
    /// </summary>
    public List<string> FlatpakFinishArguments { get; set; } =
        ["--socket=x11", "--share=ipc", "--device=dri", "--share=network"];
}
