using System;
using Fallout.Common.IO;
using StageKit.Runtime;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Determines whether a runtime identifier still needs a portable ZIP.
    /// </summary>
    /// <param name="runtimeIdentifier">The runtime identifier to inspect.</param>
    /// <returns>
    /// <c>false</c> when a macOS application bundle already ships that runtime as a ZIP; otherwise, <c>true</c>.
    /// </returns>
    protected virtual bool ShouldCreatePortableZip(string runtimeIdentifier)
    {
        return PublishRid.ParseRuntimeIdentifier(runtimeIdentifier).Family is not PublishRidFamily.MacOS ||
               !PublishBundles.HasFlag(PublishBundleTypes.MacOSAppBundle);
    }

    /// <summary>
    /// Creates a portable ZIP from one published runtime output.
    /// </summary>
    /// <param name="context">The runtime publish context.</param>
    protected virtual void CreatePortableZip(PublishRidContext context)
    {
        var stagingPath = PublishStagingDirectory / Guid.NewGuid().ToString("N");
        var archivePath = (AbsolutePath)$"{context.BundleOutputPath}.zip";

        try
        {
            stagingPath.DeleteDirectory();
            context.PublishPath.Copy(stagingPath, ExistsPolicy.MergeAndOverwrite);
            PublishUtilities.WriteRuntimeManifest(stagingPath, BuildRuntimeCacheFileName,
                new BuildRuntime(context.RuntimeIdentifier, SoftwareVersion, false, ApplicationBundleType.None));

            archivePath.DeleteFile();
            PublishUtilities.CreateZip(stagingPath, archivePath);
        }
        finally
        {
            stagingPath.DeleteDirectory();
        }
    }
}