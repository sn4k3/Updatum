using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Serilog;
using StageKit.Runtime;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Selects and creates the configured macOS application bundles.
    /// </summary>
    /// <param name="contexts">The successfully published runtime contexts.</param>
    protected virtual void CreateMacOSApps(IReadOnlyCollection<PublishRidContext> contexts)
    {
        var macContexts = contexts
            .Where(context => context.RuntimeIdentifier.StartsWith("osx-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (macContexts.Length == 0)
            return;

        if (!IsUnixHost)
        {
            WarnMacOSAppsUnsupportedHost();
            return;
        }

        if (!PublishMultiArch)
        {
            foreach (var context in macContexts)
                CreateMacOSApp(context);

            return;
        }

        var x64Context = macContexts.FirstOrDefault(context =>
            context.RuntimeIdentifier.Equals("osx-x64", StringComparison.OrdinalIgnoreCase));
        var arm64Context = macContexts.FirstOrDefault(context =>
            context.RuntimeIdentifier.Equals("osx-arm64", StringComparison.OrdinalIgnoreCase));
        if (x64Context is null || arm64Context is null)
        {
            throw new InvalidOperationException(
                "A multi-architecture macOS app requires both 'osx-x64' and 'osx-arm64' publish outputs.");
        }

        CreateMultiArchMacOSApp(x64Context, arm64Context);
    }

    /// <summary>
    /// Logs that macOS application bundles cannot be created on the current host.
    /// </summary>
    protected virtual void WarnMacOSAppsUnsupportedHost()
    {
        Log.Warning("Skipping macOS application bundles on a non-Unix host.");
    }

    /// <summary>
    /// Creates the common macOS application directory structure and metadata.
    /// </summary>
    /// <param name="stagingPath">The isolated staging directory.</param>
    /// <returns>The staged application bundle path.</returns>
    /// <exception cref="FileNotFoundException">The configured macOS icon does not exist.</exception>
    protected virtual AbsolutePath CreateMacOSAppLayout(AbsolutePath stagingPath)
    {
        if (!MacOSIconFile.FileExists())
        {
            throw new FileNotFoundException(
                $"The configured macOS application icon '{MacOSIconFile}' does not exist.",
                (string)MacOSIconFile);
        }

        var options = MacAppBundleOptions;
        ResolveMacOSPathOptions(options);
        var appPath = stagingPath / $"{SoftwareName}.app";
        var contentsPath = appPath / "Contents";
        var executablePath = contentsPath / "MacOS";
        var resourcesPath = contentsPath / "Resources";
        executablePath.CreateDirectory();
        resourcesPath.CreateDirectory();

        var iconFileName = options.IconFileName!;
        MacOSIconFile.Copy(resourcesPath / iconFileName, ExistsPolicy.FileOverwrite);
        (contentsPath / "Info.plist").WriteAllText(
            MacAppBundle.GetInfoPList(options).ReplaceLineEndings("\n"));
        (contentsPath / $"{SoftwareName}.entitlements").WriteAllText(
            MacAppBundle.GetEntitlements(options).ReplaceLineEndings("\n"));

        return appPath;
    }

    /// <summary>
    /// Creates one single-architecture macOS application archive.
    /// </summary>
    /// <param name="context">The macOS runtime publish context.</param>
    protected virtual void CreateMacOSApp(PublishRidContext context)
    {
        var stagingPath = PublishStagingDirectory / Guid.NewGuid().ToString("N");
        var archivePath = (AbsolutePath)$"{context.BundleOutputPath}.zip";

        try
        {
            stagingPath.DeleteDirectory();
            var appPath = CreateMacOSAppLayout(stagingPath);
            var executablePath = appPath / "Contents" / "MacOS";
            ValidateMacOSPayload(context, MacAppBundleOptions.ExecutableName!);
            context.PublishPath.Copy(executablePath, ExistsPolicy.MergeAndOverwrite);
            PublishUtilities.WriteRuntimeManifest(executablePath, BuildRuntimeCacheFileName,
                new BuildRuntime(context.RuntimeIdentifier, SoftwareVersion, true,
                    ApplicationBundleType.MacOSAppBundle));

            if (IsMacOSHost)
                SignMacOSApp(appPath);

            archivePath.DeleteFile();
            PublishUtilities.CreateZip(stagingPath, archivePath);
        }
        finally
        {
            stagingPath.DeleteDirectory();
        }
    }

    /// <summary>
    /// Creates one multi-architecture macOS application archive.
    /// </summary>
    /// <param name="x64Context">The Intel macOS runtime publish context.</param>
    /// <param name="arm64Context">The Apple Silicon macOS runtime publish context.</param>
    protected virtual void CreateMultiArchMacOSApp(PublishRidContext x64Context,
        PublishRidContext arm64Context)
    {
        var stagingPath = PublishStagingDirectory / Guid.NewGuid().ToString("N");
        var archivePath = PublishDirectory / $"{SoftwareName}_osx-multiarch_v{SoftwareVersion}.zip";

        try
        {
            stagingPath.DeleteDirectory();
            var appPath = CreateMacOSAppLayout(stagingPath);
            var executablePath = appPath / "Contents" / "MacOS";
            var options = MacAppBundleOptions;
            var executableName = options.ExecutableName!;
            var runtimePayloads = new[]
            {
                (Context: x64Context, DirectoryName: options.X64RuntimeIdentifier),
                (Context: arm64Context, DirectoryName: options.Arm64RuntimeIdentifier)
            };

            foreach (var payload in runtimePayloads)
            {
                ValidateMacOSPayload(payload.Context, executableName);
            }

            foreach (var payload in runtimePayloads)
            {
                var runtimePath = executablePath / payload.DirectoryName;
                payload.Context.PublishPath.Copy(runtimePath, ExistsPolicy.MergeAndOverwrite);
                PublishUtilities.WriteRuntimeManifest(runtimePath, BuildRuntimeCacheFileName,
                    new BuildRuntime("osx-multiarch", SoftwareVersion, true, ApplicationBundleType.MacOSAppBundle));
            }

            var launcherPath = executablePath / executableName;
            launcherPath.WriteAllText(
                MacAppBundle.GetMultiArchEntryScript(options).ReplaceLineEndings("\n"));
            UnixSystem.SetUnixExecutable(launcherPath);

            if (IsMacOSHost)
                SignMacOSApp(appPath);

            archivePath.DeleteFile();
            PublishUtilities.CreateZip(stagingPath, archivePath);
        }
        finally
        {
            stagingPath.DeleteDirectory();
        }
    }

    private void ResolveMacOSPathOptions(MacAppBundleOptions options)
    {
        options.IconFileName = ValidateMacOSPathName(
            options.IconFileName ?? $"{options.ProductName}.icns",
            $"{nameof(MacAppBundleOptions)}.{nameof(MacAppBundleOptions.IconFileName)}");

        var executableName = ValidateMacOSPathName(
            options.ExecutableName ?? options.ProductName,
            $"{nameof(MacAppBundleOptions)}.{nameof(MacAppBundleOptions.ExecutableName)}");
        options.ExecutableName = executableName;

        if (!PublishMultiArch)
            return;

        var x64RuntimeIdentifier = ValidateMacOSPathName(
            options.X64RuntimeIdentifier,
            $"{nameof(MacAppBundleOptions)}.{nameof(MacAppBundleOptions.X64RuntimeIdentifier)}");
        var arm64RuntimeIdentifier = ValidateMacOSPathName(
            options.Arm64RuntimeIdentifier,
            $"{nameof(MacAppBundleOptions)}.{nameof(MacAppBundleOptions.Arm64RuntimeIdentifier)}");

        if (x64RuntimeIdentifier.Equals(arm64RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{nameof(MacAppBundleOptions.X64RuntimeIdentifier)} and " +
                $"{nameof(MacAppBundleOptions.Arm64RuntimeIdentifier)} must be distinct.");
        }

        if (executableName.Equals(x64RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) ||
            executableName.Equals(arm64RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{nameof(MacAppBundleOptions.ExecutableName)} must be distinct from the runtime directory names.");
        }
    }

    private static string ValidateMacOSPathName(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathRooted(value) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value is "." or "..")
        {
            throw new InvalidOperationException($"{optionName} must be a nonblank simple leaf name.");
        }

        return value;
    }

    private static void ValidateMacOSPayload(PublishRidContext context, string executableName)
    {
        var executablePath = context.PublishPath / executableName;
        if (!executablePath.FileExists())
        {
            throw new FileNotFoundException(
                $"Published executable '{executablePath}' does not exist for '{context.RuntimeIdentifier}'.",
                (string)executablePath);
        }
    }

    /// <summary>
    /// Ad-hoc signs a completed macOS application bundle.
    /// </summary>
    /// <param name="appPath">The application bundle path.</param>
    protected virtual void SignMacOSApp(AbsolutePath appPath)
    {
        ProcessTasks.StartProcess("codesign", $"--force --deep --sign - \"{appPath}\"")
            .AssertWaitForExit();
    }
}