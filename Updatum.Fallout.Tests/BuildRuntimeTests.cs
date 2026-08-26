using System;
using System.Text.Json;
using StageKit.Runtime;
using Xunit;

namespace Updatum.Fallout.Tests;

/// <summary>
/// Verifies runtime manifest construction and serialization behavior.
/// </summary>
public class BuildRuntimeTests
{
    /// <summary>
    /// Verifies that construction populates required values and environment-derived defaults.
    /// </summary>
    [Fact]
    public void Constructor_RequiredValuesAndDefaults_ArePopulated()
    {
        var before = DateTime.UtcNow;
        var runtime = new BuildRuntime("linux-x64", "2.3.4", true,
            ApplicationBundleType.LinuxAppImage);

        Assert.Equal("linux-x64", runtime.Runtime);
        Assert.Equal("2.3.4", runtime.BuildVersion);
        Assert.True(runtime.IsBundle);
        Assert.Equal(ApplicationBundleType.LinuxAppImage, runtime.BundleType);
        Assert.InRange(runtime.BuildDateTimeUtc, before, DateTime.UtcNow);
        Assert.False(string.IsNullOrWhiteSpace(runtime.BuildOSDescription));
    }

    /// <summary>
    /// Verifies that JSON serialization writes the bundle type as a string.
    /// </summary>
    [Fact]
    public void JsonSerialization_BundleType_IsWrittenAsString()
    {
        var runtime = new BuildRuntime("win-x64", "1.0.0", true,
            ApplicationBundleType.WindowsInstaller);

        var json = JsonSerializer.Serialize(runtime);

        Assert.Contains("\"BundleType\":\"Installer\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the .NET single-file bundle type is serialized by name.
    /// </summary>
    [Fact]
    public void JsonSerialization_DotNetSingleFileApp_IsWrittenAsString()
    {
        var runtime = new BuildRuntime("win-x64", "1.0.0", true,
            ApplicationBundleType.DotNetSingleFile);

        var json = JsonSerializer.Serialize(runtime);

        Assert.Contains("\"BundleType\":\"DotNetSingleFileApp\"", json, StringComparison.Ordinal);
    }
}