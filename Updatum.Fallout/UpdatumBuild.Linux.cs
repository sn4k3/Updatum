using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Fallout.Common.IO;
using Serilog;
using StageKit.Runtime;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Selects and creates the configured Linux AppImage bundles.
    /// </summary>
    /// <param name="contexts">The successfully published runtime contexts.</param>
    protected virtual void CreateLinuxAppImages(IReadOnlyCollection<PublishRidContext> contexts)
    {
        var linuxContexts = contexts
            .Select(context => (Context: context,
                Runtime: PublishUtilities.ParseRuntimeIdentifier(context.RuntimeIdentifier)))
            .Where(item => item.Runtime.Family is PublishRidFamily.Linux)
            .ToArray();
        if (linuxContexts.Length == 0)
            return;

        if (!IsLinuxHost)
        {
            WarnLinuxAppImagesUnsupportedHost();
            return;
        }

        foreach (var item in linuxContexts)
            CreateLinuxAppImage(item.Context, item.Runtime.AppImageArchitecture!);
    }

    /// <summary>
    /// Creates one Linux AppImage from a published runtime output.
    /// </summary>
    /// <param name="context">The Linux runtime publish context.</param>
    /// <param name="architecture">The AppImage target architecture.</param>
    protected virtual void CreateLinuxAppImage(PublishRidContext context, string architecture)
    {
        var appDirPath = AppImageStagingDirectory / Guid.NewGuid().ToString("N");
        var outputPath = (AbsolutePath)$"{context.BundleOutputPath}.AppImage";
        var temporaryOutputPath = CreateTemporaryAppImageOutputPath(outputPath);

        try
        {
            DeleteFileSystemEntry(appDirPath);
            DeleteFileSystemEntry(temporaryOutputPath);
            CreateLinuxAppDir(context, appDirPath);
            var appImageTool = PrepareAppImageTool();

            ExecuteShell(
                CreateAppImageBuildCommand(architecture, appImageTool, appDirPath, temporaryOutputPath),
                appDirPath);
            if (!temporaryOutputPath.FileExists())
            {
                throw new FileNotFoundException(
                    $"Temporary AppImage output '{temporaryOutputPath}' does not exist.",
                    (string)temporaryOutputPath);
            }

            UnixSystem.SetUnixExecutable(temporaryOutputPath);
            temporaryOutputPath.Move(outputPath, ExistsPolicy.FileOverwrite);
        }
        finally
        {
            try
            {
                DeleteFileSystemEntry(temporaryOutputPath);
            }
            finally
            {
                DeleteFileSystemEntry(appDirPath);
            }
        }
    }

    /// <summary>
    /// Creates a unique temporary AppImage output path beside the final artifact.
    /// </summary>
    /// <param name="outputPath">The final AppImage output path.</param>
    /// <returns>A unique temporary path on the same filesystem.</returns>
    protected virtual AbsolutePath CreateTemporaryAppImageOutputPath(AbsolutePath outputPath)
    {
        return outputPath.Parent / $".{outputPath.Name}.{Guid.NewGuid():N}.tmp";
    }

    /// <summary>
    /// Creates the complete AppDir layout and metadata for one Linux runtime.
    /// </summary>
    /// <param name="context">The Linux runtime publish context.</param>
    /// <param name="appDirPath">The isolated AppDir staging path.</param>
    /// <exception cref="FileNotFoundException">
    /// The configured Linux icon or resolved published executable does not exist.
    /// </exception>
    protected virtual void CreateLinuxAppDir(PublishRidContext context, AbsolutePath appDirPath)
    {
        var linuxIconFile = LinuxIconFile;
        if (!linuxIconFile.Extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The configured Linux application icon '{linuxIconFile}' must use the .svg extension.");
        }

        var options = LinuxAppBundleOptions;
        ResolveLinuxPathOptions(options);
        if (!linuxIconFile.FileExists())
        {
            throw new FileNotFoundException(
                $"The configured Linux application icon '{linuxIconFile}' does not exist.",
                (string)linuxIconFile);
        }

        var publishedExecutable = context.PublishPath / options.ExecutableName!;
        if (!publishedExecutable.FileExists())
        {
            throw new FileNotFoundException(
                $"Published executable '{publishedExecutable}' does not exist for '{context.RuntimeIdentifier}'.",
                (string)publishedExecutable);
        }

        var usrBinPath = appDirPath / "usr" / "bin";
        var applicationsPath = appDirPath / "usr" / "share" / "applications";
        var iconsPath = appDirPath / "usr" / "share" / "icons" / "hicolor" / "scalable" / "apps";
        var metainfoPath = appDirPath / "usr" / "share" / "metainfo";
        usrBinPath.CreateDirectory();
        applicationsPath.CreateDirectory();
        iconsPath.CreateDirectory();
        metainfoPath.CreateDirectory();

        var appRunPath = appDirPath / "AppRun";
        var desktopFileName = $"{options.ApplicationId}.desktop";
        var desktopPath = appDirPath / desktopFileName;
        var installedDesktopPath = applicationsPath / desktopFileName;
        var iconFileName = $"{options.IconName}.svg";
        var appStreamPath = metainfoPath / $"{options.ApplicationId}.appdata.xml";
        var appRun = LinuxAppBundle.GetAppRunScript(options);
        var desktop = LinuxAppBundle.GetDesktopEntry(options);
        var appStream = LinuxAppBundle.GetAppStreamMetadata(options);

        appRunPath.WriteAllText(appRun);
        UnixSystem.SetUnixExecutable(appRunPath);
        desktopPath.WriteAllText(desktop);
        installedDesktopPath.WriteAllText(desktop);
        appStreamPath.WriteAllText(appStream);
        linuxIconFile.Copy(appDirPath / iconFileName, ExistsPolicy.FileOverwrite);
        linuxIconFile.Copy(iconsPath / iconFileName, ExistsPolicy.FileOverwrite);

        context.PublishPath.Copy(usrBinPath, ExistsPolicy.MergeAndOverwrite);
        UnixSystem.SetUnixExecutable(usrBinPath / options.ExecutableName!);
        PublishUtilities.WriteRuntimeManifest(usrBinPath, BuildRuntimeCacheFileName,
            new BuildRuntime(context.RuntimeIdentifier, SoftwareVersion, true,
                ApplicationBundleType.LinuxAppImage));
    }

    private static void ResolveLinuxPathOptions(LinuxAppBundleOptions options)
    {
        options.ExecutableName = ValidateLinuxPathName(
            options.ExecutableName ?? options.ProductName,
            $"{nameof(LinuxAppBundleOptions)}.{nameof(LinuxAppBundleOptions.ExecutableName)}");
        options.IconName = ValidateLinuxPathName(
            options.IconName ?? options.ProductName,
            $"{nameof(LinuxAppBundleOptions)}.{nameof(LinuxAppBundleOptions.IconName)}");
        options.ApplicationId = ValidateLinuxPathName(
            options.ApplicationId,
            $"{nameof(LinuxAppBundleOptions)}.{nameof(LinuxAppBundleOptions.ApplicationId)}");
    }

    private static string ValidateLinuxPathName(string? value, string optionName)
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

    /// <summary>
    /// Downloads and extracts the appimagetool executable for the current host architecture.
    /// </summary>
    /// <returns>The cached extracted <c>AppRun</c> executable.</returns>
    protected virtual AbsolutePath PrepareAppImageTool()
    {
        var hostArchitecture = HostArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => throw new InvalidOperationException(
                $"Host architecture '{HostArchitecture}' is not supported by appimagetool.")
        };
        var cacheDirectory = AppImageToolCacheDirectory;
        var downloadedPath = cacheDirectory / $"appimagetool-{hostArchitecture}.AppImage";
        var extractedPath = cacheDirectory / $"squashfs-root-{hostArchitecture}";
        var extractedAppRun = extractedPath / "AppRun";

        cacheDirectory.CreateDirectory();
        if (extractedPath.DirectoryExists() && extractedAppRun.FileExists())
        {
            UnixSystem.SetUnixExecutable(extractedAppRun);
            return extractedAppRun;
        }

        DeleteFileSystemEntry(extractedPath);

        downloadedPath.DeleteDirectory();

        var downloadedNow = false;
        if (!downloadedPath.FileExists())
        {
            var url =
                $"{LinuxAppBundle.AppImageToolRepositoryUrl}/releases/download/continuous/appimagetool-{hostArchitecture}.AppImage";
            try
            {
                DownloadFile(url, downloadedPath);
                if (!downloadedPath.FileExists())
                    throw new FileNotFoundException($"Downloaded appimagetool '{downloadedPath}' does not exist.",
                        downloadedPath);

                UnixSystem.SetUnixExecutable(downloadedPath);
                downloadedNow = true;
            }
            catch
            {
                DeleteFileSystemEntry(downloadedPath);
                throw;
            }
        }

        if (!downloadedNow)
            UnixSystem.SetUnixExecutable(downloadedPath);

        if (!IsFuseAvailable)
            WarnFuseUnavailable();

        var extractionOutput = cacheDirectory / "squashfs-root";
        var extractionAppRun = extractionOutput / "AppRun";
        DeleteFileSystemEntry(extractionOutput);
        try
        {
            ExecuteShell(CreateAppImageToolExtractionCommand(downloadedPath), cacheDirectory);
            if (!extractionOutput.DirectoryExists())
            {
                throw new DirectoryNotFoundException(
                    $"appimagetool extraction output '{extractionOutput}' does not exist.");
            }

            if (!extractionAppRun.FileExists())
            {
                throw new FileNotFoundException(
                    $"Extracted appimagetool executable '{extractionAppRun}' does not exist.",
                    (string)extractionAppRun);
            }

            extractionOutput.Move(extractedPath);
        }
        finally
        {
            DeleteFileSystemEntry(extractionOutput);
        }

        UnixSystem.SetUnixExecutable(extractedAppRun);
        return extractedAppRun;
    }

    /// <summary>
    /// Composes the shell command that builds one AppImage.
    /// </summary>
    /// <param name="architecture">The target AppImage architecture.</param>
    /// <param name="appImageTool">The extracted appimagetool executable.</param>
    /// <param name="appDirPath">The staged AppDir path.</param>
    /// <param name="outputPath">The command output path.</param>
    /// <returns>The shell-safe AppImage build command.</returns>
    protected virtual string CreateAppImageBuildCommand(string architecture, AbsolutePath appImageTool,
        AbsolutePath appDirPath, AbsolutePath outputPath)
    {
        return $"ARCH={architecture} {AppBundleUtilities.QuoteShell(appImageTool)} " +
               $"{AppBundleUtilities.QuoteShell(appDirPath)} " +
               AppBundleUtilities.QuoteShell(outputPath);
    }

    /// <summary>
    /// Logs that AppImage bundles cannot be created on the current host.
    /// </summary>
    protected virtual void WarnLinuxAppImagesUnsupportedHost()
    {
        Log.Warning("Skipping Linux AppImage bundles on a non-Linux host.");
    }

    /// <summary>
    /// Logs that FUSE 2 is unavailable and extraction will be used instead.
    /// </summary>
    protected virtual void WarnFuseUnavailable()
    {
        Log.Warning("FUSE 2 is unavailable; appimagetool will be extracted before use.");
    }
}