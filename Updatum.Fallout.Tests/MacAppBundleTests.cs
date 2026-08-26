using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Updatum.Fallout.Tests;

public class MacAppBundleTests
{
    [Fact]
    public void Options_AfterConstruction_CanBeModified()
    {
        var options = CreateOptions();

        options.ProductName = "Modified";
        options.ExecutableName = "Modified.Launcher";

        var document = XDocument.Parse(MacAppBundle.GetInfoPList(options));

        Assert.Equal("Modified", GetDictionaryValue(document, "CFBundleName"));
        Assert.Equal("Modified.Launcher", GetDictionaryValue(document, "CFBundleExecutable"));
    }

    [Fact]
    public void GetInfoPList_DefaultOptions_ProducesValidMetadata()
    {
        var options = new MacAppBundleOptions
        {
            ProductName = "Example & Tool",
            BundleIdentifier = "com.example.tool",
            Version = "1.2.3",
            Copyright = "Copyright <Example>"
        };

        var propertyList = MacAppBundle.GetInfoPList(options);
        var document = XDocument.Parse(propertyList);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", propertyList, StringComparison.Ordinal);
        Assert.Equal("Example & Tool", GetDictionaryValue(document, "CFBundleDisplayName"));
        Assert.Equal("Example & Tool.icns", GetDictionaryValue(document, "CFBundleIconFile"));
        Assert.Equal("Example & Tool", GetDictionaryValue(document, "CFBundleExecutable"));
        Assert.Equal("com.example.tool", GetDictionaryValue(document, "CFBundleIdentifier"));
        Assert.Equal("1.2.3", GetDictionaryValue(document, "CFBundleVersion"));
        Assert.Equal("1.2.3", GetDictionaryValue(document, "CFBundleShortVersionString"));
        Assert.Equal("13.0", GetDictionaryValue(document, "LSMinimumSystemVersion"));
        Assert.Equal("public.app-category.utilities", GetDictionaryValue(document, "LSApplicationCategoryType"));
        Assert.Equal("Copyright <Example>", GetDictionaryValue(document, "NSHumanReadableCopyright"));
        Assert.Equal("true", GetDictionaryValue(document, "NSHighResolutionCapable"));
    }

    [Fact]
    public void GetInfoPList_CustomOptions_UsesCustomBundleValues()
    {
        var options = new MacAppBundleOptions
        {
            ProductName = "Example",
            BundleIdentifier = "org.example.app",
            Version = "2.0.0",
            Copyright = "Example Ltd.",
            DevelopmentRegion = "pt",
            IconFileName = "Brand.icns",
            ExecutableName = "Example.Launcher",
            MinimumSystemVersion = "14.2",
            ApplicationCategory = "public.app-category.developer-tools",
            HighResolutionCapable = false
        };

        var document = XDocument.Parse(MacAppBundle.GetInfoPList(options));

        Assert.Equal("pt", GetDictionaryValue(document, "CFBundleDevelopmentRegion"));
        Assert.Equal("Brand.icns", GetDictionaryValue(document, "CFBundleIconFile"));
        Assert.Equal("Example.Launcher", GetDictionaryValue(document, "CFBundleExecutable"));
        Assert.Equal("14.2", GetDictionaryValue(document, "LSMinimumSystemVersion"));
        Assert.Equal("public.app-category.developer-tools", GetDictionaryValue(document, "LSApplicationCategoryType"));
        Assert.Equal("false", GetDictionaryValue(document, "NSHighResolutionCapable"));
    }

    [Fact]
    public void GetInfoPList_ExtraEntries_AppendsValidatedPlistValues()
    {
        var options = CreateOptions();
        options.ExtraInfoPListEntries = """
                                        <key>NSCameraUsageDescription</key>
                                        <string>Capture images &amp; video.</string>
                                        <key>LSUIElement</key>
                                        <true />
                                        <key>CFBundleURLTypes</key>
                                        <array>
                                          <dict>
                                            <key>CFBundleURLSchemes</key>
                                            <array><string>example</string></array>
                                          </dict>
                                        </array>
                                        """;

        var document = XDocument.Parse(MacAppBundle.GetInfoPList(options));

        Assert.Equal("Capture images & video.", GetDictionaryValue(document, "NSCameraUsageDescription"));
        Assert.Equal("true", GetDictionaryValue(document, "LSUIElement"));
        Assert.Equal("example", GetDictionaryValue(document, "CFBundleURLSchemes"));
    }

