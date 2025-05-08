using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Updatum.Extensions;

namespace Updatum;

/// <summary>
/// Utilities methods to identify the entry application.
/// </summary>
public static class EntryApplication
{
    /// <summary>
    /// Gets the entry assembly name.
    /// </summary>
    public static string? AssemblyName => Assembly.GetEntryAssembly()?.GetName().Name;

    /// <summary>
    /// Gets the entry assembly location, that is the path for the .dll file.
    /// </summary>
    public static string? AssemblyLocation => Assembly.GetEntryAssembly()?.Location;

    /// <summary>
    /// Gets the entry assembly version.
    /// </summary>
    public static Version? AssemblyVersion => Assembly.GetEntryAssembly()?.GetName().Version;

    /// <summary>
    /// Gets the process name of the running application.
    /// </summary>
    public static string? ProcessName { get; }

    /// <summary>
    /// Checks if the application is running under a dotnet process.
    /// </summary>
    [MemberNotNullWhen(true, nameof(ProcessName), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsRunningFromDotNetProcess { get; }

    /// <summary>
    /// Gets the path to the running application if is a single-file app.
    /// </summary>
    public static readonly string? DotNetSingleFileAppPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

    /// <summary>
    /// Checks if the application is running under a single-file app.
    /// </summary>
    [MemberNotNullWhen(true, nameof(DotNetSingleFileAppPath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsDotNetSingleFileApp => !string.IsNullOrWhiteSpace(DotNetSingleFileAppPath);

    /// <summary>
    /// Gets the path to the running linux application image (AppImage).
    /// </summary>
    public static readonly string? LinuxAppImagePath = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null;

    /// <summary>
    /// Checks if the application is running under linux application image (AppImage).
    /// </summary>
    [MemberNotNullWhen(true, nameof(LinuxAppImagePath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsLinuxAppImage => !string.IsNullOrWhiteSpace(LinuxAppImagePath);


    /// <summary>
    /// Gets the path to the running macOS application bundle if is a macOS app bundle.
    /// </summary>
    public static readonly string? MacOSAppBundlePath;

    /// <summary>
    /// Checks if the application is running under a macOS app bundle.
    /// </summary>
    [MemberNotNullWhen(true, nameof(MacOSAppBundlePath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsMacOSAppBundle => !string.IsNullOrWhiteSpace(MacOSAppBundlePath);

    /// <summary>
    /// Gets the full path to the entry executable of the running application.
    /// </summary>
    /// <remarks>Note the executable is from entry point and not the app executable itself.<br/>
    /// It's expected to be different from <see cref="Environment.ProcessPath"/> in some cases.<br/>
    /// Example: The MyApp.AppImage, MyApp.app will be returned instead of the app executable.</remarks>
    public static readonly string? ExecutablePath;

    /// <summary>
    /// Gets the file name of the entry executable of the running application.
    /// </summary>
    public static string? ExecutableFileName { get; }

    /// <summary>
    /// Gets the base directory of the entry executable of the running application.<br/>
    /// This is the directory where the entry executable is located.
    /// </summary>
    public static string? BaseDirectory { get; }


    /// <summary>
    /// Gets if the executable path is known, <see cref="ExecutablePath"/> is not null.
    /// </summary>
    [MemberNotNull(nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsExecutablePathKnown { get; }

    /// <summary>
    /// Gets the application bundle type.
    /// </summary>
    public static readonly ApplicationBundleType BundleType;

    /// <summary>
    /// Checks if the application is running under a bundled application.<br/>
    /// Example: dotnet single-file, linux AppImage, macOS app bundle.
    /// </summary>
    public static bool IsAppBundled => BundleType
        is ApplicationBundleType.DotNetSingleFile
        or ApplicationBundleType.LinuxAppImage
        or ApplicationBundleType.MacOSAppBundle;

    /// <summary>
    /// Checks if the application is running under a bundled single-file application that extracts itself.<br/>
    /// Example: dotnet single-file, linux AppImage.
    /// </summary>
    public static bool IsSingleFileApp => BundleType
        is ApplicationBundleType.DotNetSingleFile
        or ApplicationBundleType.LinuxAppImage;

    static EntryApplication()
    {
        if (!OperatingSystem.IsMacOS())
        {
            var pathRequirement = Path.Combine(".app", "Contents", $"MacOS{Path.DirectorySeparatorChar}");
            var index = AppContext.BaseDirectory.IndexOf(pathRequirement, StringComparison.Ordinal);
            if (index >= 2)
            {
                MacOSAppBundlePath = AppContext.BaseDirectory[..(index + 4)];
            }
        }

        ExecutablePath = DotNetSingleFileAppPath
            ?? LinuxAppImagePath
            ?? MacOSAppBundlePath
            ?? Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            ExecutableFileName = Path.GetFileName(ExecutablePath);
            BaseDirectory = Path.GetDirectoryName(ExecutablePath);
            IsExecutablePathKnown = true;
        }

        if (IsDotNetSingleFileApp) BundleType = ApplicationBundleType.DotNetSingleFile;
        else if (IsLinuxAppImage) BundleType = ApplicationBundleType.LinuxAppImage;
        else if (IsMacOSAppBundle) BundleType = ApplicationBundleType.MacOSAppBundle;
        else BundleType = ApplicationBundleType.None;

        var currentProcess = Process.GetCurrentProcess();
        ProcessName = Path.GetFileName(Environment.ProcessPath) ?? currentProcess.ProcessName;
        if (!string.IsNullOrWhiteSpace(ProcessName) && OperatingSystem.IsWindows() && !ProcessName.Contains('.')) ProcessName += ".exe";
        IsRunningFromDotNetProcess = ProcessName is "dotnet" or "dotnet.exe";
    }

    /// <summary>
    /// Launches a new instance of the application with the given arguments.
    /// </summary>
    /// <param name="runArguments">Arguments to pass within the application</param>
    /// <returns>True if able to launch a new instance, otherwise false.</returns>
    public static bool LaunchNewInstance(string? runArguments = null)
    {
        if (!IsExecutablePathKnown) return false;
        if (!File.Exists(ExecutablePath)) return false;

        if (IsRunningFromDotNetProcess)
        {
            if (string.IsNullOrWhiteSpace(AssemblyLocation)) return false;
            runArguments = $"\"{AssemblyLocation}\" {runArguments}";
        }

        try
        {
            Utilities.StartProcess(ExecutablePath, runArguments);
            return true;
        }
        catch
        {
            return false;
        }
    }
}