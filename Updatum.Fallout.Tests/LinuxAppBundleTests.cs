using System;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Updatum.Fallout.Tests;

public class LinuxAppBundleTests
{
    [Fact]
    public void Options_AfterConstruction_CanBeModified()
    {
        var options = CreateOptions();

        options.ProductName = "Modified";
        options.ExecutableName = "Modified.Launcher";

        var desktopEntry = LinuxAppBundle.GetDesktopEntry(options);

        Assert.Contains("Name=Modified", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Exec=\"Modified.Launcher\"", desktopEntry, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDesktopEntry_CustomOptions_ProducesEscapedDesktopMetadata()
    {
        var options = CreateOptions();
        options.ProductName = "Example\nTool";
        options.Summary = "First line\nSecond line";
        options.ExecutableName = "Example Tool";
        options.IconName = "example-icon";
        options.Categories = ["Utility", "Development"];
        options.Keywords = ["bundle", "cross platform"];
        options.Terminal = true;
        options.SingleMainWindow = true;

        var desktopEntry = LinuxAppBundle.GetDesktopEntry(options);

        Assert.Contains("Name=Example\\nTool", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Comment=First line\\nSecond line", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Categories=Utility;Development;", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Keywords=bundle;cross platform;", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Icon=example-icon", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Exec=\"Example Tool\"", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("Terminal=true", desktopEntry, StringComparison.Ordinal);
        Assert.Contains("SingleMainWindow=true", desktopEntry, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAppRunScript_CustomExecutable_EscapesExecutableAndForwardsArguments()
    {
        var options = CreateOptions();
        options.ExecutableName = "Example $Tool\"";

        var script = LinuxAppBundle.GetAppRunScript(options);

        Assert.StartsWith("#!/usr/bin/env bash", script, StringComparison.Ordinal);
        Assert.Contains("exec \"Example \\$Tool\\\"\" \"$@\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDesktopEntry_ExtraDesktopEntry_AppendsCustomLines()
    {
        var options = CreateOptions();
        options.ExtraDesktopEntry = """
                                    StartupNotify=true
                                    X-GNOME-UsesNotifications=true
                                    """;

        var desktopEntry = LinuxAppBundle.GetDesktopEntry(options);

        Assert.Contains(
            "SingleMainWindow=true\nStartupNotify=true\nX-GNOME-UsesNotifications=true",
            desktopEntry,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetAppRunScript_CustomCode_InsertsCodeImmediatelyBeforeExec()
    {
        var options = CreateOptions();
        options.AppRunScriptBeforeExec = """
                                         export EXAMPLE_MODE="portable"
                                         echo "Starting ${HERE}"
                                         """;

        var script = LinuxAppBundle.GetAppRunScript(options);
        var customCodeIndex = script.IndexOf("export EXAMPLE_MODE", StringComparison.Ordinal);
        var execIndex = script.IndexOf("exec \"Example\" \"$@\"", StringComparison.Ordinal);

        Assert.True(customCodeIndex >= 0);
        Assert.True(execIndex > customCodeIndex);
        Assert.Contains(
            "export EXAMPLE_MODE=\"portable\"\necho \"Starting ${HERE}\"\n\nexec",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GetAppStreamMetadata_CustomContentRating_UsesConfiguredType()
    {
        var options = CreateOptions();
        options.ContentRatingType = "oars-1.1";

        var document = XDocument.Parse(LinuxAppBundle.GetAppStreamMetadata(options));

        Assert.Equal("oars-1.1", document.Descendants("content_rating").Single().Attribute("type")?.Value);
    }

    [Fact]
    public void GetAppStreamMetadata_DefaultContentRating_UsesOarsOnePointZero()
    {
        var document = XDocument.Parse(LinuxAppBundle.GetAppStreamMetadata(CreateOptions()));

        Assert.Equal("oars-1.0", document.Descendants("content_rating").Single().Attribute("type")?.Value);
    }

    [Fact]
    public void GetAppStreamMetadata_ScreenshotsAndEmail_ProducesOrderedEscapedMetadata()
    {
        var options = CreateOptions();
        options.ProductName = "Example & Tool";
        options.Summary = "A summary that spans\nmultiple lines and remains concise for an application store listing.";
        options.Description = "Creates <portable> bundles & metadata.";
        options.ScreenshotUrls =
        [
            "https://example.com/screens/main.png?size=large&theme=dark",
            "https://example.com/screens/options.png"
        ];
        options.UpdateContact = "maintainer@example.com";

        var metadata = LinuxAppBundle.GetAppStreamMetadata(options);
        var document = XDocument.Parse(metadata);
        var screenshotElements = document.Descendants("screenshot").ToArray();

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", metadata, StringComparison.Ordinal);
        Assert.Equal("Example & Tool", document.Descendants("name").Single().Value);
        Assert.Equal("Creates <portable> bundles & metadata.",
            document.Descendants("description").Single().Element("p")?.Value);
        Assert.Equal("maintainer@example.com", document.Descendants("update_contact").Single().Value);
        Assert.Equal(2, screenshotElements.Length);
        Assert.Equal("default", screenshotElements[0].Attribute("type")?.Value);
        Assert.Equal("https://example.com/screens/main.png?size=large&theme=dark",
            screenshotElements[0].Element("image")?.Value);
        Assert.Null(screenshotElements[1].Attribute("type"));
        Assert.Equal("https://example.com/screens/options.png", screenshotElements[1].Element("image")?.Value);
        Assert.DoesNotContain('\n', document.Descendants("summary").Single().Value);
        Assert.True(document.Descendants("summary").Single().Value.Length <= 78);
    }

    [Fact]
    public void GetAppStreamMetadata_EmptyOptionalValues_OmitsScreenshotsAndEmail()
    {
        var options = CreateOptions();
        options.ScreenshotUrls = [];
        options.UpdateContact = null;

        var document = XDocument.Parse(LinuxAppBundle.GetAppStreamMetadata(options));

        Assert.Empty(document.Descendants("screenshots"));
        Assert.Empty(document.Descendants("update_contact"));
    }

    [Fact]
    public void GetFlatpakManifest_CustomOptions_UsesRuntimePermissionsAndExecutable()
    {
        var options = CreateOptions();
        options.ExecutableName = "Example Tool";
        options.FlatpakRuntime = "org.example.Platform";
        options.FlatpakRuntimeVersion = "1.2";
        options.FlatpakSdk = "org.example.Sdk";
        options.FlatpakFinishArguments = ["--socket=wayland", "--share=network"];

        var manifest = LinuxAppBundle.GetFlatpakManifest(options);

        Assert.Contains("app-id: 'org.example.app'", manifest, StringComparison.Ordinal);
        Assert.Contains("runtime: 'org.example.Platform'", manifest, StringComparison.Ordinal);
        Assert.Contains("runtime-version: '1.2'", manifest, StringComparison.Ordinal);
        Assert.Contains("sdk: 'org.example.Sdk'", manifest, StringComparison.Ordinal);
        Assert.Contains("command: 'Example Tool'", manifest, StringComparison.Ordinal);
        Assert.Contains("  - '--socket=wayland'", manifest, StringComparison.Ordinal);
        Assert.Contains("  - '--share=network'", manifest, StringComparison.Ordinal);
        Assert.Contains("chmod +x", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAppStreamMetadata_InvalidScreenshotUrl_ThrowsArgumentException()
    {
        var options = CreateOptions();
        options.ScreenshotUrls = ["relative/screenshot.png"];

        Assert.Throws<ArgumentException>(() => LinuxAppBundle.GetAppStreamMetadata(options));
    }

    [Theory]
    [InlineData("", "org.example.app", "Example")]
    [InlineData("Example", "", "Example")]
    [InlineData("Example", "org.example.app", "")]
    public void GetDesktopEntry_MissingRequiredValue_ThrowsArgumentException(
        string productName,
        string applicationId,
        string executableName)
    {
        var options = CreateOptions();
        options.ProductName = productName;
        options.ApplicationId = applicationId;
        options.ExecutableName = executableName;

        Assert.Throws<ArgumentException>(() => LinuxAppBundle.GetDesktopEntry(options));
    }

    private static LinuxAppBundleOptions CreateOptions()
    {
        return new LinuxAppBundleOptions
        {
            ApplicationId = "org.example.app",
            ProductName = "Example",
            Summary = "Creates portable application bundles.",
            Description = "A reusable example application.",
            License = "MIT",
            RepositoryUrl = "https://example.com/project",
            Authors = "Example Authors"
        };
    }
}
