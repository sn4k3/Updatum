using System;
using System.IO;
using System.IO.Compression;
using Fallout.Common.IO;
using StageKit.Runtime;
using Xunit;

namespace Updatum.Fallout.Tests;

/// <summary>
/// Verifies pure runtime publishing utility behavior.
/// </summary>
public class PublishUtilitiesTests
{
    /// <summary>
    /// Verifies that supported runtime identifiers map to their expected bundle platforms.
    /// </summary>
    /// <param name="rid">The runtime identifier to parse.</param>
    /// <param name="expectedFamily">The expected operating system family.</param>
    /// <param name="expectedArchitecture">The expected normalized architecture.</param>
    /// <param name="expectedInstallerPlatform">The expected Windows installer platform.</param>
    /// <param name="expectedAppImageArchitecture">The expected AppImage architecture.</param>
    [Theory]
    [InlineData("win-x64", "Windows", "x64", "x64", null)]
    [InlineData("win-arm64", "Windows", "arm64", "arm64", null)]
    [InlineData("win-x86", "Windows", "x86", "x86", null)]
    [InlineData("osx-x64", "MacOS", "x64", null, null)]
    [InlineData("osx-arm64", "MacOS", "arm64", null, null)]
    [InlineData("linux-x64", "Linux", "x64", null, "x86_64")]
    [InlineData("linux-arm64", "Linux", "arm64", null, "aarch64")]
    [InlineData("unix-x64", "Linux", "x64", null, "x86_64")]
    [InlineData("unix-arm64", "Linux", "arm64", null, "aarch64")]
    public void ParseRuntimeIdentifier_KnownRid_MapsFamilyAndArchitectures(
        string rid,
        string expectedFamily,
        string expectedArchitecture,
        string? expectedInstallerPlatform,
        string? expectedAppImageArchitecture)
    {
        var parsed = PublishUtilities.ParseRuntimeIdentifier(rid);

        Assert.Equal(rid, parsed.RuntimeIdentifier);
        Assert.Equal(expectedFamily, parsed.Family.ToString());
        Assert.Equal(expectedArchitecture, parsed.Architecture);
        Assert.Equal(expectedInstallerPlatform, parsed.InstallerPlatform);
        Assert.Equal(expectedAppImageArchitecture, parsed.AppImageArchitecture);
    }

    /// <summary>
    /// Verifies that a compound runtime identifier uses its last hyphen for the architecture.
    /// </summary>
    [Fact]
    public void ParseRuntimeIdentifier_CompoundLinuxRid_PreservesOriginalIdentifier()
    {
        var parsed = PublishUtilities.ParseRuntimeIdentifier("linux-musl-x64");

        Assert.Equal("linux-musl-x64", parsed.RuntimeIdentifier);
        Assert.Equal(PublishRidFamily.Linux, parsed.Family);
        Assert.Equal("x64", parsed.Architecture);
        Assert.Equal("x86_64", parsed.AppImageArchitecture);
    }

