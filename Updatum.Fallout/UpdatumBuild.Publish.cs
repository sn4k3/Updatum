using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Solutions;
using Serilog;
using StageKit.Runtime;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Gets the bundle types created during publishing.
    /// </summary>
    public PublishBundleTypes PublishBundles { get; protected set; } =
        PublishBundleTypes.Portable |
        PublishBundleTypes.DotNetSingleExecutable |
        PublishBundleTypes.WindowsInstaller |
        PublishBundleTypes.MacOSAppBundle |
        PublishBundleTypes.LinuxAppImage;

    /// <summary>
    /// Gets the file extensions removed directly from the publish directory after successful publishing.
    /// Specify extension names without a leading period.
    /// </summary>
    public string[] PublishCleanupExtensions { get; protected set; } = ["wixpdb"];

    /// <summary>
    /// Gets or sets a value indicating whether Windows installers should package the single-file executable.
    /// When <see langword="false"/>, installers use a separate normal publish while the standalone artifact
    /// remains single-file.
    /// </summary>
    [Parameter(
        "When single-file and Windows installer publishing are enabled, package the single-file executable in the installer. Default: False")]
    public bool PublishInstallerWithSingleFile { get; protected set; }

    /// <summary>
    /// Gets the icon file used for macOS application bundles.
    /// </summary>
    public virtual AbsolutePath MacOSIconFile => MediaDirectory / $"{SoftwareName}.icns";

    /// <summary>
    /// Gets the icon file used for Linux application bundles.
    /// </summary>
    public virtual AbsolutePath LinuxIconFile => MediaDirectory / $"{SoftwareName}.svg";

    /// <summary>
    /// Gets the installer projects discovered in the solution.
    /// </summary>
    public virtual IReadOnlyCollection<Project> InstallerProjects =>
        field ??= Solution.AllProjects.Where(IsWindowsInstallerProject).ToArray();

    /// <summary>
    /// Gets or sets the callback invoked before publishing each runtime identifier.
    /// </summary>
    public Action<PublishRidContext>? BeforePublishRid { get; set; }

    /// <summary>
    /// Gets or sets the callback that returns the base asset name for each runtime publish operation.
    /// The returned value must be a simple file name without a directory or extension.
    /// </summary>
    /// <example>MyApp_win-x64_v1.0.0<br/>
    /// MyApp</example>
    public Func<PublishRidContext, string> AssetName
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = context => $"{context.Build.SoftwareName}_{context.RuntimeIdentifier}_v{context.Build.SoftwareVersion}";

    /// <summary>
    /// Gets or sets the callback invoked after publishing each runtime identifier.
    /// </summary>
    public Action<PublishRidContext>? AfterPublishRid { get; set; }

    /// <summary>
    /// Gets or sets the callback that configures settings before publishing each runtime identifier.
    /// </summary>
    public Func<DotNetPublishSettings, PublishRidContext, DotNetPublishSettings>? ConfigurePublishRid { get; set; }

    /// <summary>
    /// Publishes all configured runtime identifiers and creates their selected bundles.
    /// </summary>
    public virtual Target Publish => d => d
        .DependsOn(Restore)
        .DependsOn(DependOnTargets)
        .Executes(ExecutePublish);

    /// <summary>
    /// Gets a value indicating whether the current host supports Unix application staging.
    /// </summary>
    protected virtual bool IsUnixHost => !OperatingSystem.IsWindows();

    /// <summary>
    /// Gets a value indicating whether the current host supports macOS code signing.
    /// </summary>
    protected virtual bool IsMacOSHost => OperatingSystem.IsMacOS();

    /// <summary>
    /// Gets a value indicating whether the current host supports AppImage creation.
    /// </summary>
    protected virtual bool IsLinuxHost => OperatingSystem.IsLinux();

    /// <summary>
    /// Gets the parent directory for temporary non-single-file bundle payloads.
    /// </summary>
    protected virtual AbsolutePath BundlePayloadDirectory => TemporaryDirectory / "bundle-publish";

    /// <summary>
    /// Gets the parent directory for temporary non-single-file installer payloads.
    /// </summary>
    protected virtual AbsolutePath InstallerPayloadDirectory => TemporaryDirectory / "installer-publish";

    /// <summary>
    /// Gets the parent directory for temporary single-file publish inputs.
    /// </summary>
    protected virtual AbsolutePath SingleFileInputsDirectory => TemporaryDirectory / "single-file-publish";

    /// <summary>
    /// Creates the default settings for a runtime publish operation.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    /// <returns>The configured publish settings.</returns>
    protected virtual DotNetPublishSettings CreatePublishSettings(PublishRidContext context)
    {
        var settings = new DotNetPublishSettings()
            .SetProject(MainProject.Path)
            .SetConfiguration(Configuration)
            .SetRuntime(context.RuntimeIdentifier)
            .SetOutput(context.PublishPath)
            .EnableSelfContained()
            .EnablePublishReadyToRun()
            .SetPublishSingleFile(PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable))
            .EnableNoRestore();

        if (PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable))
        {
            settings = settings
                .SetProperty("DebugType", "embedded")
                .SetProperty("PublishDocumentationFiles", false)
                .SetProperty("IncludeAllContentForSelfExtract", true)
                .SetProperty("IncludeNativeLibrariesForSelfExtract", true);
        }

        return settings;
    }


    /// <summary>
    /// Executes a .NET publish command.
    /// </summary>
    /// <param name="settings">The settings for the publish command.</param>
    protected virtual void ExecuteDotNetPublish(DotNetPublishSettings settings)
    {
        DotNetPublish(_ => settings);
    }

    /// <summary>
    /// Restores the runtime-specific assets required by one publish operation.
    /// </summary>
    /// <param name="runtimeIdentifier">The runtime identifier to restore.</param>
    protected virtual void RestorePublishRuntimeIdentifier(string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        DotNetRestore(settings => settings
            .SetProjectFile(MainProject)
            .SetRuntime(runtimeIdentifier)
            .EnablePublishReadyToRun());
    }

    /// <summary>
    /// Prepares published output for distribution.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    protected virtual void PreparePublishedOutput(PublishRidContext context)
    {
        var executablePath = context.PublishPath / SoftwareName;
        if (!OperatingSystem.IsWindows() &&
            !context.RuntimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
        {
            if (!executablePath.FileExists())
                throw new FileNotFoundException($"Published executable '{executablePath}' does not exist.",
                    executablePath);

            File.SetUnixFileMode(executablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        if (!PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable))
        {
            PublishUtilities.WriteRuntimeManifest(context.PublishPath, BuildRuntimeCacheFileName,
                new BuildRuntime(context.RuntimeIdentifier, SoftwareVersion, false, ApplicationBundleType.None));
        }
    }

    /// <summary>
    /// Publishes one runtime identifier through the configurable command pipeline.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="ConfigurePublishRid"/> returned <see langword="null"/>.
    /// </exception>
    protected virtual void PublishRuntime(PublishRidContext context)
    {
        BeforePublishRid?.Invoke(context);

        SingleFilePublishInputs? singleFileInputs = null;
        try
        {
            var settings = CreatePublishSettings(context);
            if (PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable))
            {
                singleFileInputs = CreateSingleFilePublishInputs(context);
                settings = settings
                    .SetProperty("FalloutBuildRuntimeManifest", singleFileInputs.ManifestPath)
                    .SetProperty("FalloutBuildRuntimeManifestFileName", BuildRuntimeCacheFileName)
                    .SetProperty("CustomAfterMicrosoftCommonTargets", singleFileInputs.TargetsPath);
            }

            if (ConfigurePublishRid is not null)
            {
                settings = ConfigurePublishRid(settings, context)
                           ?? throw new InvalidOperationException("ConfigurePublishRid returned null.");
            }

            ExecuteDotNetPublish(settings);
            PreparePublishedOutput(context);
            AfterPublishRid?.Invoke(context);
        }
        finally
        {
            singleFileInputs?.Delete();
        }
    }

    /// <summary>
    /// Executes the runtime publishing and bundling workflow.
    /// </summary>
    protected virtual void ExecutePublish()
    {
        var runtimeIdentifiers = PublishRid.ValidateRuntimeIdentifiers(RIds);
        var softwareName = PublishUtilities.ValidatePathLeafName(SoftwareName, nameof(SoftwareName));
        var softwareVersion = PublishUtilities.ValidatePathLeafName(SoftwareVersion, nameof(SoftwareVersion));
        _ = PublishUtilities.ValidatePathLeafName(BuildRuntimeCacheFileName, nameof(BuildRuntimeCacheFileName));
        var publishDirectory = PublishDirectory;
        var contexts = runtimeIdentifiers
            .Select(runtimeIdentifier => CreatePublishContext(
                runtimeIdentifier.RuntimeIdentifier,
                publishDirectory,
                softwareName,
                softwareVersion))
            .ToList();

        ReleaseNotes.WriteLatestReleaseNotes(ChangelogFile, ReleaseNotesFile);

        foreach (var context in contexts)
        {
            RestorePublishRuntimeIdentifier(context.RuntimeIdentifier);
            context.PublishPath.DeleteDirectory();
            DeleteStaleBundleArtifacts(context);
            PublishRuntime(context);
            CopySingleFileExecutable(context);
        }

        if (!PublishNoBundles)
            CreateBundles(contexts);

        if (PublishDiscardNonBundles)
        {
            foreach (var context in contexts)
                context.PublishPath.DeleteDirectory();
        }

        DeletePublishFilesByExtension(publishDirectory);
        CleanupPublishTemporaryDirectories();
    }

    /// <summary>
    /// Deletes the bundle artifacts left over from a previous publish of the same runtime identifier.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    protected virtual void DeleteStaleBundleArtifacts(PublishRidContext context)
    {
        ((AbsolutePath)$"{context.BundleOutputPath}.zip").DeleteFile();
        ((AbsolutePath)$"{context.BundleOutputPath}.AppImage").DeleteFile();
        GetSingleFileAssetPath(context).DeleteFile();
    }

    /// <summary>
    /// Removes the temporary payload roots created while publishing.
    /// </summary>
    /// <remarks>
    /// The appimagetool cache and the shared staging root are intentionally preserved; per-bundle staging
    /// directories below the staging root are already removed by their own <c>finally</c> blocks.
    /// </remarks>
    protected virtual void CleanupPublishTemporaryDirectories()
    {
        foreach (var directory in new[]
                 {
                     BundlePayloadDirectory, InstallerPayloadDirectory, SingleFileInputsDirectory
                 })
        {
            try
            {
                directory.DeleteDirectory();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Debug(exception, "Could not remove the temporary directory {Directory}.", directory);
            }
        }
    }

    /// <summary>
    /// Deletes files with configured cleanup extensions directly from the publish directory.
    /// </summary>
    /// <param name="publishDirectory">The publish directory to clean.</param>
    /// <exception cref="InvalidOperationException">
    /// <see cref="PublishCleanupExtensions"/> contains a blank extension.
    /// </exception>
    protected virtual void DeletePublishFilesByExtension(AbsolutePath publishDirectory)
    {
        PublishUtilities.DeleteFilesByExtension(
            publishDirectory,
            PublishCleanupExtensions,
            nameof(PublishCleanupExtensions));
    }

    private PublishRidContext CreatePublishContext(
        string runtimeIdentifier,
        AbsolutePath publishDirectory,
        string softwareName,
        string softwareVersion)
    {
        var defaultAssetName = $"{softwareName}_{runtimeIdentifier}_v{softwareVersion}";
        var context = new PublishRidContext
        {
            Build = this,
            RuntimeIdentifier = runtimeIdentifier,
            PublishPath = PublishUtilities.GetDirectChildPath(
                publishDirectory,
                defaultAssetName,
                "Publish artifact name")
        };
        var assetName = PublishUtilities.ValidatePathLeafName(AssetName(context), nameof(AssetName));

        return string.Equals(assetName, defaultAssetName, StringComparison.Ordinal)
            ? context
            : context with
            {
                PublishPath = PublishUtilities.GetDirectChildPath(publishDirectory, assetName, nameof(AssetName))
            };
    }

    /// <summary>
    /// Creates bundles from the published runtime output.
    /// </summary>
    /// <param name="contexts">The successfully published runtime contexts.</param>
    protected virtual void CreateBundles(IReadOnlyCollection<PublishRidContext> contexts)
    {
        var bundleContexts = CreateBundlePayloads(contexts);
        try
        {
            if (PublishBundles.HasFlag(PublishBundleTypes.Portable))
            {
                foreach (var context in bundleContexts)
                {
                    var runtime = PublishRid.ParseRuntimeIdentifier(context.RuntimeIdentifier);
                    if (ShouldCreatePortableZip(context.RuntimeIdentifier) ||
                        !IsUnixHost ||
                        PublishMultiArch)
                    {
                        CreatePortableZip(context);
                    }
                }
            }

            foreach (var context in contexts)
            {
                var runtime = PublishRid.ParseRuntimeIdentifier(context.RuntimeIdentifier);

                if (PublishBundles.HasFlag(PublishBundleTypes.WindowsInstaller) &&
                    runtime.Family is PublishRidFamily.Windows)
                {
                    CreateWindowsInstallers(context, runtime.InstallerPlatform!);
                }
            }

            if (PublishBundles.HasFlag(PublishBundleTypes.MacOSAppBundle))
                CreateMacOSApps(bundleContexts);

            if (PublishBundles.HasFlag(PublishBundleTypes.LinuxAppImage))
                CreateLinuxAppImages(bundleContexts);
        }
        finally
        {
            DeleteBundlePayloads(bundleContexts, contexts);
        }
    }

    /// <summary>
    /// Creates normal publish outputs for bundle formats that cannot use a single-file payload.
    /// </summary>
    /// <param name="contexts">The primary publish contexts.</param>
    /// <returns>Contexts pointing to normal bundle payloads where required.</returns>
    protected virtual IReadOnlyCollection<PublishRidContext> CreateBundlePayloads(
        IReadOnlyCollection<PublishRidContext> contexts)
    {
        if (!PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable) ||
            (!PublishBundles.HasFlag(PublishBundleTypes.Portable) &&
             !PublishBundles.HasFlag(PublishBundleTypes.MacOSAppBundle) &&
             !PublishBundles.HasFlag(PublishBundleTypes.LinuxAppImage)))
        {
            return contexts;
        }

        var bundleContexts = new List<PublishRidContext>(contexts.Count);
        try
        {
            foreach (var context in contexts)
            {
                var runtime = PublishRid.ParseRuntimeIdentifier(context.RuntimeIdentifier);
                var requiresNormalPayload =
                    PublishBundles.HasFlag(PublishBundleTypes.Portable) ||
                    (runtime.Family is PublishRidFamily.MacOS &&
                     PublishBundles.HasFlag(PublishBundleTypes.MacOSAppBundle)) ||
                    (runtime.Family is PublishRidFamily.Linux &&
                     PublishBundles.HasFlag(PublishBundleTypes.LinuxAppImage));

                if (!requiresNormalPayload)
                {
                    bundleContexts.Add(context);
                    continue;
                }

                var outputPath = BundlePayloadDirectory / Guid.NewGuid().ToString("N");
                outputPath.CreateOrCleanDirectory();
                ExecuteDotNetPublish(CreateBundlePublishSettings(context, outputPath));
                bundleContexts.Add(new PublishRidContext
                {
                    Build = this,
                    RuntimeIdentifier = context.RuntimeIdentifier,
                    PublishPath = outputPath,
                    BundleOutputPath = context.BundleOutputPath
                });
            }

            return bundleContexts;
        }
        catch
        {
            DeleteBundlePayloads(bundleContexts, contexts);
            throw;
        }
    }

    /// <summary>
    /// Creates publish settings for a normal, non-single-file application bundle payload.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    /// <param name="outputPath">The temporary bundle payload path.</param>
    /// <returns>Publish settings for the normal payload.</returns>
    protected virtual DotNetPublishSettings CreateBundlePublishSettings(
        PublishRidContext context,
        AbsolutePath outputPath)
    {
        return CreatePublishSettings(context)
            .SetOutput(outputPath)
            .SetPublishSingleFile(false)
            .SetProperty("DebugType", "portable")
            .SetProperty("PublishDocumentationFiles", true)
            .SetProperty("IncludeAllContentForSelfExtract", false)
            .SetProperty("IncludeNativeLibrariesForSelfExtract", false)
            .DisableNoRestore();
    }

    private static void DeleteBundlePayloads(
        IReadOnlyCollection<PublishRidContext> bundleContexts,
        IReadOnlyCollection<PublishRidContext> originalContexts)
    {
        var originalPaths = originalContexts
            .Select(context => context.PublishPath.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var context in bundleContexts)
        {
            if (originalPaths.Contains(context.PublishPath))
                continue;

            context.PublishPath.DeleteDirectory();
        }
    }



}