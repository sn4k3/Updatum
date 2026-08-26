using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Updatum.Fallout.AppBundleUtilities;

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

/// <summary>
/// Generates desktop, AppImage, AppStream, and Flatpak files for a Linux application bundle.
/// </summary>
public static class LinuxAppBundle
{
    /// <summary>
    /// Identifies the upstream AppImage tooling repository.
    /// </summary>
    public const string AppImageToolRepositoryUrl = "https://github.com/AppImage/appimagetool";

    /// <summary>
    /// Generates a freedesktop desktop entry from the supplied options.
    /// </summary>
    /// <param name="options">The Linux bundle configuration.</param>
    /// <returns>A desktop entry document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option or list item is empty.</exception>
    public static string GetDesktopEntry(LinuxAppBundleOptions options)
    {
        ValidateOptions(options);
        var executableName = GetExecutableName(options);
        var iconName = options.IconName ?? options.ProductName;
        ValidateRequired(iconName, nameof(options.IconName));

        var desktopEntry = $$"""
                             [Desktop Entry]
                             Type=Application
                             Name={{EscapeDesktopValue(options.ProductName)}}
                             Comment={{EscapeDesktopValue(options.Summary)}}
                             Categories={{FormatDesktopList(options.Categories, nameof(options.Categories))}}
                             Keywords={{FormatDesktopList(options.Keywords, nameof(options.Keywords))}}
                             Icon={{EscapeDesktopValue(iconName)}}
                             Exec="{{EscapeDesktopExecDoubleQuoted(executableName)}}"
                             Terminal={{options.Terminal.ToString().ToLowerInvariant()}}
                             SingleMainWindow={{options.SingleMainWindow.ToString().ToLowerInvariant()}}
                             """;

        return AppendCustomBlock(desktopEntry, options.ExtraDesktopEntry).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Generates the AppImage <c>AppRun</c> launcher script from the supplied options.
    /// </summary>
    /// <param name="options">The Linux bundle configuration.</param>
    /// <returns>A Bash launcher script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is empty.</exception>
    public static string GetAppRunScript(LinuxAppBundleOptions options)
    {
        ValidateOptions(options);
        var executableName = EscapeBashDoubleQuoted(GetExecutableName(options));
        var customCode = string.IsNullOrWhiteSpace(options.AppRunScriptBeforeExec)
            ? string.Empty
            : $"{options.AppRunScriptBeforeExec}\n\n";

        return $$"""
                 #!/usr/bin/env bash

                 HERE="$(dirname "$(readlink -f "${0}")")"
                 export PATH="${HERE}/usr/bin/:${PATH}"

                 {{customCode}}exec "{{executableName}}" "$@"
                 """.ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Generates an AppStream metadata document from the supplied options.
    /// </summary>
    /// <param name="options">The Linux bundle configuration.</param>
    /// <returns>An AppStream XML document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is empty or a screenshot URL is invalid.</exception>
    public static string GetAppStreamMetadata(LinuxAppBundleOptions options)
    {
        ValidateOptions(options);
        ValidateRequired(options.MetadataLicense, nameof(options.MetadataLicense));
        ValidateRequired(options.ContentRatingType, nameof(options.ContentRatingType));
        ValidateList(options.Controls, nameof(options.Controls));

        var component = new XElement(
            "component",
            new XAttribute("type", "desktop-application"),
            new XElement("id", options.ApplicationId),
            new XElement("name", options.ProductName),
            new XElement("metadata_license", options.MetadataLicense),
            new XElement("project_license", options.License),
            new XElement("content_rating", new XAttribute("type", options.ContentRatingType)),
            new XElement("summary", NormalizeSummary(options.Summary)),
            new XElement("description", new XElement("p", options.Description)),
            new XElement("categories", options.Categories.Select(category => new XElement("category", category))),
            new XElement("supports", options.Controls.Select(control => new XElement("control", control))));

        if (options.MinimumDisplayLength is not null)
        {
            if (options.MinimumDisplayLength <= 0)
            {
                throw new ArgumentException("The minimum display length must be greater than zero.",
                    nameof(options.MinimumDisplayLength));
            }

            component.Add(
                new XElement(
                    "recommends",
                    new XElement(
                        "display_length",
                        new XAttribute("compare", "ge"),
                        options.MinimumDisplayLength.Value)));
        }

        component.Add(new XElement("launchable", new XAttribute("type", "desktop-id"),
            $"{options.ApplicationId}.desktop"));

        var screenshots = CreateScreenshots(options.ScreenshotUrls);
        if (screenshots is not null)
        {
            component.Add(screenshots);
        }

        component.Add(
            new XElement("url", new XAttribute("type", "homepage"), options.RepositoryUrl),
            new XElement("developer_name", options.Authors));

        if (!string.IsNullOrWhiteSpace(options.UpdateContact))
        {
            component.Add(new XElement("update_contact", options.UpdateContact));
        }

        component.Add(new XElement("provides", new XElement("binary", GetExecutableName(options))));

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), component);
        return $"{document.Declaration}{Environment.NewLine}{document}".ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Generates a Flatpak manifest for already-published application files.
    /// </summary>
    /// <param name="options">The Linux bundle configuration.</param>
    /// <returns>A YAML Flatpak manifest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option or permission is empty.</exception>
    public static string GetFlatpakManifest(LinuxAppBundleOptions options)
    {
        ValidateOptions(options);
        ValidateRequired(options.FlatpakRuntime, nameof(options.FlatpakRuntime));
        ValidateRequired(options.FlatpakRuntimeVersion, nameof(options.FlatpakRuntimeVersion));
        ValidateRequired(options.FlatpakSdk, nameof(options.FlatpakSdk));
        ValidateList(options.FlatpakFinishArguments, nameof(options.FlatpakFinishArguments));

        var executableName = GetExecutableName(options);
        var sourceDirectory = options.ProductName;
        var executablePath = $"/app/bin/app-sources/{executableName}";
        var finishArguments = string.Join(
            Environment.NewLine,
            options.FlatpakFinishArguments.Select(argument => $"  - {QuoteYaml(argument)}"));

        return $$"""
                 app-id: {{QuoteYaml(options.ApplicationId)}}
                 runtime: {{QuoteYaml(options.FlatpakRuntime)}}
                 runtime-version: {{QuoteYaml(options.FlatpakRuntimeVersion)}}
                 sdk: {{QuoteYaml(options.FlatpakSdk)}}
                 command: {{QuoteYaml(executableName)}}
                 finish-args:
                 {{finishArguments}}

                 modules:
                   - name: {{QuoteYaml(options.ProductName)}}
                     buildsystem: simple
                     build-commands:
                       - {{QuoteYaml("mkdir -p /app/bin")}}
                       - {{QuoteYaml("mv ./app-sources /app/bin/app-sources")}}
                       - {{QuoteYaml($"chmod +x {QuoteShell(executablePath)}")}}
                       - {{QuoteYaml($"ln -s {QuoteShell(executablePath)} {QuoteShell($"/app/bin/{executableName}")}")}}
                     sources:
                       - type: dir
                         path: {{QuoteYaml(sourceDirectory)}}
                         dest: app-sources
                 """;
    }

    private static XElement? CreateScreenshots(IReadOnlyList<string> screenshotUrls)
    {
        ArgumentNullException.ThrowIfNull(screenshotUrls);
        if (screenshotUrls.Count == 0)
        {
            return null;
        }

        var screenshots = new XElement("screenshots");
        for (var index = 0; index < screenshotUrls.Count; index++)
        {
            var screenshotUrl = screenshotUrls[index];
            if (!Uri.TryCreate(screenshotUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException($"Screenshot URL at index {index} must be an absolute HTTP or HTTPS URL.",
                    nameof(screenshotUrls));
            }

            var screenshot = new XElement("screenshot", new XElement("image", screenshotUrl));
            if (index == 0)
            {
                screenshot.SetAttributeValue("type", "default");
            }

            screenshots.Add(screenshot);
        }

        return screenshots;
    }

    private static void ValidateOptions(LinuxAppBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateRequired(options.ApplicationId, nameof(options.ApplicationId));
        ValidateRequired(options.ProductName, nameof(options.ProductName));
        ValidateRequired(options.Summary, nameof(options.Summary));
        ValidateRequired(options.Description, nameof(options.Description));
        ValidateRequired(options.License, nameof(options.License));
        ValidateRequired(options.RepositoryUrl, nameof(options.RepositoryUrl));
        ValidateRequired(options.Authors, nameof(options.Authors));
        ValidateList(options.Categories, nameof(options.Categories));
    }

    private static string GetExecutableName(LinuxAppBundleOptions options)
    {
        var executableName = options.ExecutableName ?? options.ProductName;
        ValidateRequired(executableName, nameof(options.ExecutableName));
        return executableName;
    }

    private static void ValidateList(IReadOnlyList<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("List items cannot be null, empty, or white space.", parameterName);
        }
    }

    private static string FormatDesktopList(IReadOnlyList<string> values, string parameterName)
    {
        ValidateList(values, parameterName);
        return values.Count == 0
            ? string.Empty
            : $"{string.Join(';', values.Select(EscapeDesktopListItem))};";
    }

    private static string NormalizeSummary(string value)
    {
        var summary = Regex.Replace(value, @"\s+", " ").Trim();
        return summary.Length <= 78 ? summary : summary[..78].TrimEnd();
    }

    private static string AppendCustomBlock(string content, string? customBlock)
    {
        return string.IsNullOrWhiteSpace(customBlock) ? content : $"{content}\n{customBlock}";
    }

    private static string EscapeDesktopValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static string EscapeDesktopListItem(string value)
    {
        return EscapeDesktopValue(value).Replace(";", "\\;", StringComparison.Ordinal);
    }

    private static string EscapeDesktopExecDoubleQuoted(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal);
    }
}