    /// <summary>
    /// Verifies that runtime validation rejects blank and case-insensitively duplicate identifiers.
    /// </summary>
    [Fact]
    public void ValidateRuntimeIdentifiers_BlankIdentifier_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishUtilities.ValidateRuntimeIdentifiers(["", "win-x64"]));

        Assert.Contains("blank", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that runtime validation rejects case-insensitively duplicate identifiers.
    /// </summary>
    [Fact]
    public void ValidateRuntimeIdentifiers_CaseInsensitiveDuplicate_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishUtilities.ValidateRuntimeIdentifiers(["win-x64", "WIN-X64"]));

        Assert.Contains("WIN-X64", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that malformed and unknown runtime identifiers identify the invalid value.
    /// </summary>
    [Theory]
    [InlineData("linux")]
    [InlineData("freebsd-x64")]
    [InlineData("win-../../target-x64")]
    [InlineData("linux-..\\target-x64")]
    public void ParseRuntimeIdentifier_InvalidRid_ThrowsInvalidOperationException(string rid)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishUtilities.ParseRuntimeIdentifier(rid));

        Assert.Contains(rid, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that unsupported bundle architectures identify the invalid runtime.
    /// </summary>
    [Theory]
    [InlineData("win-riscv64")]
    [InlineData("linux-x86")]
    [InlineData("osx-x86")]
    public void ParseRuntimeIdentifier_UnsupportedBundleArchitecture_ThrowsInvalidOperationException(string rid)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PublishUtilities.ParseRuntimeIdentifier(rid));

        Assert.Contains(rid, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the first level-two changelog section is returned without leading or trailing blank lines.
    /// </summary>
    [Fact]
    public void ExtractLatestReleaseNotes_FirstReleaseSection_ReturnsTrimmedBody()
    {
        var changelog = """
                        # Changelog

                        ## 2.0.0

                        Added publishing.

                        Fixed callbacks.

                        ## 1.0.0
                        Old notes.
                        """;

        var releaseNotes = PublishUtilities.ExtractLatestReleaseNotes(changelog);

        Assert.Equal("Added publishing.\n\nFixed callbacks.", releaseNotes);
    }

    /// <summary>
    /// Verifies that a changelog without a level-two release heading produces no release notes.
    /// </summary>
    [Fact]
    public void ExtractLatestReleaseNotes_NoReleaseHeading_ReturnsNull()
    {
        Assert.Null(PublishUtilities.ExtractLatestReleaseNotes("# Changelog\nNo releases"));
    }

    /// <summary>
    /// Verifies that installer detection uses only a case-insensitive WiX project extension.
    /// </summary>
    [Theory]
    [InlineData("Installer.WIXPROJ", true)]
    [InlineData("Installer.wixproj", true)]
    [InlineData("Installer.csproj", false)]
    [InlineData("Application.Installer.csproj", false)]
    public void IsWixProject_ProjectPath_UsesOnlyWixProjectExtension(string projectPath, bool expected)
    {
        Assert.Equal(expected, PublishUtilities.IsWixProject(projectPath));
    }

    /// <summary>
    /// Verifies that a macOS app bundle suppresses the colliding portable ZIP artifact.
    /// </summary>
    [Theory]
    [InlineData("win-x64", PublishBundleTypes.MacOSAppBundle, true)]
    [InlineData("osx-x64", (PublishBundleTypes)0, true)]
    [InlineData("osx-x64", PublishBundleTypes.MacOSAppBundle, false)]
    public void ShouldCreatePortableZip_MacOSAppBundleSelection_AvoidsArtifactCollision(
        string rid,
        PublishBundleTypes bundleTypes,
        bool expected)
    {
        var parsed = PublishUtilities.ParseRuntimeIdentifier(rid);

        Assert.Equal(expected, PublishUtilities.ShouldCreatePortableZip(parsed, bundleTypes));
    }

    /// <summary>
    /// Verifies that staging helpers write an indented runtime manifest and archive nested entries.
    /// </summary>
    [Fact]
    public void StagingHelpers_NestedDirectory_WritesManifestAndZipEntries()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"updatum-publish-tests-{Guid.NewGuid():N}");
        var destination = (AbsolutePath)Path.Combine(rootPath, "destination");
        var archive = (AbsolutePath)Path.Combine(rootPath, "archive.zip");

        try
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "destination", "nested"));
            File.WriteAllText(Path.Combine(rootPath, "destination", "nested", "payload.txt"), "payload");

            PublishUtilities.WriteRuntimeManifest(destination, "build-runtime.json",
                new BuildRuntime("linux-x64", "2.0.0", true, ApplicationBundleType.LinuxAppImage));
            PublishUtilities.CreateZip(destination, archive);

            var manifest = File.ReadAllText(Path.Combine(rootPath, "destination", "build-runtime.json"));
            using var zip = ZipFile.OpenRead(archive);

            Assert.Contains("\n  \"Runtime\": \"linux-x64\"", manifest, StringComparison.Ordinal);
            Assert.Contains(zip.Entries, entry => entry.FullName == "nested/payload.txt");
            Assert.Contains(zip.Entries, entry => entry.FullName == "build-runtime.json");
        }
        finally
        {
            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, true);
        }
    }

    /// <summary>
    /// Verifies that a rooted runtime-manifest name cannot write outside the staging directory.
    /// </summary>
    [Fact]
    public void WriteRuntimeManifest_RootedFileName_RejectsPathOutsideStagingDirectory()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"updatum-publish-tests-{Guid.NewGuid():N}");
        var stagingDirectory = (AbsolutePath)Path.Combine(rootPath, "staging");
        var outsidePath = Path.Combine(rootPath, "outside.json");

        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                PublishUtilities.WriteRuntimeManifest(stagingDirectory, outsidePath,
                    new BuildRuntime("linux-x64", "2.0.0")));

            Assert.Equal("fileName", exception.ParamName);
            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, true);
        }
    }

    /// <summary>
    /// Verifies that a traversal runtime-manifest name cannot write outside the staging directory.
    /// </summary>
    [Fact]
    public void WriteRuntimeManifest_TraversalFileName_RejectsPathOutsideStagingDirectory()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"updatum-publish-tests-{Guid.NewGuid():N}");
        var stagingDirectory = (AbsolutePath)Path.Combine(rootPath, "staging");
        var outsidePath = Path.Combine(rootPath, "outside.json");
        var traversalFileName = Path.Combine("..", "outside.json");

        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                PublishUtilities.WriteRuntimeManifest(stagingDirectory, traversalFileName,
                    new BuildRuntime("linux-x64", "2.0.0")));

            Assert.Equal("fileName", exception.ParamName);
            Assert.False(File.Exists(outsidePath));
        }
        finally
        {
            if (Directory.Exists(rootPath))
                Directory.Delete(rootPath, true);
        }
    }
}
