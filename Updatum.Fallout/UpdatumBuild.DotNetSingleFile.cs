using System;
using System.IO;
using System.Xml.Linq;
using Fallout.Common.IO;
using StageKit.Runtime;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Copies a single-file executable beside the other publish artifacts.
    /// </summary>
    /// <param name="context">The successfully published runtime context.</param>
    protected virtual void CopySingleFileExecutable(PublishRidContext context)
    {
        if (!PublishBundles.HasFlag(PublishBundleTypes.DotNetSingleExecutable))
            return;

        var runtime = PublishRid.ParseRuntimeIdentifier(context.RuntimeIdentifier);
        var executableName = runtime.Family is PublishRidFamily.Windows
            ? $"{SoftwareName}.exe"
            : SoftwareName;
        var sourcePath = context.PublishPath / executableName;
        if (!sourcePath.FileExists())
        {
            throw new FileNotFoundException(
                $"Published single-file executable '{sourcePath}' does not exist.",
                sourcePath);
        }

        sourcePath.Copy(GetSingleFileAssetPath(context), ExistsPolicy.FileOverwrite);
    }

    private static AbsolutePath GetSingleFileAssetPath(PublishRidContext context)
    {
        var runtime = PublishRid.ParseRuntimeIdentifier(context.RuntimeIdentifier);
        var extension = runtime.Family is PublishRidFamily.Windows ? ".exe" : ".bin";
        return (AbsolutePath)$"{context.BundleOutputPath}{extension}";
    }

    private SingleFilePublishInputs CreateSingleFilePublishInputs(PublishRidContext context)
    {
        var directory = SingleFileInputsDirectory / Guid.NewGuid().ToString("N");
        directory.CreateDirectory();

        var manifestPath = directory / BuildRuntimeCacheFileName;
        var targetsPath = directory / "Fallout.SingleFile.targets";

        PublishUtilities.WriteRuntimeManifest(directory, BuildRuntimeCacheFileName,
            new BuildRuntime(context.RuntimeIdentifier, SoftwareVersion, true,
                ApplicationBundleType.DotNetSingleFile));

        var document = new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    new XElement("Content",
                        new XAttribute("Include", "$(FalloutBuildRuntimeManifest)"),
                        new XAttribute("Link", "$(FalloutBuildRuntimeManifestFileName)"),
                        new XElement("CopyToPublishDirectory", "PreserveNewest"),
                        new XElement("ExcludeFromSingleFile", false)))));
        document.Save(targetsPath);

        return new SingleFilePublishInputs(directory, manifestPath, targetsPath);
    }

    private sealed class SingleFilePublishInputs(
        AbsolutePath directory,
        AbsolutePath manifestPath,
        AbsolutePath targetsPath)
    {
        internal string ManifestPath { get; } = manifestPath;

        internal string TargetsPath { get; } = targetsPath;

        internal void Delete()
        {
            directory.DeleteDirectory();
        }
    }
}