    [Fact]
    public void GetInfoPList_MalformedExtraEntries_ThrowsArgumentException()
    {
        var options = CreateOptions();
        options.ExtraInfoPListEntries = "<key>NSCameraUsageDescription</key><string>Missing end tag";

        Assert.Throws<ArgumentException>(() => MacAppBundle.GetInfoPList(options));
    }

    [Fact]
    public void GetInfoPList_DuplicateExtraEntry_ThrowsArgumentException()
    {
        var options = CreateOptions();
        options.ExtraInfoPListEntries = "<key>CFBundleName</key><string>Replacement</string>";

        Assert.Throws<ArgumentException>(() => MacAppBundle.GetInfoPList(options));
    }

    [Fact]
    public void GetEntitlements_CustomOptions_UsesConfiguredFlags()
    {
        var options = new MacAppBundleOptions
        {
            ProductName = "Example",
            BundleIdentifier = "org.example.app",
            Version = "1.0.0",
            Entitlements = new Dictionary<string, bool>
            {
                ["com.apple.security.app-sandbox"] = true,
                ["com.apple.security.network.client"] = false
            }
        };

        var document = XDocument.Parse(MacAppBundle.GetEntitlements(options));

        Assert.Equal("true", GetDictionaryValue(document, "com.apple.security.app-sandbox"));
        Assert.Equal("false", GetDictionaryValue(document, "com.apple.security.network.client"));
        Assert.Null(FindDictionaryValue(document, "com.apple.security.cs.allow-jit"));
    }

    [Fact]
    public void GetMultiArchEntryScript_CustomOptions_EscapesValuesAndForwardsArguments()
    {
        var options = new MacAppBundleOptions
        {
            ProductName = "Example",
            BundleIdentifier = "org.example.app",
            Version = "1.0.0",
            ExecutableName = "Example $Tool\"",
            Arm64RuntimeIdentifier = "custom-arm64",
            X64RuntimeIdentifier = "custom-x64"
        };

        var script = MacAppBundle.GetMultiArchEntryScript(options);

        Assert.Contains("${DIR}/custom-arm64/", script, StringComparison.Ordinal);
        Assert.Contains("${DIR}/custom-x64/", script, StringComparison.Ordinal);
        Assert.Contains("exec \"Example \\$Tool\\\"\" \"$@\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMultiArchEntryScript_CustomBash_InsertsCodeImmediatelyBeforeExec()
    {
        var options = CreateOptions();
        options.MultiArchEntryScriptBeforeExec = """
                                                 export EXAMPLE_MODE="portable"
                                                 echo "Starting ${DIR}"
                                                 """;

        var script = MacAppBundle.GetMultiArchEntryScript(options);
        var customCodeIndex = script.IndexOf("export EXAMPLE_MODE", StringComparison.Ordinal);
        var execIndex = script.IndexOf("exec \"Example\" \"$@\"", StringComparison.Ordinal);

        Assert.True(customCodeIndex >= 0);
        Assert.True(execIndex > customCodeIndex);
        Assert.Contains(
            "export EXAMPLE_MODE=\"portable\"\necho \"Starting ${DIR}\"\n\nexec",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "org.example.app", "1.0.0")]
    [InlineData("Example", "", "1.0.0")]
    [InlineData("Example", "org.example.app", "")]
    public void GetInfoPList_MissingRequiredValue_ThrowsArgumentException(
        string productName,
        string bundleIdentifier,
        string version)
    {
        var options = new MacAppBundleOptions
        {
            ProductName = productName,
            BundleIdentifier = bundleIdentifier,
            Version = version
        };

        Assert.Throws<ArgumentException>(() => MacAppBundle.GetInfoPList(options));
    }

    private static string GetDictionaryValue(XDocument document, string key)
    {
        return FindDictionaryValue(document, key) ?? throw new Xunit.Sdk.XunitException($"Missing plist key '{key}'.");
    }

    private static MacAppBundleOptions CreateOptions()
    {
        return new MacAppBundleOptions
        {
            ProductName = "Example",
            BundleIdentifier = "org.example.app",
            Version = "1.0.0"
        };
    }

    private static string? FindDictionaryValue(XDocument document, string key)
    {
        var keyElement = document.Descendants("key").SingleOrDefault(element => element.Value == key);
        var valueElement = keyElement?.ElementsAfterSelf().FirstOrDefault();

        return valueElement?.Name.LocalName switch
        {
            "true" => "true",
            "false" => "false",
            _ => valueElement?.Value
        };
    }
}