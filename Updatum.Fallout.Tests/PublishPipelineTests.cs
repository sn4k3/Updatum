using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using Fallout.Solutions;
using StageKit.Runtime;
using Xunit;

namespace Updatum.Fallout.Tests;

/// <summary>
/// Verifies the configurable per-runtime publish pipeline.
/// </summary>
public class PublishPipelineTests
{
    /// <summary>
    /// Verifies that runnable-project detection survives the known in-process NuGet assembly conflict.
    /// </summary>
    [Fact]
    public void IsRunnableProject_NuGetFrameworksConflict_UsesFalloutMSBuildProjectEvaluation()
    {
        var build = new TestBuild
        {
            ThrowNuGetFrameworksFromProjectEvaluation = true,
            TestMSBuildProjectProperties = new Dictionary<string, string?>
            {
                ["OutputType"] = "Exe"
            }
        };
        var project = CreateProject("Example.csproj");

        Assert.True(build.InvokeIsRunnableProject(project));
        Assert.Equal(1, build.MSBuildProjectEvaluationCalls);
        Assert.Equal(0, build.ExternalProjectEvaluationCalls);
    }

    /// <summary>
    /// Verifies that repeated in-process NuGet assembly conflicts advance to one cached isolated evaluation.
    /// </summary>
    [Fact]
    public void IsRunnableProject_AllInProcessEvaluatorsConflict_UsesCachedExternalEvaluation()
    {
        var build = new TestBuild
        {
            ThrowNuGetFrameworksFromProjectEvaluation = true,
            ThrowNuGetFrameworksFromMSBuildProjectEvaluation = true,
            TestExternallyEvaluatedProjectProperties = new Dictionary<string, string?>
            {
                ["OutputType"] = "Exe"
            }
        };
        var project = CreateProject("Example.csproj");

        Assert.True(build.InvokeIsRunnableProject(project));
        Assert.True(build.InvokeIsRunnableProject(project));
        Assert.Equal(2, build.MSBuildProjectEvaluationCalls);
        Assert.Equal(1, build.ExternalProjectEvaluationCalls);
    }

