using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using static Updatum.Fallout.AppBundleUtilities;

namespace Updatum.Fallout;

/// <summary>
/// Generates metadata and launcher files for a macOS application bundle.
/// </summary>
public static class MacAppBundle
{
    private const string ApplePlistPublicIdentifier = "-//Apple//DTD PLIST 1.0//EN";
    private const string ApplePlistSystemIdentifier = "http://www.apple.com/DTDs/PropertyList-1.0.dtd";

    private static readonly IReadOnlyDictionary<string, bool> DefaultEntitlementValues =
        new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>
        {
            ["com.apple.security.cs.allow-jit"] = true,
            ["com.apple.security.cs.allow-unsigned-executable-memory"] = true,
            ["com.apple.security.cs.disable-library-validation"] = true,
            ["com.apple.security.cs.allow-dyld-environment-variables"] = true
        });

    /// <summary>
    /// Gets the default code-signing entitlements used by .NET desktop applications.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> DefaultEntitlements => DefaultEntitlementValues;

    /// <summary>
    /// Generates an <c>Info.plist</c> document from the supplied bundle options.
    /// </summary>
    /// <param name="options">The macOS bundle configuration.</param>
    /// <returns>An XML property-list document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required option is empty, or the custom plist entries are invalid or duplicated.</exception>
    public static string GetInfoPList(MacAppBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateRequired(options.ProductName, nameof(options.ProductName));
        ValidateRequired(options.BundleIdentifier, nameof(options.BundleIdentifier));
        ValidateRequired(options.Version, nameof(options.Version));
        ValidateRequired(options.DevelopmentRegion, nameof(options.DevelopmentRegion));
        ValidateRequired(options.MinimumSystemVersion, nameof(options.MinimumSystemVersion));
        ValidateRequired(options.ApplicationCategory, nameof(options.ApplicationCategory));

        var iconFileName = options.IconFileName ?? $"{options.ProductName}.icns";
        var executableName = options.ExecutableName ?? options.ProductName;
        ValidateRequired(iconFileName, nameof(options.IconFileName));
        ValidateRequired(executableName, nameof(options.ExecutableName));

        var entries = new[]
        {
            StringEntry("CFBundleDevelopmentRegion", options.DevelopmentRegion),
            StringEntry("CFBundleIconFile", iconFileName),
            StringEntry("CFBundleIdentifier", options.BundleIdentifier),
            StringEntry("CFBundleDisplayName", options.ProductName),
            StringEntry("CFBundleName", options.ProductName),
            StringEntry("CFBundleVersion", options.Version),
            StringEntry("LSMinimumSystemVersion", options.MinimumSystemVersion),
            StringEntry("CFBundleExecutable", executableName),
            StringEntry("CFBundleInfoDictionaryVersion", "6.0"),
            StringEntry("CFBundlePackageType", "APPL"),
            StringEntry("CFBundleShortVersionString", options.Version),
            ArrayEntry("CFBundleSupportedPlatforms", "MacOSX"),
            StringEntry("LSApplicationCategoryType", options.ApplicationCategory),
            BooleanEntry("NSHighResolutionCapable", options.HighResolutionCapable),
            StringEntry("NSHumanReadableCopyright", options.Copyright)
        }.ToList();

        entries.AddRange(ParseExtraInfoPListEntries(options.ExtraInfoPListEntries, entries));
        return CreatePropertyList(entries.ToArray());
    }

    /// <summary>
    /// Generates an <c>Info.plist</c> document using the standard optional values.
    /// </summary>
    /// <param name="productName">The product name displayed by macOS.</param>
    /// <param name="bundleIdentifier">The reverse-DNS bundle identifier.</param>
    /// <param name="version">The bundle version.</param>
    /// <param name="copyright">The human-readable copyright notice.</param>
    /// <returns>An XML property-list document.</returns>
    public static string GetInfoPList(
        string productName,
        string bundleIdentifier,
        string version,
        string copyright)
    {
        return GetInfoPList(new MacAppBundleOptions
        {
            ProductName = productName,
            BundleIdentifier = bundleIdentifier,
            Version = version,
            Copyright = copyright
        });
    }

    /// <summary>
    /// Generates a code-signing entitlements property list from the supplied bundle options.
    /// </summary>
    /// <param name="options">The macOS bundle configuration.</param>
    /// <returns>An XML property-list document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An entitlement key is empty or consists only of white-space characters.</exception>
    public static string GetEntitlements(MacAppBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GetEntitlements(options.Entitlements ?? DefaultEntitlements);
    }

    /// <summary>
    /// Generates a code-signing entitlements property list from the supplied values.
    /// </summary>
    /// <param name="entitlements">The entitlement keys and their Boolean values.</param>
    /// <returns>An XML property-list document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entitlements"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An entitlement key is empty or consists only of white-space characters.</exception>
    public static string GetEntitlements(IReadOnlyDictionary<string, bool> entitlements)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        var entries = entitlements.Select(entitlement =>
        {
            ValidateRequired(entitlement.Key, nameof(entitlements));
            return BooleanEntry(entitlement.Key, entitlement.Value);
        });

        return CreatePropertyList(entries.ToArray());
    }

    /// <summary>
    /// Generates a launcher that selects the appropriate runtime directory for the current Mac architecture.
    /// </summary>
    /// <param name="options">The macOS bundle configuration.</param>
    /// <returns>A Bash launcher script.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A required script option is empty or consists only of white-space characters.</exception>
    public static string GetMultiArchEntryScript(MacAppBundleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return GetMultiArchEntryScript(
            options.ExecutableName ?? options.ProductName,
            options.Arm64RuntimeIdentifier,
            options.X64RuntimeIdentifier,
            options.MultiArchEntryScriptBeforeExec);
    }

    /// <summary>
    /// Generates a launcher using the conventional macOS runtime directory names.
    /// </summary>
    /// <param name="productName">The executable file name.</param>
    /// <returns>A Bash launcher script.</returns>
    /// <exception cref="ArgumentException"><paramref name="productName"/> is empty or consists only of white-space characters.</exception>
    public static string GetMultiArchEntryScript(string productName)
    {
        return GetMultiArchEntryScript(productName, "osx-arm64", "osx-x64", null);
    }

    private static string GetMultiArchEntryScript(
        string executableName,
        string arm64RuntimeIdentifier,
        string x64RuntimeIdentifier,
        string? customCodeBeforeExec)
    {
        ValidateRequired(executableName, nameof(executableName));
        ValidateRequired(arm64RuntimeIdentifier, nameof(arm64RuntimeIdentifier));
        ValidateRequired(x64RuntimeIdentifier, nameof(x64RuntimeIdentifier));

        var escapedExecutableName = EscapeBashDoubleQuoted(executableName);
        var escapedArm64RuntimeIdentifier = EscapeBashDoubleQuoted(arm64RuntimeIdentifier);
        var escapedX64RuntimeIdentifier = EscapeBashDoubleQuoted(x64RuntimeIdentifier);
        var customCode = string.IsNullOrWhiteSpace(customCodeBeforeExec)
            ? string.Empty
            : $"{customCodeBeforeExec}\n\n";

        return $$"""
                 #!/usr/bin/env bash

                 DIR=$(dirname "$0")
                 ARM64=$(sysctl -ni hw.optional.arm64)

                 if [[ "$ARM64" == 1 ]]; then
                     export PATH="${DIR}/{{escapedArm64RuntimeIdentifier}}/:${PATH}"
                 else
                     export PATH="${DIR}/{{escapedX64RuntimeIdentifier}}/:${PATH}"
                 fi

                 {{customCode}}exec "{{escapedExecutableName}}" "$@"
                 """;
    }

    private static IEnumerable<XElement> ParseExtraInfoPListEntries(
        string? fragment,
        IEnumerable<XElement> existingEntries)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return [];
        }

        XElement root;
        try
        {
            root = XElement.Parse($"<dict>{fragment}</dict>", LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new ArgumentException("The custom Info.plist entries must be a well-formed XML fragment.",
                nameof(fragment), exception);
        }

        var elements = root.Elements().ToArray();
        if (elements.Length % 2 != 0)
        {
            throw new ArgumentException("The custom Info.plist entries must contain complete key/value pairs.",
                nameof(fragment));
        }

        var keys = existingEntries
            .Select(entry => entry.Element("key")?.Value)
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);
        var entries = new List<XElement>(elements.Length / 2);

        for (var index = 0; index < elements.Length; index += 2)
        {
            var keyElement = elements[index];
            var valueElement = elements[index + 1];
            if (keyElement.Name != "key")
            {
                throw new ArgumentException("Each custom Info.plist entry must begin with a key element.",
                    nameof(fragment));
            }

            ValidateRequired(keyElement.Value, nameof(fragment));
            if (!keys.Add(keyElement.Value))
            {
                throw new ArgumentException($"The Info.plist key '{keyElement.Value}' is duplicated.",
                    nameof(fragment));
            }

            if (!IsPropertyListValue(valueElement))
            {
                throw new ArgumentException(
                    $"The value for Info.plist key '{keyElement.Value}' is not a supported plist value.",
                    nameof(fragment));
            }

            entries.Add(new XElement("entry", new XElement(keyElement), new XElement(valueElement)));
        }

        return entries;
    }

    private static bool IsPropertyListValue(XElement element)
    {
        return element.Name.LocalName is "array" or "data" or "date" or "dict" or "false" or "integer" or "real"
            or "string" or "true";
    }

    private static string CreatePropertyList(params XElement[] entries)
    {
        var dictionary = new XElement("dict");
        foreach (var entry in entries)
        {
            dictionary.Add(entry.Elements());
        }

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", ApplePlistPublicIdentifier, ApplePlistSystemIdentifier, null),
            new XElement("plist", new XAttribute("version", "1.0"), dictionary));

        return $"{document.Declaration}{Environment.NewLine}{document}";
    }

    private static XElement StringEntry(string key, string value)
    {
        return new XElement("entry", new XElement("key", key), new XElement("string", value));
    }

    private static XElement BooleanEntry(string key, bool value)
    {
        return new XElement("entry", new XElement("key", key), new XElement(value ? "true" : "false"));
    }

    private static XElement ArrayEntry(string key, params string[] values)
    {
        return new XElement("entry", new XElement("key", key),
            new XElement("array", values.Select(value => new XElement("string", value))));
    }
}
