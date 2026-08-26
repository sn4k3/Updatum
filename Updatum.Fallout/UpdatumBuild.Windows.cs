using System;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using Fallout.Solutions;
using Serilog;
using StageKit.Runtime;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Creates the publish settings used for an installer payload.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    /// <param name="outputPath">The temporary normal-publish output path.</param>
    /// <returns>Settings for a non-single-file publish.</returns>
    protected virtual DotNetPublishSettings CreateWindowsInstallerPublishSettings(
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

    /// <summary>
    /// Publishes the normal, non-single-file payload used by an installer.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    /// <param name="outputPath">The temporary normal-publish output path.</param>
    protected virtual void PublishWindowsInstallerPayload(PublishRidContext context, AbsolutePath outputPath)
    {
        outputPath.DeleteDirectory();
        outputPath.CreateDirectory();
        ExecuteDotNetPublish(CreateWindowsInstallerPublishSettings(context, outputPath));
    }

    /// <summary>
    /// Creates Windows installers from one published Windows runtime output.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    /// <param name="platform">The installer platform.</param>
    protected virtual void CreateWindowsInstallers(PublishRidContext context, string platform)
    {
        if (!OperatingSystem.IsWindows())
        {
            Log.Warning("Skipping Windows installers for {RuntimeIdentifier} on a non-Windows host.",
                context.RuntimeIdentifier);
            return;
        }

        var installerProjects = InstallerProjects;
        if (installerProjects.Count == 0)
        {
            Log.Warning("Skipping Windows installers for {RuntimeIdentifier} because no installer project was found.",
                context.RuntimeIdentifier);
            return;
        }

        var stagingPath = PublishStagingDirectory / Guid.NewGuid().ToString("N");
        AbsolutePath? normalPublishPath = null;
        try
        {
            var installerSourcePath = context.PublishPath;
            if (PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable) &&
                !PublishInstallerWithSingleFile)
            {
                var installerPublishPath = TemporaryDirectory / "installer-publish" / Guid.NewGuid().ToString("N");
                normalPublishPath = installerPublishPath;
                PublishWindowsInstallerPayload(context, installerPublishPath);
                installerSourcePath = installerPublishPath;
            }

            stagingPath.DeleteDirectory();
            installerSourcePath.Copy(stagingPath, ExistsPolicy.MergeAndOverwrite);
            PublishUtilities.WriteRuntimeManifest(stagingPath, BuildRuntimeCacheFileName,
                new BuildRuntime(context.RuntimeIdentifier, SoftwareVersion, true,
                    ApplicationBundleType.WindowsInstaller));

            foreach (var project in installerProjects)
                BuildWindowsInstaller(project, context, stagingPath, platform);
        }
        finally
        {
            stagingPath.DeleteDirectory();
            normalPublishPath?.DeleteDirectory();
        }
    }

    /// <summary>
    /// Builds one installer project from a staged published payload.
    /// </summary>
    /// <param name="project">The installer project.</param>
    /// <param name="context">The runtime publish context.</param>
    /// <param name="sourcePath">The staged installer payload.</param>
    /// <param name="platform">The installer platform.</param>
    protected virtual void BuildWindowsInstaller(Project project, PublishRidContext context,
        AbsolutePath sourcePath, string platform)
    {
        DotNetBuild(settings => settings
            .SetProjectFile(project)
            .SetConfiguration(Configuration)
            .SetPlatform(platform)
            .SetOutputDirectory(PublishDirectory)
            .SetRuntime(context.RuntimeIdentifier)
            .SetProperty("PublishDirectory", sourcePath)
            .SetProperty("BuildVersion", SoftwareVersion)
            .SetProperty("ApplicationName", SoftwareName)
            .SetProperty("OutputName", context.BundleOutputPath.Name));
    }
}