    /// <summary>
    /// Verifies that diagnostic output includes all public build variables without materializing targets.
    /// </summary>
    [Fact]
    public void GetPrintVariables_PublicBuildState_IncludesVariablesAndExcludesTargets()
    {
        var build = new TestBuild();
        build.ConfigurePublishTarget(["linux-x64", "win-x64"], false, false);

        var variables = build.InvokeGetPrintVariables();

        Assert.Contains(nameof(UpdatumBuild.RootDirectory), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.Configuration), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.SoftwareName), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.PublishBundles), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.PublishCleanupExtensions), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.AssetName), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.MacAppBundleOptions), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.LinuxAppBundleOptions), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.BeforePublishRid), variables.Keys);
        Assert.Contains(nameof(UpdatumBuild.AfterPublishRid), variables.Keys);
        Assert.Equal("[linux-x64, win-x64]", variables[nameof(UpdatumBuild.RIds)]);
        Assert.DoesNotContain(nameof(UpdatumBuild.Print), variables.Keys);
        Assert.DoesNotContain(nameof(UpdatumBuild.Publish), variables.Keys);
        Assert.DoesNotContain("AbortedTargets", variables.Keys);
        Assert.DoesNotContain("ExecutionPlan", variables.Keys);
    }

    /// <summary>
    /// Verifies that an undefined MSBuild summary falls back to the software description.
    /// </summary>
    [Fact]
    public void SoftwareSummary_EmptyProjectValue_UsesSoftwareDescription()
    {
        var build = new TestBuild
        {
            TestMainProject = CreateProject("Example.csproj"),
            TestMainProjectProperties = new Dictionary<string, string?>
            {
                ["Summary"] = string.Empty,
                ["Description"] = "Fallback description"
            }
        };

        Assert.Equal("Fallback description", build.SoftwareSummary);
    }

    /// <summary>
    /// Verifies that the default configuration enables every Task 3 bundle type.
    /// </summary>
    [Fact]
    public void PublishBundles_DefaultConfiguration_EnablesAllTaskThreeBundles()
    {
        var build = new TestBuild();

        Assert.True(build.PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable));
        Assert.True(build.PublishBundles.HasFlag(PublishBundleTypes.WindowsInstaller));
        Assert.True(build.PublishBundles.HasFlag(PublishBundleTypes.MacOSAppBundle));
        Assert.True(build.PublishBundles.HasFlag(PublishBundleTypes.LinuxAppImage));
    }

    /// <summary>
    /// Verifies that runtime callbacks and publish boundaries execute in the documented order.
    /// </summary>
    [Fact]
    public void PublishRuntime_ConfiguredCallbacks_ExecutesInOrder()
    {
        var build = new TestBuild();
        var context = CreateContext(build);
        build.BeforePublishRid = _ => build.Calls.Add("before");
        build.ConfigurePublishRid = (settings, _) =>
        {
            build.Calls.Add("configure");
            return settings;
        };
        build.AfterPublishRid = _ => build.Calls.Add("after");

        build.InvokePublishRuntime(context);

        Assert.Equal(["before", "settings", "configure", "publish", "prepare", "after"], build.Calls);
    }

    /// <summary>
    /// Verifies that a callback cannot replace publish settings with a null value.
    /// </summary>
    [Fact]
    public void PublishRuntime_ConfigureCallbackReturnsNull_ThrowsBeforePublish()
    {
        var build = new TestBuild();
        var context = CreateContext(build);
        build.BeforePublishRid = _ => build.Calls.Add("before");
        build.ConfigurePublishRid = (_, _) =>
        {
            build.Calls.Add("configure");
            return null!;
        };

        var exception = Assert.Throws<InvalidOperationException>(() => build.InvokePublishRuntime(context));

        Assert.Equal("ConfigurePublishRid returned null.", exception.Message);
        Assert.Equal(["before", "settings", "configure"], build.Calls);
    }

    /// <summary>
    /// Verifies that preparing a multi-file output writes a non-bundle runtime manifest.
    /// </summary>
    [Fact]
    public void PreparePublishedOutput_MultiFilePublish_WritesNonBundleRuntimeManifest()
    {
        var publishDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishDirectory);

        try
        {
            var build = new TestBuild
            {
                TestBuildRuntimeCacheFileName = "runtime.json",
                UseDefaultPreparation = true
            };
            build.SetPublishBundles((PublishBundleTypes)0);
            var context = new PublishRidContext
            {
                Build = build,
                RuntimeIdentifier = "linux-x64",
                PublishPath = (AbsolutePath)publishDirectory
            };
            var executablePath = Path.Combine(publishDirectory, build.SoftwareName);
            File.WriteAllText(executablePath, string.Empty);

            build.InvokePreparePublishedOutput(context);

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(publishDirectory, "runtime.json")));
            Assert.Equal("linux-x64", document.RootElement.GetProperty("Runtime").GetString());
            Assert.False(document.RootElement.GetProperty("IsBundle").GetBoolean());
            Assert.Equal("None", document.RootElement.GetProperty("BundleType").GetString());
            Assert.Equal("1.2.3", document.RootElement.GetProperty("BuildVersion").GetString());

            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(executablePath);
                Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
                Assert.True(mode.HasFlag(UnixFileMode.GroupExecute));
                Assert.True(mode.HasFlag(UnixFileMode.OtherExecute));
            }
        }
        finally
        {
            Directory.Delete(publishDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a single-file publish does not leave an external runtime manifest.
    /// </summary>
    [Fact]
    public void PreparePublishedOutput_SingleFilePublish_DoesNotWriteExternalRuntimeManifest()
    {
        var publishDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(publishDirectory);

        try
        {
            var build = new TestBuild
            {
                TestBuildRuntimeCacheFileName = "runtime.json",
                UseDefaultPreparation = true
            };
            var context = CreateContext(build, "win-x64", publishDirectory);

            build.InvokePreparePublishedOutput(context);

            Assert.False(File.Exists(Path.Combine(publishDirectory, "runtime.json")));
        }
        finally
        {
            Directory.Delete(publishDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that default publish settings preserve the configured project, RID, output, and required flags.
    /// </summary>
    [Fact]
    public void CreatePublishSettings_Defaults_UseProjectRuntimeOutputAndRequiredFlags()
    {
        var build = new TestBuild
        {
            UseDefaultSettings = true,
            TestMainProject = CreateProject("Example.csproj")
        };
        var context = CreateContext(build);

        var settings = build.InvokeCreatePublishSettings(context);

        Assert.Equal(build.TestMainProject.Path, settings.Project);
        Assert.Equal("Release", settings.Configuration);
        Assert.Equal("linux-x64", settings.Runtime);
        Assert.Equal(context.PublishPath, settings.Output);
        Assert.True(settings.SelfContained);
        Assert.True(settings.NoRestore);
        Assert.True(Assert.IsType<JsonElement>(settings.Properties["PublishReadyToRun"]).GetBoolean());
        Assert.True(Assert.IsType<JsonElement>(settings.Properties["PublishSingleFile"]).GetBoolean());
        Assert.Equal("embedded", Assert.IsType<JsonElement>(settings.Properties["DebugType"]).GetString());
        Assert.False(Assert.IsType<JsonElement>(settings.Properties["PublishDocumentationFiles"]).GetBoolean());
        //Assert.True(Assert.IsType<JsonElement>(settings.Properties["IncludeAllContentForSelfExtract"]).GetBoolean());
        Assert.True(
            Assert.IsType<JsonElement>(settings.Properties["IncludeNativeLibrariesForSelfExtract"]).GetBoolean());
    }

    /// <summary>
    /// Verifies that single-file publishing injects the runtime manifest into the SDK bundle and cleans temporary inputs.
    /// </summary>
    [Fact]
    public void PublishRuntime_SingleFile_InjectsRuntimeManifestAndCleansTemporaryInputs()
    {
        var build = new TestBuild
        {
            CaptureSingleFileInputs = true,
            UseDefaultSettings = true,
            TestMainProject = CreateProject("Example.csproj")
        };
        var context = CreateContext(build, "win-x64");

        build.InvokePublishRuntime(context);

        using var manifest = JsonDocument.Parse(build.CapturedRuntimeManifest);
        Assert.Equal("win-x64", manifest.RootElement.GetProperty("Runtime").GetString());
        Assert.True(manifest.RootElement.GetProperty("IsBundle").GetBoolean());
        Assert.Equal(nameof(ApplicationBundleType.DotNetSingleFile),
            manifest.RootElement.GetProperty("BundleType").GetString());
        Assert.Contains("<Content Include=\"$(FalloutBuildRuntimeManifest)\"",
            build.CapturedSingleFileTargets);
        Assert.Contains("<ExcludeFromSingleFile>false</ExcludeFromSingleFile>",
            build.CapturedSingleFileTargets);
        Assert.False(File.Exists(build.CapturedRuntimeManifestPath));
        Assert.False(File.Exists(build.CapturedSingleFileTargetsPath));
    }

    /// <summary>
    /// Verifies that default publish settings disable single-file publishing when its bundle flag is absent.
    /// </summary>
    [Fact]
    public void CreatePublishSettings_SingleExecutableFlagAbsent_DisablesPublishSingleFile()
    {
        var build = new TestBuild
        {
            UseDefaultSettings = true,
            TestMainProject = CreateProject("Example.csproj")
        };
        build.SetPublishBundles((PublishBundleTypes)0);
        var context = CreateContext(build);

        var settings = build.InvokeCreatePublishSettings(context);

        Assert.False(Assert.IsType<JsonElement>(settings.Properties["PublishSingleFile"]).GetBoolean());
    }

    /// <summary>
    /// Verifies that MSI staging uses a normal publish when single-file and installer bundles are both enabled.
    /// </summary>
    [Fact]
    public void CreateInstallerPublishSettings_SingleFileEnabled_DisablesSingleFileForInstallerPayload()
    {
        var build = new TestBuild
        {
            UseDefaultSettings = true,
            TestMainProject = CreateProject("Example.csproj")
        };
        build.SetPublishBundles(PublishBundleTypes.DotNetSingleExecutable | PublishBundleTypes.WindowsInstaller);
        var context = CreateContext(build, "win-x64");
        var installerOutput = (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");

        var settings = build.InvokeCreateInstallerPublishSettings(context, installerOutput);

        Assert.False(Assert.IsType<JsonElement>(settings.Properties["PublishSingleFile"]).GetBoolean());
        Assert.Equal(installerOutput, settings.Output);
    }

    /// <summary>
    /// Verifies that the default installer predicate only accepts WiX project paths.
    /// </summary>
    [Fact]
    public void IsInstallerProject_WixProjectExtension_IsDetectedCaseInsensitively()
    {
        var build = new TestBuild();

        Assert.True(build.InvokeIsInstallerProject(CreateProject("installer.WIXPROJ")));
        Assert.False(build.InvokeIsInstallerProject(CreateProject("installer.csproj")));
    }

    /// <summary>
    /// Verifies that a missing runtime-cache property uses the package default file name.
    /// </summary>
    [Fact]
    public void BuildRuntimeCacheFileName_ProjectDoesNotDefineValue_UsesDefaultFileName()
    {
        var build = new TestBuild
        {
            UseDefaultBuildRuntimeCacheFileName = true,
            RuntimeCacheFileNameFromMainProject = null
        };

        Assert.Equal("build-runtime.json", build.BuildRuntimeCacheFileName);
    }

    /// <summary>
    /// Verifies that a blank runtime-cache property uses the package default file name.
    /// </summary>
    [Fact]
    public void BuildRuntimeCacheFileName_ProjectDefinesWhitespace_UsesDefaultFileName()
    {
        var build = new TestBuild
        {
            UseDefaultBuildRuntimeCacheFileName = true,
            RuntimeCacheFileNameFromMainProject = "  "
        };

        Assert.Equal("build-runtime.json", build.BuildRuntimeCacheFileName);
    }

    /// <summary>
    /// Verifies that publishing uses deterministic RID paths and creates bundles after all RIDs succeed.
    /// </summary>
    [Fact]
    public void ExecutePublish_ConfiguredRids_PublishesInOrderThenCreatesBundles()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, false, "linux-x64", "win-x64");
            build.RecordRestoreCalls = true;
            var stalePath = Path.Combine(rootDirectory, "TestApp_linux-x64_v1.2.3");
            Directory.CreateDirectory(stalePath);
            File.WriteAllText(Path.Combine(stalePath, "stale.txt"), "stale");

            build.InvokeExecutePublish();

            Assert.Equal(["linux-x64", "win-x64"], build.RestoredRuntimeIdentifiers);
            Assert.Equal(
            [
                "restore:linux-x64",
                "publish:linux-x64:TestApp_linux-x64_v1.2.3",
                "restore:win-x64",
                "publish:win-x64:TestApp_win-x64_v1.2.3",
                "bundles"
            ], build.Calls);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that successful publishing removes top-level WiX debug symbols without touching other files.
    /// </summary>
    [Fact]
    public void ExecutePublish_WixDebugSymbolsExist_RemovesOnlyTopLevelWixpdbFiles()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var nestedDirectory = Path.Combine(rootDirectory, "diagnostics");
        Directory.CreateDirectory(nestedDirectory);
        var wixDebugSymbol = Path.Combine(rootDirectory, "installer.wixpdb");
        var mixedCaseWixDebugSymbol = Path.Combine(rootDirectory, "installer.WIXPDB");
        var portableDebugSymbol = Path.Combine(rootDirectory, "application.pdb");
        var nestedWixDebugSymbol = Path.Combine(nestedDirectory, "nested.wixpdb");
        File.WriteAllText(wixDebugSymbol, "wix");
        File.WriteAllText(mixedCaseWixDebugSymbol, "wix");
        File.WriteAllText(portableDebugSymbol, "pdb");
        File.WriteAllText(nestedWixDebugSymbol, "nested");

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, false, "win-x64");
            Assert.Equal(["wixpdb"], build.PublishCleanupExtensions);

            build.InvokeExecutePublish();

            Assert.False(File.Exists(wixDebugSymbol));
            Assert.False(File.Exists(mixedCaseWixDebugSymbol));
            Assert.True(File.Exists(portableDebugSymbol));
            Assert.True(File.Exists(nestedWixDebugSymbol));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that publishing removes every configured top-level file extension.
    /// </summary>
    [Fact]
    public void ExecutePublish_CustomCleanupExtensions_RemovesMatchingFiles()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);
        var wixDebugSymbol = Path.Combine(rootDirectory, "installer.wixpdb");
        var portableDebugSymbol = Path.Combine(rootDirectory, "application.pdb");
        var retainedFile = Path.Combine(rootDirectory, "release.json");
        File.WriteAllText(wixDebugSymbol, "wix");
        File.WriteAllText(portableDebugSymbol, "pdb");
        File.WriteAllText(retainedFile, "json");

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, false, "win-x64");
            build.SetPublishCleanupExtensions("wixpdb", "pdb");

            build.InvokeExecutePublish();

            Assert.False(File.Exists(wixDebugSymbol));
            Assert.False(File.Exists(portableDebugSymbol));
            Assert.True(File.Exists(retainedFile));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies unsafe artifact metadata is rejected before restore or publish cleanup runs.
    /// </summary>
    /// <param name="propertyName">The build property to configure.</param>
    [Theory]
    [InlineData("SoftwareName")]
    [InlineData("SoftwareVersion")]
    [InlineData("BuildRuntimeCacheFileName")]
    public void ExecutePublish_UnsafePathComponent_ThrowsBeforeRestore(string propertyName)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, false, "win-x64");
            switch (propertyName)
            {
                case "SoftwareName":
                    build.TestSoftwareName = "../outside";
                    break;
                case "SoftwareVersion":
                    build.TestSoftwareVersion = "../outside";
                    break;
                case "BuildRuntimeCacheFileName":
                    build.TestBuildRuntimeCacheFileName = "../outside.json";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null);
            }

            var exception = Assert.Throws<InvalidOperationException>(() => build.InvokeExecutePublish());

            Assert.Contains(propertyName, exception.Message, StringComparison.Ordinal);
            Assert.Empty(build.RestoredRuntimeIdentifiers);
            Assert.Empty(build.Calls);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a latest changelog section is written to the release-notes file before publishing.
    /// </summary>
    [Fact]
    public void ExecutePublish_LatestReleaseNotesExist_WritesReleaseNotesFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, false, "linux-x64");
            File.WriteAllText(build.TestChangelogFile,
                "# Changelog\n\n## 2.0.0\n\nNew release.\n\n## 1.0.0\nOld release.");

            build.InvokeExecutePublish();

            Assert.Equal("New release.", File.ReadAllText(build.TestReleaseNotesFile));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that raw publish output is retained when bundle creation fails.
    /// </summary>
    [Fact]
    public void ExecutePublish_BundleCreationFails_RetainsRawOutput()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, true, false, "linux-x64");
            build.ThrowWhenCreatingBundles = true;

            Assert.Throws<InvalidOperationException>(() => build.InvokeExecutePublish());

            Assert.True(Directory.Exists(Path.Combine(rootDirectory, "TestApp_linux-x64_v1.2.3")));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that raw output is discarded only after all selected bundle work succeeds.
    /// </summary>
    [Fact]
    public void ExecutePublish_DiscardNonBundlesEnabled_DeletesRawOutputAfterBundles()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, true, false, "linux-x64");

            build.InvokeExecutePublish();

            Assert.Contains("bundles", build.Calls);
            Assert.False(Directory.Exists(Path.Combine(rootDirectory, "TestApp_linux-x64_v1.2.3")));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that the global no-bundles option suppresses bundle dispatch.
    /// </summary>
    [Fact]
    public void ExecutePublish_NoBundlesEnabled_DoesNotCreateBundles()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, true, "linux-x64");

            build.InvokeExecutePublish();

            Assert.DoesNotContain("bundles", build.Calls);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that single-file publishing removes portable ZIPs left by earlier Fallout versions.
    /// </summary>
    [Fact]
    public void ExecutePublish_SingleFile_RemovesLegacyPortableZip()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, true, "win-x64");
            var archivePath = Path.Combine(rootDirectory, "TestApp_win-x64_v1.2.3.zip");
            File.WriteAllText(archivePath, "legacy");

            build.InvokeExecutePublish();

            Assert.False(File.Exists(archivePath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a Windows single-file executable is copied beside the other publish assets.
    /// </summary>
    [Fact]
    public void ExecutePublish_SingleFileWindows_CopiesExecutableBesidePublishDirectory()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, true, "win-x64");

            build.InvokeExecutePublish();

            var assetPath = Path.Combine(rootDirectory, "TestApp_win-x64_v1.2.3.exe");
            Assert.Equal("win-x64", File.ReadAllText(assetPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that an extensionless Unix single-file executable uses a collision-free adjacent asset suffix.
    /// </summary>
    [Fact]
    public void ExecutePublish_SingleFileLinux_CopiesExecutableBesidePublishDirectoryAsBin()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, true, "linux-x64");

            build.InvokeExecutePublish();

            var assetPath = Path.Combine(rootDirectory, "TestApp_linux-x64_v1.2.3.bin");
            Assert.Equal("linux-x64", File.ReadAllText(assetPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that the default asset-name convention includes the software, runtime, and version.
    /// </summary>
    [Fact]
    public void AssetName_DefaultConvention_UsesSoftwareRuntimeAndVersion()
    {
        var build = new TestBuild();
        var context = CreateContext(build, "linux-x64");

        Assert.Equal("TestApp_linux-x64_v1.2.3", build.AssetName(context));
    }

    /// <summary>
    /// Verifies that a custom asset-name convention controls both the raw output and adjacent executable.
    /// </summary>
    [Fact]
    public void ExecutePublish_CustomAssetName_UsesReturnedNameForPublishArtifacts()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, true, "win-x64");
            var invocationCount = 0;
            build.AssetName = context =>
            {
                invocationCount++;
                return $"custom-{context.RuntimeIdentifier}";
            };

            build.InvokeExecutePublish();

            Assert.Equal(1, invocationCount);
            Assert.Contains("publish:win-x64:custom-win-x64", build.Calls);
            Assert.Equal("win-x64", File.ReadAllText(Path.Combine(rootDirectory, "custom-win-x64.exe")));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that unsafe custom asset names are rejected before restore or publish starts.
    /// </summary>
    /// <param name="assetName">The unsafe custom asset name.</param>
    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    [InlineData("nested/name")]
    public void ExecutePublish_UnsafeCustomAssetName_ThrowsBeforeRestore(string assetName)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, true, "win-x64");
            build.RecordRestoreCalls = true;
            build.AssetName = _ => assetName;

            var exception = Assert.Throws<InvalidOperationException>(() => build.InvokeExecutePublish());

            Assert.Contains(nameof(UpdatumBuild.AssetName), exception.Message, StringComparison.Ordinal);
            Assert.Empty(build.RestoredRuntimeIdentifiers);
            Assert.Empty(build.Calls);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a changelog without a release section does not overwrite existing release notes.
    /// </summary>
    [Fact]
    public void ExecutePublish_NoReleaseNotesInChangelog_PreservesExistingReleaseNotes()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = CreateTargetBuild(rootDirectory, false, false, "linux-x64");
            File.WriteAllText(build.TestChangelogFile, "# Changelog\nNo releases yet.");
            File.WriteAllText(build.TestReleaseNotesFile, "Existing release notes.");

            build.InvokeExecutePublish();

            Assert.Equal("Existing release notes.", File.ReadAllText(build.TestReleaseNotesFile));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that single-file publishing does not create a redundant portable ZIP.
    /// </summary>
    [Fact]
    public void CreateBundles_SingleExecutableSelected_DoesNotCreatePortableZip()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordPortableZip = true
        };
        build.SetPublishBundles(PublishBundleTypes.DotNetSingleExecutable);
        var contexts = new[]
        {
            CreateContext(build, "win-x64"),
            CreateContext(build, "osx-x64"),
            CreateContext(build, "linux-x64")
        };

        build.InvokeCreateBundles(contexts);

        Assert.Empty(build.Calls);
    }

    /// <summary>
    /// Verifies that selecting portable bundles creates one ZIP for every configured runtime.
    /// </summary>
    [Fact]
    public void CreateBundles_PortableSelected_CreatesZipForEveryRuntime()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordPortableZip = true
        };
        build.SetPublishBundles(PublishBundleTypes.Portable);
        var contexts = new[]
        {
            CreateContext(build, "win-x64"),
            CreateContext(build, "osx-x64"),
            CreateContext(build, "linux-x64")
        };

        build.InvokeCreateBundles(contexts);

        Assert.Equal(["zip:win-x64", "zip:osx-x64", "zip:linux-x64"], build.Calls);
    }

    /// <summary>
    /// Verifies that a macOS app archive is not overwritten by a portable ZIP with the same asset name.
    /// </summary>
    [Fact]
    public void CreateBundles_PortableAndMacOSAppSelected_AvoidsArchiveNameCollision()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordPortableZip = true,
            RecordMacOSApp = true,
            TestUnixHost = true
        };
        build.SetPublishBundles(PublishBundleTypes.Portable |
                                PublishBundleTypes.MacOSAppBundle);

        build.InvokeCreateBundles([CreateContext(build, "osx-x64")]);

        Assert.Equal(["mac:osx-x64"], build.Calls);
    }

    /// <summary>
    /// Verifies that unsupported macOS app creation does not suppress the portable macOS ZIP.
    /// </summary>
    [Fact]
    public void CreateBundles_PortableAndUnsupportedMacOSAppSelected_PreservesPortableZip()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordPortableZip = true,
            RecordMacOSApp = true,
            RecordMacOSWarning = true,
            TestUnixHost = false
        };
        build.SetPublishBundles(PublishBundleTypes.Portable |
                                PublishBundleTypes.MacOSAppBundle);

        build.InvokeCreateBundles([CreateContext(build, "osx-x64")]);

        Assert.Equal(["zip:osx-x64", "mac-warning"], build.Calls);
    }

    /// <summary>
    /// Verifies that portable ZIPs use a temporary normal publish when single-file output is also selected.
    /// </summary>
    [Fact]
    public void CreateBundles_PortableAndSingleFileSelected_UsesTemporaryNormalPayload()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishPath = CreateRawOutput(rootDirectory, "win-x64", "TestApp.exe");

        try
        {
            var build = new TestBuild
            {
                UseDefaultBundlePipeline = true,
                UseDefaultSettings = true,
                RecordPortableZip = true,
                TestMainProject = CreateProject("Example.csproj")
            };
            build.SetPublishBundles(PublishBundleTypes.Portable |
                                    PublishBundleTypes.DotNetSingleExecutable);

            build.InvokeCreateBundles([CreateContext(build, "win-x64", publishPath)]);

            Assert.Equal(["publish", "zip:win-x64"], build.Calls);
            Assert.Equal([publishPath], build.BundleOutputPaths);
            var payloadPath = Assert.Single(build.PortableZipPayloadPaths);
            Assert.NotEqual(Path.GetFullPath(publishPath), Path.GetFullPath(payloadPath));
            Assert.False(Directory.Exists(payloadPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a temporary normal payload retains the original publish path for bundle artifacts.
    /// </summary>
    [Fact]
    public void CreateBundles_SingleFileMacOSPayload_PreservesOriginalBundleOutputPath()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishPath = CreateRawOutput(rootDirectory, "osx-x64");

        try
        {
            var build = new TestBuild
            {
                UseDefaultBundlePipeline = true,
                UseDefaultSettings = true,
                RecordMacOSApp = true,
                TestUnixHost = true,
                TestMainProject = CreateProject("Example.csproj")
            };
            build.SetPublishBundles(PublishBundleTypes.DotNetSingleExecutable |
                                    PublishBundleTypes.MacOSAppBundle);

            build.InvokeCreateBundles([CreateContext(build, "osx-x64", publishPath)]);

            Assert.Equal([publishPath], build.BundleOutputPaths);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that portable ZIP creation replaces only the staged runtime manifest and removes staging.
    /// </summary>
    [Fact]
    public void CreatePortableZip_RawOutput_CreatesIsolatedBundleManifestAndCleansStaging()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = Path.Combine(rootDirectory, "TestApp_win-x64_v1.2.3");
        Directory.CreateDirectory(publishDirectory);

        try
        {
            var build = new TestBuild
            {
                TestPublishDirectory = (AbsolutePath)rootDirectory
            };
            var context = CreateContext(build, "win-x64", publishDirectory);
            File.WriteAllText(Path.Combine(publishDirectory, "raw.txt"), "raw");
            File.WriteAllText(Path.Combine(publishDirectory, "runtime.json"),
                "{\"Runtime\":\"win-x64\",\"IsBundle\":false,\"BundleType\":\"None\"}");

            build.InvokeCreatePortableZip(context);

            var archivePath = $"{publishDirectory}.zip";
            Assert.True(File.Exists(archivePath));
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                Assert.NotNull(archive.GetEntry("raw.txt"));
                var manifestEntry = archive.GetEntry("runtime.json");
                Assert.NotNull(manifestEntry);

                using var manifestStream = manifestEntry.Open();
                using var manifest = JsonDocument.Parse(manifestStream);
                Assert.Equal("win-x64", manifest.RootElement.GetProperty("Runtime").GetString());
                Assert.False(manifest.RootElement.GetProperty("IsBundle").GetBoolean());
                Assert.Equal("None", manifest.RootElement.GetProperty("BundleType").GetString());
            }

            using var rawManifest =
                JsonDocument.Parse(File.ReadAllText(Path.Combine(publishDirectory, "runtime.json")));
            Assert.False(rawManifest.RootElement.GetProperty("IsBundle").GetBoolean());

            var stagingRoot = Path.Combine(UpdatumBuild.TemporaryDirectory, "publish-staging");
            if (Directory.Exists(stagingRoot))
                Assert.Empty(Directory.EnumerateDirectories(stagingRoot));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a macOS app bundle suppresses the colliding portable ZIP artifact.
    /// </summary>
    [Theory]
    [InlineData("win-x64", PublishBundleTypes.MacOSAppBundle, true)]
    [InlineData("osx-x64", PublishBundleTypes.None, true)]
    [InlineData("osx-x64", PublishBundleTypes.MacOSAppBundle, false)]
    public void ShouldCreatePortableZip_MacOSAppBundleSelection_AvoidsArtifactCollision(
        string rid,
        PublishBundleTypes bundleTypes,
        bool expected)
    {
        var build = new TestBuild();
        build.SetPublishBundles(bundleTypes);

        Assert.Equal(expected, build.InvokeShouldCreatePortableZip(rid));
    }

    /// <summary>
    /// Verifies that a portable ZIP made from a temporary normal payload is written beside the original output.
    /// </summary>
    [Fact]
    public void CreatePortableZip_TemporaryPayload_CreatesCompleteArchiveAtBundleOutputPath()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(rootDirectory, "bundle-publish", Guid.NewGuid().ToString("N"));
        var bundleOutputPath = Path.Combine(rootDirectory, "publish", "TestApp_linux-x64_v1.2.3");
        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(bundleOutputPath)!);
        File.WriteAllText(Path.Combine(payloadDirectory, "TestApp"), "executable");
        File.WriteAllText(Path.Combine(payloadDirectory, "dependency.dll"), "dependency");

        try
        {
            var build = new TestBuild();
            var context = new PublishRidContext
            {
                Build = build,
                RuntimeIdentifier = "linux-x64",
                PublishPath = (AbsolutePath)payloadDirectory,
                BundleOutputPath = (AbsolutePath)bundleOutputPath
            };

            build.InvokeCreatePortableZip(context);

            var archivePath = $"{bundleOutputPath}.zip";
            Assert.True(File.Exists(archivePath));
            Assert.False(File.Exists($"{payloadDirectory}.zip"));
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.NotNull(archive.GetEntry("TestApp"));
            Assert.NotNull(archive.GetEntry("dependency.dll"));
            Assert.NotNull(archive.GetEntry(build.TestBuildRuntimeCacheFileName));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies every supported Windows RID maps to the installer platform expected by WiX builds.
    /// </summary>
    [Theory]
    [InlineData("win-x64", "x64")]
    [InlineData("win-arm64", "arm64")]
    [InlineData("win-x86", "x86")]
    public void ParseRuntimeIdentifier_WindowsRid_MapsInstallerPlatform(string runtimeIdentifier,
        string expectedPlatform)
    {
        var runtime = PublishRid.ParseRuntimeIdentifier(runtimeIdentifier);

        Assert.Equal(expectedPlatform, runtime.InstallerPlatform);
    }

    /// <summary>
    /// Verifies Windows installer orchestration invokes every detected project through the protected boundary.
    /// </summary>
    [Fact]
    public void CreateBundles_WindowsInstallersSelected_InvokesAllProjectsThroughProtectedBoundary()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        try
        {
            var build = new TestBuild
            {
                UseDefaultBundlePipeline = true,
                TestPublishDirectory = (AbsolutePath)rootDirectory,
                TestInstallerProjects =
                [
                    CreateProject("First.wixproj"),
                    CreateProject("Second.WIXPROJ")
                ]
            };
            build.SetPublishBundles(PublishBundleTypes.WindowsInstaller);
            var contexts = new[]
            {
                CreateContext(build, "win-x64", CreateRawOutput(rootDirectory, "win-x64")),
                CreateContext(build, "linux-x64", CreateRawOutput(rootDirectory, "linux-x64")),
                CreateContext(build, "win-arm64", CreateRawOutput(rootDirectory, "win-arm64")),
                CreateContext(build, "win-x86", CreateRawOutput(rootDirectory, "win-x86"))
            };

            build.InvokeCreateBundles(contexts);

            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(
                [
                    "installer:win-x64:x64",
                    "installer:win-x64:x64",
                    "installer:win-arm64:arm64",
                    "installer:win-arm64:arm64",
                    "installer:win-x86:x86",
                    "installer:win-x86:x86"
                ], build.Calls);
                Assert.Equal(build.InstallerSourcePaths[0], build.InstallerSourcePaths[1]);
                Assert.Equal(build.InstallerSourcePaths[2], build.InstallerSourcePaths[3]);
                Assert.Equal(build.InstallerSourcePaths[4], build.InstallerSourcePaths[5]);
                Assert.All(build.InstallerSourcePaths, sourcePath => Assert.False(Directory.Exists(sourcePath)));
            }
            else
            {
                Assert.Empty(build.Calls);
            }
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies Linux AppImage selection preserves Linux and Unix RID order and ignores other families.
    /// </summary>
    [Fact]
    public void CreateBundles_LinuxAppImageSelectedOnLinuxHost_DispatchesLinuxAndUnixContextsInOrder()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordLinuxAppImage = true,
            TestLinuxHost = true
        };
        build.SetPublishBundles(PublishBundleTypes.LinuxAppImage);
        var contexts = new[]
        {
            CreateContext(build, "win-x64"),
            CreateContext(build, "linux-arm64"),
            CreateContext(build, "osx-x64"),
            CreateContext(build, "unix-x64")
        };

        build.InvokeCreateBundles(contexts);

        Assert.Equal(["linux:linux-arm64:aarch64", "linux:unix-x64:x86_64"], build.Calls);
    }

    /// <summary>
    /// Verifies a non-Linux host emits one warning and creates no AppImages.
    /// </summary>
    [Fact]
    public void CreateBundles_LinuxAppImageSelectedOnNonLinuxHost_WarnsOnceAndCreatesNoImages()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordLinuxAppImage = true,
            RecordLinuxWarning = true
        };
        build.SetPublishBundles(PublishBundleTypes.LinuxAppImage);

        build.InvokeCreateBundles(
        [
            CreateContext(build, "linux-x64"),
            CreateContext(build, "unix-arm64"),
            CreateContext(build, "win-x64")
        ]);

        Assert.Equal(["linux-warning"], build.Calls);
    }

    /// <summary>
    /// Verifies appimagetool is downloaded and extracted once using protected boundaries and the current upstream URL.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_X64Host_ComposesBoundariesAndReusesArchitectureCache()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var cacheDirectory = Path.Combine(rootDirectory, "appimagetool");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)cacheDirectory,
            TestHostArchitecture = Architecture.X64,
            TestFuseAvailable = false,
            CreateDownloadedFile = true,
            CreateExtractedAppRun = true,
            RecordLinuxWarning = true
        };

        try
        {
            var firstPath = build.InvokePrepareAppImageTool();
            var secondPath = build.InvokePrepareAppImageTool();
            var downloadedPath = Path.Combine(cacheDirectory, "appimagetool-x86_64.AppImage");
            var expectedAppRun = Path.Combine(cacheDirectory, "squashfs-root-x86_64", "AppRun");

            Assert.Equal(expectedAppRun, firstPath);
            Assert.Equal(expectedAppRun, secondPath);
            Assert.Equal(
                ["https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"],
                build.DownloadUrls);
            Assert.Equal([downloadedPath], build.DownloadDestinations);
            Assert.Equal([$"'{downloadedPath}' --appimage-extract"], build.ShellCommands);
            var extractionDirectory = Assert.Single(build.ShellWorkingDirectories);
            Assert.Equal(cacheDirectory, Path.GetDirectoryName(extractionDirectory));
            Assert.StartsWith("extract-", Path.GetFileName(extractionDirectory), StringComparison.Ordinal);
            Assert.False(Directory.Exists(extractionDirectory));
            Assert.Equal(["linux-warning"], build.Calls);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies unsupported appimagetool host architectures fail before touching the cache or boundaries.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_UnsupportedHostArchitecture_ThrowsBeforeExternalBoundaries()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)rootDirectory,
            TestHostArchitecture = Architecture.X86
        };

        var exception = Assert.Throws<InvalidOperationException>(() => build.InvokePrepareAppImageTool());

        Assert.Contains("X86", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(rootDirectory));
        Assert.Empty(build.DownloadUrls);
        Assert.Empty(build.ShellCommands);
    }

    /// <summary>
    /// Verifies both AppImage shell commands single-quote every hostile path character.
    /// </summary>
    [Fact]
    public void ComposeAppImageShellCommands_HostilePaths_QuotesEveryPath()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\shell-test" : "/shell-test";
        var downloadedPath = (AbsolutePath)Path.Combine(root, "download $() ` \" single'quote.AppImage");
        var toolPath = (AbsolutePath)Path.Combine(root, "tool $() ` \" single'quote", "AppRun");
        var appDirPath = (AbsolutePath)Path.Combine(root, "AppDir $() ` \" single'quote");
        var outputPath = (AbsolutePath)Path.Combine(root, "output $() ` \" single'quote.AppImage");
        var build = new TestBuild();

        var extractionCommand = build.InvokeCreateAppImageToolExtractionCommand(downloadedPath);
        var buildCommand = build.InvokeCreateAppImageBuildCommand(
            "x86_64", toolPath, appDirPath, outputPath);

        var expectedDownloadedPath = $"'{downloadedPath.ToString().Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
        var expectedToolPath = $"'{toolPath.ToString().Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
        var expectedAppDirPath = $"'{appDirPath.ToString().Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
        var expectedOutputPath = $"'{outputPath.ToString().Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
        Assert.Equal($"{expectedDownloadedPath} --appimage-extract", extractionCommand);
        Assert.Equal(
            $"ARCH=x86_64 {expectedToolPath} {expectedAppDirPath} {expectedOutputPath}",
            buildCommand);
    }

    /// <summary>
    /// Verifies AppDir layout creation fails before staging when the configured SVG icon is absent.
    /// </summary>
    [Fact]
    public void CreateLinuxAppDir_MissingIcon_ThrowsBeforeCreatingStaging()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "missing.svg");
        var build = new TestBuild
        {
            TestLinuxIconFile = (AbsolutePath)iconPath
        };
        ConfigureLinuxOptions(build);

        try
        {
            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-x64", publishDirectory), (AbsolutePath)stagingPath));

            Assert.Equal(iconPath, exception.FileName);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies an existing non-SVG Linux icon is rejected before AppDir staging begins.
    /// </summary>
    [Fact]
    public void CreateLinuxAppDir_NonSvgIcon_ThrowsBeforeCreatingStaging()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.png");
        File.WriteAllText(iconPath, "not-svg");
        var build = new TestBuild
        {
            TestLinuxIconFile = (AbsolutePath)iconPath
        };
        ConfigureLinuxOptions(build);

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-x64", publishDirectory), (AbsolutePath)stagingPath));

            Assert.Contains(".svg", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies AppDir layout uses mutable Linux names consistently and writes complete LF-normalized metadata.
    /// </summary>
    [Fact]
    public void CreateLinuxAppDir_ConfiguredOptions_CreatesCompleteIsolatedLfLayout()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64", "configured-bin");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.SVG");
        File.WriteAllText(iconPath, "<svg>configured</svg>");
        File.WriteAllText(Path.Combine(publishDirectory, "runtime.json"),
            "{\"Runtime\":\"linux-x64\",\"IsBundle\":false,\"BundleType\":\"None\"}");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath
            };
            ConfigureLinuxOptions(build, "configured-bin", "configured-icon", "org.example.configured");
            var context = CreateContext(build, "linux-x64", publishDirectory);

            build.InvokeCreateLinuxAppDir(context, (AbsolutePath)stagingPath);

            var appRun = File.ReadAllText(Path.Combine(stagingPath, "AppRun"));
            var desktopFileName = "org.example.configured.desktop";
            var desktop = File.ReadAllText(Path.Combine(stagingPath, desktopFileName));
            var installedDesktop = File.ReadAllText(Path.Combine(
                stagingPath, "usr", "share", "applications", desktopFileName));
            var appStream = File.ReadAllText(Path.Combine(
                stagingPath, "usr", "share", "metainfo", "org.example.configured.appdata.xml"));

            Assert.Contains("exec \"configured-bin\"", appRun, StringComparison.Ordinal);
            Assert.Contains("Icon=configured-icon", desktop, StringComparison.Ordinal);
            Assert.Contains("Exec=\"configured-bin\"", desktop, StringComparison.Ordinal);
            Assert.Equal(desktop, installedDesktop);
            Assert.Contains("<id>org.example.configured</id>", appStream, StringComparison.Ordinal);
            Assert.Contains("<binary>configured-bin</binary>", appStream, StringComparison.Ordinal);
            Assert.All([appRun, desktop, appStream], content =>
                Assert.DoesNotContain("\r", content, StringComparison.Ordinal));
            Assert.Equal("<svg>configured</svg>", File.ReadAllText(
                Path.Combine(stagingPath, "configured-icon.svg")));
            Assert.Equal("<svg>configured</svg>", File.ReadAllText(Path.Combine(
                stagingPath, "usr", "share", "icons", "hicolor", "scalable", "apps",
                "configured-icon.svg")));
            Assert.Equal("linux-x64", File.ReadAllText(Path.Combine(
                stagingPath, "usr", "bin", "raw.txt")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(
                stagingPath, "usr", "bin", "configured-bin")));
            using var stagedManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                stagingPath, "usr", "bin", "runtime.json")));
            Assert.Equal("linux-x64", stagedManifest.RootElement.GetProperty("Runtime").GetString());
            Assert.True(stagedManifest.RootElement.GetProperty("IsBundle").GetBoolean());
            Assert.Equal(nameof(ApplicationBundleType.LinuxAppImage),
                stagedManifest.RootElement.GetProperty("BundleType").GetString());
            using var rawManifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(publishDirectory, "runtime.json")));
            Assert.False(rawManifest.RootElement.GetProperty("IsBundle").GetBoolean());
            Assert.Equal("configured-bin", build.LinuxAppBundleOptions.ExecutableName);
            Assert.Equal("configured-icon", build.LinuxAppBundleOptions.IconName);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies null executable and icon names resolve once to the mutable product name and drive every path.
    /// </summary>
    [Fact]
    public void CreateLinuxAppDir_NullExecutableAndIconNames_UsePersistedProductNameFallbacks()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-arm64", "Configured Product");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath
            };
            ConfigureLinuxOptions(build, null, null);

            build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-arm64", publishDirectory), (AbsolutePath)stagingPath);

            Assert.Equal("Configured Product", build.LinuxAppBundleOptions.ExecutableName);
            Assert.Equal("Configured Product", build.LinuxAppBundleOptions.IconName);
            Assert.True(File.Exists(Path.Combine(stagingPath, "Configured Product.svg")));
            Assert.Contains("Icon=Configured Product", File.ReadAllText(Path.Combine(
                stagingPath, "org.example.test.desktop")), StringComparison.Ordinal);
            Assert.Contains("exec \"Configured Product\"", File.ReadAllText(Path.Combine(
                stagingPath, "AppRun")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies AppDir layout rejects path-bearing Linux options before creating staging.
    /// </summary>
    /// <param name="optionName">The mutable option to configure.</param>
    /// <param name="invalidValue">The invalid path value.</param>
    [Theory]
    [InlineData("ExecutableName", "")]
    [InlineData("ExecutableName", "../app")]
    [InlineData("IconName", ".")]
    [InlineData("IconName", "nested\\icon")]
    [InlineData("ApplicationId", " ")]
    [InlineData("ApplicationId", "org/example/app")]
    public void CreateLinuxAppDir_InvalidPathOption_ThrowsBeforeStaging(
        string optionName,
        string invalidValue)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath
            };
            ConfigureLinuxOptions(build);
            SetLinuxPathOption(build.LinuxAppBundleOptions, optionName, invalidValue);

            var exception = Assert.Throws<InvalidOperationException>(() => build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-x64", publishDirectory), (AbsolutePath)stagingPath));

            Assert.Contains(optionName, exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies rooted values are rejected for every path-bearing Linux option.
    /// </summary>
    /// <param name="optionName">The mutable option to configure.</param>
    [Theory]
    [InlineData("ExecutableName")]
    [InlineData("IconName")]
    [InlineData("ApplicationId")]
    public void CreateLinuxAppDir_RootedPathOption_ThrowsBeforeStaging(string optionName)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath
            };
            ConfigureLinuxOptions(build);
            SetLinuxPathOption(build.LinuxAppBundleOptions, optionName,
                Path.Combine(Path.GetPathRoot(rootDirectory)!, "absolute-name"));

            var exception = Assert.Throws<InvalidOperationException>(() => build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-x64", publishDirectory), (AbsolutePath)stagingPath));

            Assert.Contains(optionName, exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies AppDir layout requires the resolved executable before creating staging.
    /// </summary>
    [Fact]
    public void CreateLinuxAppDir_MissingResolvedExecutable_ThrowsBeforeStaging()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath
            };
            ConfigureLinuxOptions(build, "MissingExecutable");

            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-x64", publishDirectory), (AbsolutePath)stagingPath));

            Assert.Equal(Path.Combine(publishDirectory, "MissingExecutable"), exception.FileName);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies AppImage creation composes the target command, replaces stale output, sets permissions, and cleans only staging.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_ValidPayload_BuildsVerifiedOutputAndRetainsToolCache()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64", "configured-bin");
        var outputPath = $"{publishDirectory}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        var cacheDirectory = Path.Combine(rootDirectory, "tool-cache");
        var stagingDirectory = Path.Combine(rootDirectory, "staging");
        File.WriteAllText(iconPath, "icon");
        File.WriteAllText(outputPath, "stale");
        File.WriteAllText(temporaryOutputPath, "partial");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)cacheDirectory,
                TestAppImageStagingDirectory = (AbsolutePath)stagingDirectory,
                TestHostArchitecture = Architecture.X64,
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                CreateAppImageOutput = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build, "configured-bin", "configured-icon");

            build.InvokeCreateLinuxAppImage(
                CreateContext(build, "linux-x64", publishDirectory), "x86_64");

            var extractedAppRun = Path.Combine(cacheDirectory, "squashfs-root-x86_64", "AppRun");
            var appDirPath = build.ShellWorkingDirectories[1];
            Assert.Equal("image", File.ReadAllText(outputPath));
            Assert.Equal(
                $"ARCH=x86_64 '{extractedAppRun}' '{appDirPath}' '{temporaryOutputPath}'",
                build.ShellCommands[1]);
            Assert.Equal(appDirPath, build.ShellWorkingDirectories[1]);
            Assert.True(build.AppDirExistedDuringBuild);
            Assert.False(build.OutputExistedWhenBuildStarted);
            Assert.True(build.FinalOutputExistedWhenBuildStarted);
            Assert.False(Directory.Exists(appDirPath));
            Assert.False(File.Exists(temporaryOutputPath));
            Assert.True(File.Exists(Path.Combine(cacheDirectory, "appimagetool-x86_64.AppImage")));
            Assert.True(File.Exists(extractedAppRun));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that an AppImage built from a temporary payload is moved beside the original publish output.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_TemporaryPayload_MovesArtifactToBundleOutputPath()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(rootDirectory, "bundle-publish", Guid.NewGuid().ToString("N"));
        var bundleOutputPath = Path.Combine(rootDirectory, "publish", "TestApp_linux-x64_v1.2.3");
        var outputPath = $"{bundleOutputPath}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(bundleOutputPath)!);
        File.WriteAllText(Path.Combine(payloadDirectory, "TestApp"), "payload");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)Path.Combine(rootDirectory, "tool-cache"),
                TestAppImageStagingDirectory = (AbsolutePath)Path.Combine(rootDirectory, "staging"),
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                CreateAppImageOutput = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build);
            var context = new PublishRidContext
            {
                Build = build,
                RuntimeIdentifier = "linux-x64",
                PublishPath = (AbsolutePath)payloadDirectory,
                BundleOutputPath = (AbsolutePath)bundleOutputPath
            };

            build.InvokeCreateLinuxAppImage(context, "x86_64");

            Assert.Equal("image", File.ReadAllText(outputPath));
            Assert.False(File.Exists($"{payloadDirectory}.AppImage"));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a missing appimagetool output is fatal, staging is cleaned, and the reusable tool cache remains.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_CommandProducesNoOutput_ThrowsAndCleansAppDirOnly()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-arm64");
        var outputPath = $"{publishDirectory}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        var cacheDirectory = Path.Combine(rootDirectory, "tool-cache");
        var stagingDirectory = Path.Combine(rootDirectory, "staging");
        File.WriteAllText(iconPath, "icon");
        File.WriteAllText(outputPath, "stable");
        File.WriteAllText(temporaryOutputPath, "partial");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)cacheDirectory,
                TestAppImageStagingDirectory = (AbsolutePath)stagingDirectory,
                TestHostArchitecture = Architecture.Arm64,
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build);

            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokeCreateLinuxAppImage(
                CreateContext(build, "linux-arm64", publishDirectory), "aarch64"));

            Assert.Equal(temporaryOutputPath, exception.FileName);
            Assert.True(build.AppDirExistedDuringBuild);
            Assert.False(Directory.Exists(build.ShellWorkingDirectories[1]));
            Assert.True(File.Exists(Path.Combine(cacheDirectory, "appimagetool-aarch64.AppImage")));
            Assert.True(File.Exists(Path.Combine(cacheDirectory, "squashfs-root-aarch64", "AppRun")));
            Assert.Equal("stable", File.ReadAllText(outputPath));
            Assert.False(File.Exists(temporaryOutputPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a failed AppImage command removes partial temporary output and leaves the previous final intact.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_CommandFailsAfterPartialOutput_CleansTemporaryAndPreservesFinal()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var outputPath = $"{publishDirectory}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");
        File.WriteAllText(outputPath, "stable");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)Path.Combine(rootDirectory, "tool-cache"),
                TestAppImageStagingDirectory = (AbsolutePath)Path.Combine(rootDirectory, "staging"),
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                CreateAppImageOutput = true,
                ThrowAfterAppImageBuild = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build);

            Assert.Throws<InvalidOperationException>(() => build.InvokeCreateLinuxAppImage(
                CreateContext(build, "linux-x64", publishDirectory), "x86_64"));

            Assert.True(build.FinalOutputExistedWhenBuildStarted);
            Assert.Equal("stable", File.ReadAllText(outputPath));
            Assert.False(File.Exists(temporaryOutputPath));
            Assert.False(Directory.Exists(build.ShellWorkingDirectories[1]));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a directory colliding with the temporary output path is removed before a successful build.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_TemporaryOutputStartsAsDirectory_RemovesCollisionAndBuildsFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var outputPath = $"{publishDirectory}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");
        Directory.CreateDirectory(temporaryOutputPath);
        File.WriteAllText(Path.Combine(temporaryOutputPath, "partial"), "partial");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)Path.Combine(rootDirectory, "tool-cache"),
                TestAppImageStagingDirectory = (AbsolutePath)Path.Combine(rootDirectory, "staging"),
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                CreateAppImageOutput = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build);

            build.InvokeCreateLinuxAppImage(
                CreateContext(build, "linux-x64", publishDirectory), "x86_64");

            Assert.Equal("image", File.ReadAllText(outputPath));
            Assert.False(File.Exists(temporaryOutputPath));
            Assert.False(Directory.Exists(temporaryOutputPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a directory produced at the temporary output path is rejected and removed with AppDir staging.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_CommandProducesOutputDirectory_ThrowsAndCleansBothStagingEntries()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var outputPath = $"{publishDirectory}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)Path.Combine(rootDirectory, "tool-cache"),
                TestAppImageStagingDirectory = (AbsolutePath)Path.Combine(rootDirectory, "staging"),
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                CreateAppImageOutputDirectory = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build);

            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokeCreateLinuxAppImage(
                CreateContext(build, "linux-x64", publishDirectory), "x86_64"));

            Assert.Equal(temporaryOutputPath, exception.FileName);
            Assert.False(File.Exists(temporaryOutputPath));
            Assert.False(Directory.Exists(temporaryOutputPath));
            Assert.False(Directory.Exists(build.AppDirPathDuringBuild));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies AppDir cleanup is attempted even when final temporary-output cleanup throws.
    /// </summary>
    [Fact]
    public void CreateLinuxAppImage_TemporaryCleanupFails_StillCleansAppDir()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var outputPath = $"{publishDirectory}.AppImage";
        var temporaryOutputPath = Path.Combine(rootDirectory, "temporary-output.AppImage");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath,
                TestAppImageToolCacheDirectory = (AbsolutePath)Path.Combine(rootDirectory, "tool-cache"),
                TestAppImageStagingDirectory = (AbsolutePath)Path.Combine(rootDirectory, "staging"),
                CreateDownloadedFile = true,
                CreateExtractedAppRun = true,
                CreateAppImageOutput = true,
                ThrowAfterAppImageBuild = true,
                ThrowOnFinalTemporaryOutputCleanup = true,
                UseConfiguredTemporaryAppImageOutputPath = true,
                TestTemporaryAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestAppImageOutputPath = (AbsolutePath)temporaryOutputPath,
                TestFinalAppImageOutputPath = (AbsolutePath)outputPath
            };
            ConfigureLinuxOptions(build);

            var exception = Assert.Throws<InvalidOperationException>(() => build.InvokeCreateLinuxAppImage(
                CreateContext(build, "linux-x64", publishDirectory), "x86_64"));

            Assert.Equal("Temporary output cleanup failed.", exception.Message);
            Assert.Equal(2, build.TemporaryOutputCleanupCalls);
            Assert.True(build.AppDirCleanupAttempted);
            Assert.False(Directory.Exists(build.AppDirPathDuringBuild));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies default temporary AppImage outputs are unique and share the final output directory.
    /// </summary>
    [Fact]
    public void CreateTemporaryAppImageOutputPath_RepeatedCalls_CreateUniqueSameDirectoryPaths()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var outputPath = (AbsolutePath)Path.Combine(rootDirectory, "TestApp.AppImage");
        var build = new TestBuild();

        var firstPath = build.InvokeCreateTemporaryAppImageOutputPath(outputPath);
        var secondPath = build.InvokeCreateTemporaryAppImageOutputPath(outputPath);

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(rootDirectory, Path.GetDirectoryName(firstPath));
        Assert.Equal(rootDirectory, Path.GetDirectoryName(secondPath));
        Assert.StartsWith(".TestApp.AppImage.", Path.GetFileName(firstPath), StringComparison.Ordinal);
        Assert.EndsWith(".tmp", Path.GetFileName(firstPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies ARM64 hosts select the continuous aarch64 appimagetool asset.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_Arm64Host_UsesAarch64Asset()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)rootDirectory,
            TestHostArchitecture = Architecture.Arm64,
            CreateDownloadedFile = true,
            CreateExtractedAppRun = true
        };

        try
        {
            var appRun = build.InvokePrepareAppImageTool();

            Assert.Equal(Path.Combine(rootDirectory, "squashfs-root-aarch64", "AppRun"), appRun);
            Assert.Equal(
                ["https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-aarch64.AppImage"],
                build.DownloadUrls);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a cached extraction without AppRun is deleted and rebuilt before it can be returned.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_CachedExtractionMissingAppRun_RebuildsValidCache()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var extractedDirectory = Path.Combine(rootDirectory, "squashfs-root-x86_64");
        Directory.CreateDirectory(extractedDirectory);
        File.WriteAllText(Path.Combine(extractedDirectory, "stale.txt"), "stale");
        File.WriteAllText(Path.Combine(rootDirectory, "appimagetool-x86_64.AppImage"), "tool");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)rootDirectory,
            TestHostArchitecture = Architecture.X64,
            CreateExtractedAppRun = true
        };

        try
        {
            var appRun = build.InvokePrepareAppImageTool();

            Assert.Equal(Path.Combine(extractedDirectory, "AppRun"), appRun);
            Assert.True(File.Exists(appRun));
            Assert.False(File.Exists(Path.Combine(extractedDirectory, "stale.txt")));
            Assert.Empty(build.DownloadUrls);
            Assert.Equal(
                [$"'{Path.Combine(rootDirectory, "appimagetool-x86_64.AppImage")}' --appimage-extract"],
                build.ShellCommands);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies an extraction missing AppRun is never published as the architecture cache and can be retried.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_ExtractionMissingAppRun_CleansInvalidOutputAndAllowsRetry()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var extractedDirectory = Path.Combine(rootDirectory, "squashfs-root-x86_64");
        var extractionOutput = Path.Combine(rootDirectory, "squashfs-root");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)rootDirectory,
            TestHostArchitecture = Architecture.X64,
            CreateDownloadedFile = true,
            CreateExtractionDirectory = true
        };

        try
        {
            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokePrepareAppImageTool());

            var failedExtraction = Assert.Single(build.ShellWorkingDirectories);
            Assert.Equal(Path.Combine(failedExtraction, "squashfs-root", "AppRun"), exception.FileName);
            Assert.False(Directory.Exists(extractedDirectory));
            Assert.False(Directory.Exists(failedExtraction));

            build.CreateExtractedAppRun = true;
            var appRun = build.InvokePrepareAppImageTool();

            Assert.Equal(Path.Combine(extractedDirectory, "AppRun"), appRun);
            Assert.Single(build.DownloadUrls);
            Assert.Equal(2, build.ShellCommands.Count);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies file/directory collisions at appimagetool cache paths are removed before rebuilding.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_CachePathTypeCollisions_RemovesEntriesAndRebuilds()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var downloadedPath = Path.Combine(rootDirectory, "appimagetool-x86_64.AppImage");
        var extractedPath = Path.Combine(rootDirectory, "squashfs-root-x86_64");
        Directory.CreateDirectory(downloadedPath);
        File.WriteAllText(Path.Combine(downloadedPath, "stale.txt"), "stale");
        File.WriteAllText(extractedPath, "invalid-cache-file");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)rootDirectory,
            TestHostArchitecture = Architecture.X64,
            CreateDownloadedFile = true,
            CreateExtractedAppRun = true
        };

        try
        {
            var appRun = build.InvokePrepareAppImageTool();

            Assert.Equal(Path.Combine(extractedPath, "AppRun"), appRun);
            Assert.True(File.Exists(downloadedPath));
            Assert.False(Directory.Exists(downloadedPath));
            Assert.True(File.Exists(appRun));
            Assert.Single(build.DownloadUrls);
            Assert.Single(build.ShellCommands);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a failed download cannot leave a partial file that would poison the persistent cache.
    /// </summary>
    [Fact]
    public void PrepareAppImageTool_DownloadFails_RemovesPartialCacheFile()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var build = new TestBuild
        {
            TestAppImageToolCacheDirectory = (AbsolutePath)rootDirectory,
            TestHostArchitecture = Architecture.X64,
            CreateDownloadedFile = true,
            ThrowAfterDownload = true
        };

        try
        {
            Assert.Throws<InvalidOperationException>(() => build.InvokePrepareAppImageTool());

            Assert.False(File.Exists(Path.Combine(rootDirectory, "appimagetool-x86_64.AppImage")));
            Assert.Empty(build.ShellCommands);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies the base Linux permission boundary sets all Unix execute bits.
    /// </summary>
    [Fact]
    public void SetUnixExecutable_LinuxFile_SetsExecuteBits()
    {
        if (OperatingSystem.IsWindows())
            return;

        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var executablePath = Path.Combine(rootDirectory, "executable");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(executablePath, "payload");

        try
        {
            UnixSystem.SetUnixExecutable((AbsolutePath)executablePath);

            const UnixFileMode executeBits = UnixFileMode.UserExecute |
                                             UnixFileMode.GroupExecute |
                                             UnixFileMode.OtherExecute;
            Assert.Equal(executeBits, File.GetUnixFileMode(executablePath) & executeBits);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies base AppDir creation sets execute bits on AppRun and the copied application executable on Linux.
    /// </summary>
    [Fact]
    public void CreateLinuxAppDir_LinuxBasePermissions_SetLauncherAndCopiedExecutableExecuteBits()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "linux-x64");
        var sourceExecutable = Path.Combine(publishDirectory, "TestApp");
        var stagingPath = Path.Combine(rootDirectory, "AppDir");
        var iconPath = Path.Combine(rootDirectory, "source.svg");
        File.WriteAllText(iconPath, "icon");
        File.SetUnixFileMode(sourceExecutable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        try
        {
            var build = new TestBuild
            {
                TestLinuxIconFile = (AbsolutePath)iconPath
            };
            ConfigureLinuxOptions(build);

            build.InvokeCreateLinuxAppDir(
                CreateContext(build, "linux-x64", publishDirectory), (AbsolutePath)stagingPath);

            const UnixFileMode executeBits = UnixFileMode.UserExecute |
                                             UnixFileMode.GroupExecute |
                                             UnixFileMode.OtherExecute;
            Assert.Equal(executeBits,
                File.GetUnixFileMode(Path.Combine(stagingPath, "AppRun")) & executeBits);
            Assert.Equal(executeBits,
                File.GetUnixFileMode(Path.Combine(stagingPath, "usr", "bin", "TestApp")) & executeBits);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies single-architecture macOS bundle selection preserves macOS RID order and ignores other families.
    /// </summary>
    [Fact]
    public void CreateBundles_MacOSSingleArchitectureSelected_DispatchesEveryMacContextInOrder()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordMacOSApp = true,
            TestUnixHost = true
        };
        build.SetPublishBundles(PublishBundleTypes.MacOSAppBundle);
        var contexts = new[]
        {
            CreateContext(build, "linux-x64"),
            CreateContext(build, "osx-arm64"),
            CreateContext(build, "win-x64"),
            CreateContext(build, "osx-x64")
        };

        build.InvokeCreateBundles(contexts);

        Assert.Equal(["mac:osx-arm64", "mac:osx-x64"], build.Calls);
    }

    /// <summary>
    /// Verifies multi-architecture macOS selection suppresses per-RID apps and dispatches one combined app.
    /// </summary>
    [Fact]
    public void CreateBundles_MacOSMultiArchitectureSelected_DispatchesOneCombinedApp()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordMacOSApp = true,
            RecordMultiArchMacOSApp = true,
            TestUnixHost = true
        };
        build.SetPublishBundles(PublishBundleTypes.MacOSAppBundle);
        build.SetPublishMultiArch(true);
        var contexts = new[]
        {
            CreateContext(build, "OSX-X64"),
            CreateContext(build, "linux-x64"),
            CreateContext(build, "OsX-ArM64")
        };

        build.InvokeCreateBundles(contexts);

        Assert.Equal(["mac-multi:OSX-X64:OsX-ArM64"], build.Calls);
    }

    /// <summary>
    /// Verifies multi-architecture macOS selection requires both supported architecture outputs.
    /// </summary>
    /// <param name="availableRuntimeIdentifier">The only available macOS runtime identifier.</param>
    /// <param name="missingRuntimeIdentifier">The required runtime identifier expected in the error.</param>
    [Theory]
    [InlineData("osx-x64", "osx-arm64")]
    [InlineData("osx-arm64", "osx-x64")]
    public void CreateBundles_MacOSMultiArchitectureMissingRequiredRid_Throws(
        string availableRuntimeIdentifier,
        string missingRuntimeIdentifier)
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordMultiArchMacOSApp = true,
            TestUnixHost = true
        };
        build.SetPublishBundles(PublishBundleTypes.MacOSAppBundle);
        build.SetPublishMultiArch(true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            build.InvokeCreateBundles([CreateContext(build, availableRuntimeIdentifier)]));

        Assert.Contains(missingRuntimeIdentifier, exception.Message, StringComparison.Ordinal);
        Assert.Empty(build.Calls);
    }

    /// <summary>
    /// Verifies a non-Unix host emits one warning and creates no macOS applications.
    /// </summary>
    [Fact]
    public void CreateBundles_MacOSSelectedOnNonUnixHost_WarnsOnceAndCreatesNoApps()
    {
        var build = new TestBuild
        {
            UseDefaultBundlePipeline = true,
            RecordMacOSApp = true,
            RecordMultiArchMacOSApp = true,
            RecordMacOSWarning = true
        };
        build.SetPublishBundles(PublishBundleTypes.MacOSAppBundle);

        build.InvokeCreateBundles(
        [
            CreateContext(build, "osx-x64"),
            CreateContext(build, "osx-arm64"),
            CreateContext(build, "linux-x64")
        ]);

        Assert.Equal(["mac-warning"], build.Calls);
    }

    /// <summary>
    /// Verifies common macOS layout creation fails before staging when the configured icon is absent.
    /// </summary>
    [Fact]
    public void CreateMacOSAppLayout_MissingIcon_ThrowsBeforeCreatingStaging()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(rootDirectory, "staging");
        var iconPath = Path.Combine(rootDirectory, "missing.icns");
        var build = new TestBuild
        {
            TestMacOSIconFile = (AbsolutePath)iconPath
        };
        ConfigureMacOSOptions(build);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            build.InvokeCreateMacOSAppLayout((AbsolutePath)stagingPath));

        Assert.Equal(iconPath, exception.FileName);
        Assert.False(Directory.Exists(stagingPath));
    }

    /// <summary>
    /// Verifies common macOS layout creation uses mutable options and writes LF-normalized metadata.
    /// </summary>
    [Fact]
    public void CreateMacOSAppLayout_ConfiguredOptions_CreatesCommonLfLayout()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(rootDirectory, "staging");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath
            };
            ConfigureMacOSOptions(build, "ConfiguredExecutable");

            var appPath = build.InvokeCreateMacOSAppLayout((AbsolutePath)stagingPath);
            var contentsPath = Path.Combine(appPath, "Contents");
            var infoPList = File.ReadAllText(Path.Combine(contentsPath, "Info.plist"));
            var entitlements = File.ReadAllText(Path.Combine(contentsPath, "TestApp.entitlements"));

            Assert.Equal(Path.Combine(stagingPath, "TestApp.app"), appPath);
            Assert.True(Directory.Exists(Path.Combine(contentsPath, "MacOS")));
            Assert.Equal("icon", File.ReadAllText(Path.Combine(contentsPath, "Resources", "Configured.icns")));
            Assert.Contains("<string>pt</string>", infoPList, StringComparison.Ordinal);
            Assert.Contains("ConfiguredExecutable", infoPList, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", infoPList, StringComparison.Ordinal);
            Assert.Contains("com.example.configured", entitlements, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", entitlements, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies single-architecture layout ignores invalid runtime directory settings that it does not use.
    /// </summary>
    [Fact]
    public void CreateMacOSAppLayout_SingleArchitectureInvalidRuntimeDirectories_CreatesLayout()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(rootDirectory, "staging");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath
            };
            ConfigureMacOSOptions(build, x64RuntimeIdentifier: "../unused", arm64RuntimeIdentifier: "../UNUSED");

            var appPath = build.InvokeCreateMacOSAppLayout((AbsolutePath)stagingPath);

            Assert.True(File.Exists(Path.Combine(appPath, "Contents", "Info.plist")));
            Assert.Equal("TestApp", build.MacAppBundleOptions.ExecutableName);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a single-architecture macOS app ZIP has an isolated app manifest and cleaned staging.
    /// </summary>
    [Fact]
    public void CreateMacOSApp_SingleArchitecture_CreatesIsolatedArchiveAndCleansStaging()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "osx-x64", "Configured Product");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        File.WriteAllText(iconPath, "icon");
        File.WriteAllText(Path.Combine(publishDirectory, "runtime.json"),
            "{\"Runtime\":\"osx-x64\",\"IsBundle\":false,\"BundleType\":\"None\"}");

        try
        {
            var build = new TestBuild
            {
                TestMacOSHost = true,
                TestMacOSIconFile = (AbsolutePath)iconPath,
                TestPublishDirectory = (AbsolutePath)rootDirectory
            };
            ConfigureMacOSOptions(build, null);
            var context = CreateContext(build, "osx-x64", publishDirectory);

            build.InvokeCreateMacOSApp(context);

            using var archive = ZipFile.OpenRead($"{publishDirectory}.zip");
            Assert.Equal("osx-x64", ReadManifestRuntime(archive,
                "TestApp.app/Contents/MacOS/runtime.json", nameof(ApplicationBundleType.MacOSAppBundle)));
            Assert.Equal("osx-x64", ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/raw.txt"));
            Assert.Equal("payload", ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/Configured Product"));
            Assert.Equal("icon", ReadArchiveEntry(archive,
                "TestApp.app/Contents/Resources/Configured.icns"));
            Assert.Contains("Configured Product", ReadArchiveEntry(archive,
                "TestApp.app/Contents/Info.plist"), StringComparison.Ordinal);
            Assert.Equal("Configured Product", build.MacAppBundleOptions.ExecutableName);
            Assert.Single(build.SignedAppPaths);
            Assert.False(Directory.Exists(build.SignedAppPaths[0]));

            using var rawManifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(publishDirectory, "runtime.json")));
            Assert.False(rawManifest.RootElement.GetProperty("IsBundle").GetBoolean());
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies that a macOS bundle built from a temporary payload is archived beside the original publish output.
    /// </summary>
    [Fact]
    public void CreateMacOSApp_TemporaryPayload_CreatesArchiveAtBundleOutputPath()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var payloadDirectory = Path.Combine(rootDirectory, "bundle-publish", Guid.NewGuid().ToString("N"));
        var bundleOutputPath = Path.Combine(rootDirectory, "publish", "TestApp_osx-x64_v1.2.3");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        Directory.CreateDirectory(payloadDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(bundleOutputPath)!);
        File.WriteAllText(Path.Combine(payloadDirectory, "Configured Product"), "payload");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath
            };
            ConfigureMacOSOptions(build, null);
            var context = new PublishRidContext
            {
                Build = build,
                RuntimeIdentifier = "osx-x64",
                PublishPath = (AbsolutePath)payloadDirectory,
                BundleOutputPath = (AbsolutePath)bundleOutputPath
            };

            build.InvokeCreateMacOSApp(context);

            Assert.True(File.Exists($"{bundleOutputPath}.zip"));
            Assert.False(File.Exists($"{payloadDirectory}.zip"));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies a multi-architecture macOS app contains per-RID payloads, app manifests, and an LF launcher.
    /// </summary>
    [Fact]
    public void CreateMultiArchMacOSApp_CustomNamesAndMixedCaseContexts_UsesConfiguredLayout()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var x64PublishDirectory = CreateRawOutput(rootDirectory, "OSX-X64", "ConfiguredExecutable");
        var arm64PublishDirectory = CreateRawOutput(rootDirectory, "OsX-ArM64", "ConfiguredExecutable");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSHost = true,
                TestMacOSIconFile = (AbsolutePath)iconPath,
                TestPublishDirectory = (AbsolutePath)rootDirectory
            };
            ConfigureMacOSOptions(build, "ConfiguredExecutable", "intel-runtime", "apple-runtime");
            build.SetPublishMultiArch(true);
            var x64Context = CreateContext(build, "OSX-X64", x64PublishDirectory);
            var arm64Context = CreateContext(build, "OsX-ArM64", arm64PublishDirectory);

            build.InvokeCreateMultiArchMacOSApp(x64Context, arm64Context);

            var archivePath = Path.Combine(rootDirectory, "TestApp_osx-multiarch_v1.2.3.zip");
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Equal("osx-multiarch", ReadManifestRuntime(archive,
                "TestApp.app/Contents/MacOS/intel-runtime/runtime.json",
                nameof(ApplicationBundleType.MacOSAppBundle)));
            Assert.Equal("osx-multiarch", ReadManifestRuntime(archive,
                "TestApp.app/Contents/MacOS/apple-runtime/runtime.json",
                nameof(ApplicationBundleType.MacOSAppBundle)));
            Assert.Equal("OSX-X64", ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/intel-runtime/raw.txt"));
            Assert.Equal("OsX-ArM64", ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/apple-runtime/raw.txt"));
            Assert.Equal("payload", ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/intel-runtime/ConfiguredExecutable"));
            Assert.Equal("payload", ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/apple-runtime/ConfiguredExecutable"));

            var launcher = ReadArchiveEntry(archive,
                "TestApp.app/Contents/MacOS/ConfiguredExecutable");
            Assert.Contains("intel-runtime", launcher, StringComparison.Ordinal);
            Assert.Contains("apple-runtime", launcher, StringComparison.Ordinal);
            Assert.Contains("exec \"ConfiguredExecutable\"", launcher, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", launcher, StringComparison.Ordinal);
            Assert.Contains("ConfiguredExecutable", ReadArchiveEntry(archive,
                "TestApp.app/Contents/Info.plist"), StringComparison.Ordinal);
            Assert.Single(build.SignedAppPaths);
            Assert.False(Directory.Exists(build.SignedAppPaths[0]));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies single-architecture app creation rejects a published payload without the resolved executable.
    /// </summary>
    [Fact]
    public void CreateMacOSApp_MissingResolvedExecutable_ThrowsWithoutArchive()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var publishDirectory = CreateRawOutput(rootDirectory, "osx-x64");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath,
                TestPublishDirectory = (AbsolutePath)rootDirectory
            };
            ConfigureMacOSOptions(build, "MissingExecutable");
            var context = CreateContext(build, "osx-x64", publishDirectory);

            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokeCreateMacOSApp(context));

            Assert.Equal(Path.Combine(publishDirectory, "MissingExecutable"), exception.FileName);
            Assert.False(File.Exists($"{publishDirectory}.zip"));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies multiarch app creation validates the resolved executable in both architecture payloads.
    /// </summary>
    [Fact]
    public void CreateMultiArchMacOSApp_Arm64PayloadMissingResolvedExecutable_ThrowsWithoutArchive()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var x64PublishDirectory = CreateRawOutput(rootDirectory, "osx-x64", "ConfiguredExecutable");
        var arm64PublishDirectory = CreateRawOutput(rootDirectory, "osx-arm64");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath,
                TestPublishDirectory = (AbsolutePath)rootDirectory
            };
            ConfigureMacOSOptions(build, "ConfiguredExecutable");
            build.SetPublishMultiArch(true);

            var exception = Assert.Throws<FileNotFoundException>(() => build.InvokeCreateMultiArchMacOSApp(
                CreateContext(build, "osx-x64", x64PublishDirectory),
                CreateContext(build, "osx-arm64", arm64PublishDirectory)));

            Assert.Equal(Path.Combine(arm64PublishDirectory, "ConfiguredExecutable"), exception.FileName);
            Assert.False(File.Exists(Path.Combine(rootDirectory, "TestApp_osx-multiarch_v1.2.3.zip")));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies macOS path-bearing options reject blank, traversal, and separator-containing values.
    /// </summary>
    /// <param name="optionName">The option to configure.</param>
    /// <param name="invalidValue">The invalid path value.</param>
    [Theory]
    [InlineData("ExecutableName", "")]
    [InlineData("ExecutableName", "..")]
    [InlineData("ExecutableName", "nested/app")]
    [InlineData("IconFileName", "../outside.icns")]
    [InlineData("IconFileName", "nested\\icon.icns")]
    [InlineData("X64RuntimeIdentifier", "nested\\runtime")]
    [InlineData("Arm64RuntimeIdentifier", " ")]
    public void CreateMacOSAppLayout_InvalidPathOption_ThrowsBeforeStaging(
        string optionName,
        string invalidValue)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(rootDirectory, "staging");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath
            };
            ConfigureMacOSOptions(build);
            SetMacOSPathOption(build.MacAppBundleOptions, optionName, invalidValue);
            if (optionName is not "ExecutableName")
                build.SetPublishMultiArch(true);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                build.InvokeCreateMacOSAppLayout((AbsolutePath)stagingPath));

            Assert.Contains(optionName, exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies rooted values are rejected for every macOS path-bearing option.
    /// </summary>
    /// <param name="optionName">The option to configure.</param>
    [Theory]
    [InlineData("ExecutableName")]
    [InlineData("IconFileName")]
    [InlineData("X64RuntimeIdentifier")]
    [InlineData("Arm64RuntimeIdentifier")]
    public void CreateMacOSAppLayout_RootedPathOption_ThrowsBeforeStaging(string optionName)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(rootDirectory, "staging");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath
            };
            ConfigureMacOSOptions(build);
            SetMacOSPathOption(build.MacAppBundleOptions, optionName,
                Path.Combine(Path.GetPathRoot(rootDirectory)!, "absolute-name"));
            if (optionName is not "ExecutableName")
                build.SetPublishMultiArch(true);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                build.InvokeCreateMacOSAppLayout((AbsolutePath)stagingPath));

            Assert.Contains(optionName, exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies multiarch path components cannot collide on case-insensitive file systems.
    /// </summary>
    /// <param name="executableName">The configured executable name.</param>
    /// <param name="x64RuntimeIdentifier">The configured x64 directory name.</param>
    /// <param name="arm64RuntimeIdentifier">The configured ARM64 directory name.</param>
    [Theory]
    [InlineData("TestApp", "shared", "SHARED")]
    [InlineData("shared", "SHARED", "arm64")]
    [InlineData("shared", "x64", "SHARED")]
    public void CreateMacOSAppLayout_MultiarchPathComponentsCollide_Throws(
        string executableName,
        string x64RuntimeIdentifier,
        string arm64RuntimeIdentifier)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var stagingPath = Path.Combine(rootDirectory, "staging");
        var iconPath = Path.Combine(rootDirectory, "source.icns");
        Directory.CreateDirectory(rootDirectory);
        File.WriteAllText(iconPath, "icon");

        try
        {
            var build = new TestBuild
            {
                TestMacOSIconFile = (AbsolutePath)iconPath
            };
            build.SetPublishMultiArch(true);
            ConfigureMacOSOptions(build, executableName, x64RuntimeIdentifier, arm64RuntimeIdentifier);

            Assert.Throws<InvalidOperationException>(() =>
                build.InvokeCreateMacOSAppLayout((AbsolutePath)stagingPath));
            Assert.False(Directory.Exists(stagingPath));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    /// <summary>
    /// Verifies the base Unix executable boundary sets execute bits that are retained in ZIP metadata.
    /// </summary>
    [Fact]
    public void SetUnixExecutable_MacOSFile_SetsExecuteBitsAndZipMetadata()
    {
        if (OperatingSystem.IsWindows())
            return;

        var rootDirectory = Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(rootDirectory, "source");
        var executablePath = Path.Combine(sourceDirectory, "launcher");
        var archivePath = Path.Combine(rootDirectory, "bundle.zip");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(executablePath, "#!/usr/bin/env bash\n");

        try
        {
            UnixSystem.SetUnixExecutable((AbsolutePath)executablePath);

            const UnixFileMode executeBits = UnixFileMode.UserExecute |
                                             UnixFileMode.GroupExecute |
                                             UnixFileMode.OtherExecute;
            Assert.Equal(executeBits, File.GetUnixFileMode(executablePath) & executeBits);

            PublishUtilities.CreateZip((AbsolutePath)sourceDirectory, (AbsolutePath)archivePath);
            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.GetEntry("launcher");
            Assert.NotNull(entry);
            var archivedMode = (UnixFileMode)(entry.ExternalAttributes >> 16);
            Assert.Equal(executeBits, archivedMode & executeBits);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    private static void ConfigureMacOSOptions(
        TestBuild build,
        string? executableName = "TestApp",
        string x64RuntimeIdentifier = "osx-x64",
        string arm64RuntimeIdentifier = "osx-arm64")
    {
        build.MacAppBundleOptions = new MacAppBundleOptions
        {
            ProductName = "Configured Product",
            BundleIdentifier = "com.example.test",
            Version = "1.2.3",
            DevelopmentRegion = "pt",
            IconFileName = "Configured.icns",
            ExecutableName = executableName,
            X64RuntimeIdentifier = x64RuntimeIdentifier,
            Arm64RuntimeIdentifier = arm64RuntimeIdentifier,
            Entitlements = new Dictionary<string, bool>
            {
                ["com.example.configured"] = true
            }
        };
    }

    private static void ConfigureLinuxOptions(
        TestBuild build,
        string? executableName = "TestApp",
        string? iconName = "TestApp",
        string applicationId = "org.example.test")
    {
        build.LinuxAppBundleOptions = new LinuxAppBundleOptions
        {
            ApplicationId = applicationId,
            ProductName = "Configured Product",
            Summary = "Configured summary",
            Description = "Configured description",
            License = "MIT",
            RepositoryUrl = "https://example.test/repository",
            Authors = "Example Authors",
            ExecutableName = executableName,
            IconName = iconName,
            Categories = ["Utility"],
            Keywords = ["configured"]
        };
    }

    private static void SetLinuxPathOption(LinuxAppBundleOptions options, string optionName, string value)
    {
        switch (optionName)
        {
            case "ExecutableName":
                options.ExecutableName = value;
                break;
            case "IconName":
                options.IconName = value;
                break;
            case "ApplicationId":
                options.ApplicationId = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(optionName), optionName, null);
        }
    }

    private static void SetMacOSPathOption(MacAppBundleOptions options, string optionName, string value)
    {
        switch (optionName)
        {
            case "ExecutableName":
                options.ExecutableName = value;
                break;
            case "IconFileName":
                options.IconFileName = value;
                break;
            case "X64RuntimeIdentifier":
                options.X64RuntimeIdentifier = value;
                break;
            case "Arm64RuntimeIdentifier":
                options.Arm64RuntimeIdentifier = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(optionName), optionName, null);
        }
    }

    private static string ReadArchiveEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string ReadManifestRuntime(ZipArchive archive, string entryName, string expectedBundleType)
    {
        using var manifest = JsonDocument.Parse(ReadArchiveEntry(archive, entryName));
        Assert.True(manifest.RootElement.GetProperty("IsBundle").GetBoolean());
        Assert.Equal(expectedBundleType, manifest.RootElement.GetProperty("BundleType").GetString());
        return manifest.RootElement.GetProperty("Runtime").GetString()!;
    }

    private static PublishRidContext CreateContext(TestBuild build, string runtimeIdentifier = "linux-x64",
        string? publishPath = null)
    {
        return new PublishRidContext
        {
            Build = build,
            RuntimeIdentifier = runtimeIdentifier,
            PublishPath = (AbsolutePath)(publishPath ?? Path.GetFullPath($"publish/{runtimeIdentifier}"))
        };
    }

    private static string CreateRawOutput(string rootDirectory, string runtimeIdentifier,
        string executableName = "TestApp")
    {
        var publishDirectory = Path.Combine(rootDirectory, $"TestApp_{runtimeIdentifier}_v1.2.3");
        Directory.CreateDirectory(publishDirectory);
        File.WriteAllText(Path.Combine(publishDirectory, "raw.txt"), runtimeIdentifier);
        File.WriteAllText(Path.Combine(publishDirectory, executableName), "payload");
        return publishDirectory;
    }

    private static Project CreateProject(string projectPath)
    {
        var modelAssembly = Assembly.Load("Fallout.Persistence.Solution");
        var solutionModelType = modelAssembly.GetType("Fallout.Persistence.Solution.Model.SolutionModel", true)!;
        var projectModelType = modelAssembly.GetType("Fallout.Persistence.Solution.Model.SolutionProjectModel", true)!;
        var solutionModel = Activator.CreateInstance(solutionModelType)!;
        var projectModel = Activator.CreateInstance(projectModelType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [solutionModel, projectPath, Guid.NewGuid(), Path.GetFileNameWithoutExtension(projectPath), null],
            null)!;
        var solution = Activator.CreateInstance(typeof(Solution),
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [solutionModel, (AbsolutePath)Path.Combine(Path.GetTempPath(), "Updatum.Test.sln")],
            null)!;

        return (Project)Activator.CreateInstance(typeof(Project),
            BindingFlags.Instance | BindingFlags.Public,
            null,
            [projectModel, solution],
            null)!;
    }

    private static TestBuild CreateTargetBuild(
        string rootDirectory,
        bool discardNonBundles = false,
        bool publishNoBundles = false,
        params string[] runtimeIdentifiers)
    {
        var build = new TestBuild
        {
            UseTargetPipeline = true,
            TestPublishDirectory = (AbsolutePath)rootDirectory,
            TestChangelogFile = (AbsolutePath)Path.Combine(rootDirectory, "CHANGELOG.md"),
            TestReleaseNotesFile = (AbsolutePath)Path.Combine(rootDirectory, "RELEASE_NOTES.md")
        };
        build.ConfigurePublishTarget(runtimeIdentifiers, discardNonBundles, publishNoBundles);

        return build;
    }

    private sealed class TestBuild : UpdatumBuild
    {
        internal readonly List<string> BundleOutputPaths = [];
        internal readonly List<string> Calls = [];

        internal readonly List<string> DownloadDestinations = [];

        internal readonly List<string> DownloadUrls = [];

        internal readonly List<string> InstallerSourcePaths = [];

        internal readonly List<string> PortableZipPayloadPaths = [];

        internal readonly List<string> ShellCommands = [];

        internal readonly List<string> ShellWorkingDirectories = [];

        internal readonly List<string> SignedAppPaths = [];

        internal List<string> RestoredRuntimeIdentifiers { get; } = [];

        internal string TestBuildRuntimeCacheFileName { get; set; } = "runtime.json";

        internal string TestSoftwareName { get; set; } = "TestApp";

        internal string TestSoftwareVersion { get; set; } = "1.2.3";

        internal bool UseDefaultPreparation { get; set; }

        internal bool UseDefaultSettings { get; set; }

        internal bool CaptureSingleFileInputs { get; set; }

        internal string CapturedRuntimeManifest { get; private set; } = string.Empty;

        internal string CapturedSingleFileTargets { get; private set; } = string.Empty;

        internal string CapturedRuntimeManifestPath { get; private set; } = string.Empty;

        internal string CapturedSingleFileTargetsPath { get; private set; } = string.Empty;

        internal bool UseDefaultBuildRuntimeCacheFileName { get; set; }

        internal string? RuntimeCacheFileNameFromMainProject { get; set; }

        internal bool UseTargetPipeline { get; set; }

        internal bool RecordRestoreCalls { get; set; }

        internal bool ThrowWhenCreatingBundles { get; set; }

        internal bool ThrowNuGetFrameworksFromProjectEvaluation { get; set; }

        internal bool ThrowNuGetFrameworksFromMSBuildProjectEvaluation { get; set; }

        internal IReadOnlyDictionary<string, string?> TestMSBuildProjectProperties { get; set; } =
            new Dictionary<string, string?>();

        internal IReadOnlyDictionary<string, string?> TestExternallyEvaluatedProjectProperties { get; set; } =
            new Dictionary<string, string?>();

        internal IReadOnlyDictionary<string, string?> TestMainProjectProperties { get; set; } =
            new Dictionary<string, string?>();

        internal int MSBuildProjectEvaluationCalls { get; private set; }

        internal int ExternalProjectEvaluationCalls { get; private set; }

        internal bool UseDefaultBundlePipeline { get; set; }

        internal bool RecordPortableZip { get; set; }

        internal bool RecordMacOSApp { get; set; }

        internal bool RecordMultiArchMacOSApp { get; set; }

        internal bool RecordLinuxAppImage { get; set; }

        internal bool RecordMacOSWarning { get; set; }

        internal bool RecordLinuxWarning { get; set; }

        internal bool TestUnixHost { get; set; }

        internal bool TestMacOSHost { get; set; }

        internal bool TestLinuxHost { get; set; }

        internal bool TestFuseAvailable { get; set; } = true;

        internal bool CreateDownloadedFile { get; set; }

        internal bool CreateExtractedAppRun { get; set; }

        internal bool CreateExtractionDirectory { get; set; }

        internal bool CreateAppImageOutput { get; set; }

        internal bool CreateAppImageOutputDirectory { get; set; }

        internal bool ThrowAfterAppImageBuild { get; set; }

        internal bool UseConfiguredTemporaryAppImageOutputPath { get; set; }

        internal bool ThrowOnFinalTemporaryOutputCleanup { get; set; }

        internal int TemporaryOutputCleanupCalls { get; private set; }

        internal bool AppDirCleanupAttempted { get; private set; }

        internal bool ThrowAfterDownload { get; set; }

        internal bool AppDirExistedDuringBuild { get; private set; }

        internal bool OutputExistedWhenBuildStarted { get; private set; }

        internal bool FinalOutputExistedWhenBuildStarted { get; private set; }

        internal string AppDirPathDuringBuild { get; private set; } = string.Empty;

        internal Architecture TestHostArchitecture { get; set; } = Architecture.X64;

        internal IReadOnlyCollection<Project> TestInstallerProjects { get; set; } = [];

        internal Project? TestMainProject { get; set; }

        internal AbsolutePath TestPublishDirectory { get; set; } = (AbsolutePath)Path.GetTempPath();

        internal AbsolutePath TestMacOSIconFile { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}.icns");

        internal AbsolutePath TestLinuxIconFile { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}.svg");

        internal AbsolutePath TestAppImageToolCacheDirectory { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}-appimagetool");

        internal AbsolutePath TestAppImageStagingDirectory { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}-appdir");

        internal AbsolutePath TestAppImageOutputPath { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}.AppImage");

        internal AbsolutePath TestFinalAppImageOutputPath { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}-final.AppImage");

        internal AbsolutePath TestTemporaryAppImageOutputPath { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}.tmp");

        internal AbsolutePath TestChangelogFile { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}-changelog.md");

        internal AbsolutePath TestReleaseNotesFile { get; set; } =
            (AbsolutePath)Path.Combine(Path.GetTempPath(), $"updatum-{Guid.NewGuid():N}-release-notes.md");

        public override string SoftwareName => TestSoftwareName;

        public override string SoftwareVersion => TestSoftwareVersion;

        public override string BuildRuntimeCacheFileName =>
            UseDefaultBuildRuntimeCacheFileName ? base.BuildRuntimeCacheFileName : TestBuildRuntimeCacheFileName;

        public override Project MainProject => TestMainProject ?? base.MainProject;

        public override IReadOnlyCollection<Project> InstallerProjects => TestInstallerProjects;

        public override AbsolutePath MacOSIconFile => TestMacOSIconFile;

        public override AbsolutePath LinuxIconFile => TestLinuxIconFile;

        public override AbsolutePath PublishDirectory => TestPublishDirectory;

        public override AbsolutePath ChangelogFile => TestChangelogFile;

        public override AbsolutePath ReleaseNotesFile => TestReleaseNotesFile;

        protected override bool IsUnixHost => TestUnixHost;

        protected override bool IsMacOSHost => TestMacOSHost;

        protected override bool IsLinuxHost => TestLinuxHost;

        protected override bool IsFuseAvailable => TestFuseAvailable;

        protected override Architecture HostArchitecture => TestHostArchitecture;

        protected override AbsolutePath AppImageToolCacheDirectory => TestAppImageToolCacheDirectory;

        protected override AbsolutePath AppImageStagingDirectory => TestAppImageStagingDirectory;

        protected override string? GetBuildRuntimeCacheFileName()
        {
            return RuntimeCacheFileNameFromMainProject;
        }

        protected override string? GetMainProjectProperty(string propertyName)
        {
            return TestMainProjectProperties.TryGetValue(propertyName, out var value)
                ? value
                : base.GetMainProjectProperty(propertyName);
        }

        internal void InvokePublishRuntime(PublishRidContext context)
        {
            PublishRuntime(context);
        }

        internal void InvokePreparePublishedOutput(PublishRidContext context)
        {
            PreparePublishedOutput(context);
        }

        internal DotNetPublishSettings InvokeCreatePublishSettings(PublishRidContext context)
        {
            return CreatePublishSettings(context);
        }

        internal DotNetPublishSettings InvokeCreateInstallerPublishSettings(PublishRidContext context,
            AbsolutePath outputPath)
        {
            return CreateWindowsInstallerPublishSettings(context, outputPath);
        }

        internal bool InvokeIsInstallerProject(Project project)
        {
            return IsWindowsInstallerProject(project);
        }

        internal void InvokeExecutePublish()
        {
            ExecutePublish();
        }

        internal bool InvokeIsRunnableProject(Project project)
        {
            return IsRunnableProject(project);
        }

        internal IReadOnlyDictionary<string, string> InvokeGetPrintVariables()
        {
            return GetPrintVariables();
        }

        internal void InvokeCreateBundles(IReadOnlyCollection<PublishRidContext> contexts)
        {
            CreateBundles(contexts);
        }

        internal void InvokeCreatePortableZip(PublishRidContext context)
        {
            CreatePortableZip(context);
        }

        internal bool InvokeShouldCreatePortableZip(string runtimeIdentifier)
        {
            return ShouldCreatePortableZip(runtimeIdentifier);
        }

        internal AbsolutePath InvokeCreateMacOSAppLayout(AbsolutePath stagingPath)
        {
            return CreateMacOSAppLayout(stagingPath);
        }

        internal void InvokeCreateMacOSApp(PublishRidContext context)
        {
            CreateMacOSApp(context);
        }

        internal void InvokeCreateMultiArchMacOSApp(PublishRidContext x64Context,
            PublishRidContext arm64Context)
        {
            CreateMultiArchMacOSApp(x64Context, arm64Context);
        }

        internal AbsolutePath InvokePrepareAppImageTool()
        {
            return PrepareAppImageTool();
        }

        internal string InvokeCreateAppImageToolExtractionCommand(AbsolutePath downloadedPath)
        {
            return CreateAppImageToolExtractionCommand(downloadedPath);
        }

        internal string InvokeCreateAppImageBuildCommand(string architecture, AbsolutePath appImageTool,
            AbsolutePath appDirPath, AbsolutePath outputPath)
        {
            return CreateAppImageBuildCommand(architecture, appImageTool, appDirPath, outputPath);
        }

        internal void InvokeCreateLinuxAppDir(PublishRidContext context, AbsolutePath appDirPath)
        {
            CreateLinuxAppDir(context, appDirPath);
        }

        internal void InvokeCreateLinuxAppImage(PublishRidContext context, string architecture)
        {
            CreateLinuxAppImage(context, architecture);
        }

        internal AbsolutePath InvokeCreateTemporaryAppImageOutputPath(AbsolutePath outputPath)
        {
            return CreateTemporaryAppImageOutputPath(outputPath);
        }

        internal void ConfigurePublishTarget(
            string[] runtimeIdentifiers,
            bool discardNonBundles,
            bool publishNoBundles)
        {
            RIds = runtimeIdentifiers;
            PublishDiscardNonBundles = discardNonBundles;
            PublishNoBundles = publishNoBundles;
        }

        internal void SetPublishBundles(PublishBundleTypes publishBundles)
        {
            PublishBundles = publishBundles;
        }

        internal void SetPublishCleanupExtensions(params string[] extensions)
        {
            PublishCleanupExtensions = extensions;
        }

        internal void SetPublishMultiArch(bool publishMultiArch)
        {
            PublishMultiArch = publishMultiArch;
        }

        protected override AbsolutePath CreateTemporaryAppImageOutputPath(AbsolutePath outputPath)
        {
            return UseConfiguredTemporaryAppImageOutputPath
                ? TestTemporaryAppImageOutputPath
                : base.CreateTemporaryAppImageOutputPath(outputPath);
        }

        protected override void DeleteFileSystemEntry(AbsolutePath path)
        {
            if (UseConfiguredTemporaryAppImageOutputPath &&
                path.ToString().Equals(TestTemporaryAppImageOutputPath, StringComparison.Ordinal))
            {
                TemporaryOutputCleanupCalls++;
                if (ThrowOnFinalTemporaryOutputCleanup && TemporaryOutputCleanupCalls == 2)
                    throw new InvalidOperationException("Temporary output cleanup failed.");
            }

            if (!string.IsNullOrEmpty(AppDirPathDuringBuild) &&
                path.ToString().Equals(AppDirPathDuringBuild, StringComparison.Ordinal))
            {
                AppDirCleanupAttempted = true;
            }

            base.DeleteFileSystemEntry(path);
        }

        protected override DotNetPublishSettings CreatePublishSettings(PublishRidContext context)
        {
            if (UseDefaultSettings)
                return base.CreatePublishSettings(context);

            Calls.Add("settings");
            return new DotNetPublishSettings();
        }

        protected override void RestorePublishRuntimeIdentifier(string runtimeIdentifier)
        {
            RestoredRuntimeIdentifiers.Add(runtimeIdentifier);
            if (RecordRestoreCalls)
                Calls.Add($"restore:{runtimeIdentifier}");
        }

        protected override string? GetProjectPropertyInProcess(Project project, string propertyName)
        {
            if (ThrowNuGetFrameworksFromProjectEvaluation)
                throw new InvalidOperationException(
                    "Could not load NuGet.Frameworks because the manifest definition does not match.");

            return base.GetProjectPropertyInProcess(project, propertyName);
        }

        protected override string? GetProjectPropertyFromMSBuild(Project project, string propertyName)
        {
            MSBuildProjectEvaluationCalls++;
            if (ThrowNuGetFrameworksFromMSBuildProjectEvaluation)
            {
                throw new InvalidOperationException(
                    "Could not load NuGet.Frameworks because the manifest definition does not match.");
            }

            return TestMSBuildProjectProperties.GetValueOrDefault(propertyName);
        }

        protected override IReadOnlyDictionary<string, string?> EvaluateProjectProperties(
            Project project,
            IReadOnlyCollection<string> propertyNames)
        {
            ExternalProjectEvaluationCalls++;
            return TestExternallyEvaluatedProjectProperties;
        }

        protected override void PublishRuntime(PublishRidContext context)
        {
            if (!UseTargetPipeline)
            {
                base.PublishRuntime(context);
                return;
            }

            Assert.False(File.Exists(Path.Combine(context.PublishPath, "stale.txt")));
            Directory.CreateDirectory(context.PublishPath);
            File.WriteAllText(Path.Combine(context.PublishPath, "raw.txt"), context.RuntimeIdentifier);
            if (PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable))
            {
                var runtime = PublishRid.ParseRuntimeIdentifier(context.RuntimeIdentifier);
                var executableName = runtime.Family is PublishRidFamily.Windows
                    ? $"{SoftwareName}.exe"
                    : SoftwareName;
                File.WriteAllText(Path.Combine(context.PublishPath, executableName),
                    context.RuntimeIdentifier);
            }

            Calls.Add($"publish:{context.RuntimeIdentifier}:{Path.GetFileName(context.PublishPath)}");
        }

        protected override void CreateBundles(IReadOnlyCollection<PublishRidContext> contexts)
        {
            if (UseDefaultBundlePipeline)
            {
                base.CreateBundles(contexts);
                return;
            }

            Calls.Add("bundles");
            if (ThrowWhenCreatingBundles)
                throw new InvalidOperationException("Bundle creation failed.");
        }

        protected override void CreatePortableZip(PublishRidContext context)
        {
            if (RecordPortableZip)
            {
                Calls.Add($"zip:{context.RuntimeIdentifier}");
                PortableZipPayloadPaths.Add(context.PublishPath);
                BundleOutputPaths.Add(context.BundleOutputPath);
                return;
            }

            base.CreatePortableZip(context);
        }

        protected override void CreateMacOSApp(PublishRidContext context)
        {
            if (RecordMacOSApp)
            {
                Calls.Add($"mac:{context.RuntimeIdentifier}");
                BundleOutputPaths.Add(context.BundleOutputPath);
                return;
            }

            base.CreateMacOSApp(context);
        }

        protected override void CreateMultiArchMacOSApp(PublishRidContext x64Context,
            PublishRidContext arm64Context)
        {
            if (RecordMultiArchMacOSApp)
            {
                Calls.Add($"mac-multi:{x64Context.RuntimeIdentifier}:{arm64Context.RuntimeIdentifier}");
                return;
            }

            base.CreateMultiArchMacOSApp(x64Context, arm64Context);
        }

        protected override void WarnMacOSAppsUnsupportedHost()
        {
            if (RecordMacOSWarning)
            {
                Calls.Add("mac-warning");
                return;
            }

            base.WarnMacOSAppsUnsupportedHost();
        }

        protected override void CreateLinuxAppImage(PublishRidContext context, string architecture)
        {
            if (RecordLinuxAppImage)
            {
                Calls.Add($"linux:{context.RuntimeIdentifier}:{architecture}");
                return;
            }

            base.CreateLinuxAppImage(context, architecture);
        }

        protected override void WarnLinuxAppImagesUnsupportedHost()
        {
            if (RecordLinuxWarning)
            {
                Calls.Add("linux-warning");
                return;
            }

            base.WarnLinuxAppImagesUnsupportedHost();
        }

        protected override void WarnFuseUnavailable()
        {
            if (RecordLinuxWarning)
            {
                Calls.Add("linux-warning");
                return;
            }

            base.WarnFuseUnavailable();
        }

        protected override void DownloadFile(string url, AbsolutePath destination)
        {
            DownloadUrls.Add(url);
            DownloadDestinations.Add(destination);
            if (CreateDownloadedFile)
                File.WriteAllText(destination, "appimagetool");
            if (ThrowAfterDownload)
                throw new InvalidOperationException("Download failed.");
        }

        protected override void ExecuteShell(string command, AbsolutePath workingDirectory)
        {
            ShellCommands.Add(command);
            ShellWorkingDirectories.Add(workingDirectory);
            if (command.EndsWith(" --appimage-extract", StringComparison.Ordinal) &&
                (CreateExtractionDirectory || CreateExtractedAppRun))
            {
                var extractedPath = workingDirectory / "squashfs-root";
                Directory.CreateDirectory(extractedPath);
                if (CreateExtractedAppRun)
                    File.WriteAllText(extractedPath / "AppRun", "appimagetool");
            }

            if (command.StartsWith("ARCH=", StringComparison.Ordinal))
            {
                AppDirPathDuringBuild = workingDirectory;
                AppDirExistedDuringBuild = Directory.Exists(workingDirectory);
                OutputExistedWhenBuildStarted = File.Exists(TestAppImageOutputPath);
                FinalOutputExistedWhenBuildStarted = File.Exists(TestFinalAppImageOutputPath);
                if (CreateAppImageOutput)
                    File.WriteAllText(TestAppImageOutputPath, "image");
                if (CreateAppImageOutputDirectory)
                    Directory.CreateDirectory(TestAppImageOutputPath);
                if (ThrowAfterAppImageBuild)
                    throw new InvalidOperationException("AppImage build failed.");
            }
        }

        protected override void SignMacOSApp(AbsolutePath appPath)
        {
            SignedAppPaths.Add(appPath);
        }

        protected override void BuildWindowsInstaller(Project project, PublishRidContext context,
            AbsolutePath sourcePath, string platform)
        {
            Calls.Add($"installer:{context.RuntimeIdentifier}:{platform}");
            InstallerSourcePaths.Add(sourcePath);

            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(sourcePath, TestBuildRuntimeCacheFileName)));
            Assert.Equal(context.RuntimeIdentifier, manifest.RootElement.GetProperty("Runtime").GetString());
            Assert.True(manifest.RootElement.GetProperty("IsBundle").GetBoolean());
            Assert.Equal(nameof(ApplicationBundleType.WindowsInstaller),
                manifest.RootElement.GetProperty("BundleType").GetString());
        }

        protected override void ExecuteDotNetPublish(DotNetPublishSettings settings)
        {
            if (CaptureSingleFileInputs)
            {
                CapturedRuntimeManifestPath =
                    Assert.IsType<JsonElement>(settings.Properties["FalloutBuildRuntimeManifest"]).GetString()!;
                CapturedSingleFileTargetsPath =
                    Assert.IsType<JsonElement>(settings.Properties["CustomAfterMicrosoftCommonTargets"]).GetString()!;
                CapturedRuntimeManifest = File.ReadAllText(CapturedRuntimeManifestPath);
                CapturedSingleFileTargets = File.ReadAllText(CapturedSingleFileTargetsPath);
            }

            Calls.Add("publish");
        }

        protected override void PreparePublishedOutput(PublishRidContext context)
        {
            if (UseDefaultPreparation)
            {
                base.PreparePublishedOutput(context);
                return;
            }

            Calls.Add("prepare");
        }
    }
}