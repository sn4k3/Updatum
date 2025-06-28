using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Updatum.Extensions;

namespace Updatum;

/// <summary>
/// Utilities methods to identify the entry application.
/// </summary>
public static class EntryApplication
{
    /// <summary>
    /// Gets the generic runtime identifier for the current operating system.<br/>
    /// This prevents from returning specific runtime and versions like ubuntu-22.04-x64 when using <see cref="RuntimeInformation.RuntimeIdentifier"/>
    /// </summary>
    public static string GenericRuntimeIdentifier =>
        OperatingSystem.IsWindows() ? $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" :
        OperatingSystem.IsMacOS()   ? $"osx-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" :
        OperatingSystem.IsLinux()   ? $"linux-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" :
        RuntimeInformation.RuntimeIdentifier;

    /// <summary>
    /// Gets the target framework of the entry assembly, as specified by the <see cref="TargetFrameworkAttribute"/>.
    /// </summary>
    public static TargetFrameworkAttribute? AssemblyTargetFramework => Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>();

    /// <summary>
    /// Gets the configuration of the entry assembly, as specified by the <see cref="AssemblyConfigurationAttribute"/>.
    /// </summary>
    /// <example>Release</example>
    public static string? AssemblyConfiguration => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration;

    /// <summary>
    /// Gets the title of the entry assembly, as specified by the <see cref="AssemblyTitleAttribute"/>,
    /// this provides a human-friendly title for the assembly (e.g., for display in Windows Explorer or installer UIs).<br />
    /// If not found, it will return the <see cref="AssemblyName"/>.
    /// </summary>
    /// <example>My Awesome Application</example>
    public static string? AssemblyTitle => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? AssemblyName;

    /// <summary>
    /// Gets the product name of the entry assembly, as specified by the <see cref="AssemblyProductAttribute"/>,
    /// typically the broader software product this assembly is part of.<br />
    /// If not found, it will return the <see cref="AssemblyName"/>.
    /// </summary>
    /// <example>Microsoft Office</example>
    public static string? AssemblyProduct => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? AssemblyName;

    /// <summary>
    /// Gets the simple name of the entry assembly (also known as the "short name").<br />
    /// This is the actual filename of the assembly (without the .dll or .exe extension).
    /// </summary>
    /// <example>If your assembly is MyApp.dll, <see cref="AssemblyName"/> returns "MyApp".</example>
    public static string? AssemblyName => Assembly.GetEntryAssembly()?.GetName().Name;

    /// <summary>
    /// Gets the description of the entry assembly, as specified by the <see cref="AssemblyDescriptionAttribute"/>.
    /// </summary>
    public static string? AssemblyDescription => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

    /// <summary>
    /// Gets the copyright of the entry assembly, as specified by the <see cref="AssemblyCopyrightAttribute"/>.
    /// </summary>
    public static string? AssemblyCopyright => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

    /// <summary>
    /// Gets the company of the entry assembly, as specified by the <see cref="AssemblyCompanyAttribute"/>.
    /// </summary>
    public static string? AssemblyCompany => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;

    /// <summary>
    /// Gets the trademark of the entry assembly, as specified by the <see cref="AssemblyTrademarkAttribute"/>.
    /// </summary>
    public static string? AssemblyTrademark => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyTrademarkAttribute>()?.Trademark;

    /// <summary>
    ///Gets the authors of the entry assembly, as specified by the <see cref="AssemblyMetadataAttribute"/> with key "Authors".
    /// </summary>
    /// <remarks>It must be included with:<br/>
    /// &lt;ItemGroup&gt;<br/>
    ///     &lt;AssemblyMetadata Include="Authors" Value="$(Authors)"/&gt;<br/>
    /// &lt;/ItemGroup&gt;</remarks>
    public static string? AssemblyAuthors => Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "Authors")?.Value;

    /// <summary>
    /// Gets the repository URL of the entry assembly, as specified by the <see cref="AssemblyMetadataAttribute"/>.
    /// </summary>
    /// <example>https://github.com/sn4k3/Updatum</example>
    public static string? AssemblyRepositoryUrl => Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == "RepositoryUrl")?.Value;

    /// <summary>
    /// Gets the entry assembly location, that is the path for the .dll file.
    /// </summary>
    public static string? AssemblyLocation => Assembly.GetEntryAssembly()?.Location;

    /// <summary>
    /// Gets the entry assembly version, as specified by the <see cref="AssemblyVersionAttribute"/>.<br />
    /// </summary>
    /// <example>1.0.0.0</example>
    public static Version? AssemblyVersion => Assembly.GetEntryAssembly()?.GetName().Version;

    private static readonly Lazy<string?> AssemblyVersionStringLazy = new(() =>
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyVersionAttribute>()?.Version
                      ?? AssemblyInformationalVersion
                      ?? AssemblyVersion?.ToString();
        if (version is null) return null;
        var indexOf = version.IndexOf('+');
        return indexOf <= 0
            ? version
            : version[..indexOf]; // If the version contains a commit hash, we only return the version part
    });

    /// <summary>
    /// Gets the entry assembly version, as specified by the <see cref="AssemblyVersionAttribute"/>, if null, fallbacks to the <see cref="AssemblyInformationalVersion"/>, but without the commit hash if present.
    /// </summary>
    /// <example>1.0.0-dev</example>
    public static string? AssemblyVersionString => AssemblyVersionStringLazy.Value;

    /// <summary>
    /// Gets the file version of the entry assembly, as specified by the <see cref="AssemblyFileVersionAttribute"/>.
    /// </summary>
    /// <example>1.0.0</example>
    public static string? AssemblyFileVersion => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

    /// <summary>
    /// Gets the informational version of the entry assembly, as specified by the <see cref="AssemblyInformationalVersionAttribute"/>.
    /// </summary>
    /// <example>1.0.0+1f288c6c1a39e887b3aa7035b0fed7a680522808</example>
    public static string? AssemblyInformationalVersion => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// Gets the process name of the running application.
    /// </summary>
    public static string ProcessName { get; }

    /// <summary>
    /// Gets the application bundle type.
    /// </summary>
    public static ApplicationBundleType BundleType { get; }

    /// <summary>
    /// Checks if the application is running under a bundled application.<br/>
    /// Example: dotnet single-file, linux AppImage, macOS app bundle.
    /// </summary>
    public static bool IsAppBundled => BundleType
        is ApplicationBundleType.DotNetSingleFile
        or ApplicationBundleType.LinuxAppImage
        or ApplicationBundleType.LinuxFlatpak
        or ApplicationBundleType.MacOSAppBundle;

    /// <summary>
    /// Checks if the application is running under a bundled single-file application that extracts itself.<br/>
    /// Example: dotnet single-file, linux AppImage.
    /// </summary>
    public static bool IsSingleFileApp => BundleType
        is ApplicationBundleType.DotNetSingleFile
        or ApplicationBundleType.LinuxAppImage
        or ApplicationBundleType.LinuxFlatpak;


    /// <summary>
    /// Checks if the application is running under a dotnet process.
    /// </summary>
    [MemberNotNullWhen(true, nameof(ProcessName), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsRunningFromDotNetProcess { get; }

    /// <summary>
    /// Gets the path to the running application if is a single-file app.
    /// </summary>
    public static string? DotNetSingleFileAppPath { get; } = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

    /// <summary>
    /// Checks if the application is running under a single-file app.
    /// </summary>
    [MemberNotNullWhen(true, nameof(DotNetSingleFileAppPath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsDotNetSingleFileApp => !string.IsNullOrWhiteSpace(DotNetSingleFileAppPath);

    /// <summary>
    /// Gets the path to the running linux application image (AppImage).
    /// </summary>
    public static string? LinuxAppImagePath { get; } = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null;

    /// <summary>
    /// Checks if the application is running under linux application image (AppImage).
    /// </summary>
    [MemberNotNullWhen(true, nameof(LinuxAppImagePath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsLinuxAppImage => !string.IsNullOrWhiteSpace(LinuxAppImagePath);

    /// <summary>
    /// Gets the path to the running linux flatpak.
    /// </summary>
    public static string? LinuxFlatpakPath { get; } = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("container") : null;

    /// <summary>
    /// Checks if the application is running under linux flatpak.
    /// </summary>
    [MemberNotNullWhen(true, nameof(LinuxFlatpakPath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsLinuxFlatpak => !string.IsNullOrWhiteSpace(LinuxFlatpakPath);


    /// <summary>
    /// Gets the path to the running macOS application bundle if is a macOS app bundle.
    /// </summary>
    public static string? MacOSAppBundlePath { get; }

    /// <summary>
    /// Checks if the application is running under a macOS app bundle.
    /// </summary>
    [MemberNotNullWhen(true, nameof(MacOSAppBundlePath), nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsMacOSAppBundle => !string.IsNullOrWhiteSpace(MacOSAppBundlePath);

    /// <summary>
    /// Gets the base directory of the entry executable of the running application.<br/>
    /// This is the directory where the entry executable is located.
    /// </summary>
    public static string? BaseDirectory { get; }

    /// <summary>
    /// Gets the full path to the entry executable of the running application.
    /// </summary>
    /// <remarks>Note the executable is from entry point and not the app executable itself.<br/>
    /// It's expected to be different from <see cref="Environment.ProcessPath"/> in some cases.<br/>
    /// Example: The MyApp.AppImage, MyApp.app will be returned instead of the app executable.<br/>
    /// If running from dotnet, it will return the AssemblyLocation, eg: myapp.dll.</remarks>
    public static string? ExecutablePath { get; }

    /// <summary>
    /// Gets the file name of the entry executable of the running application.
    /// </summary>
    public static string? ExecutableFileName { get; }

    /// <summary>
    /// Gets if the executable path is known, <see cref="ExecutablePath"/> is not null.
    /// </summary>
    [MemberNotNull(nameof(ExecutablePath), nameof(ExecutableFileName), nameof(BaseDirectory))]
    public static bool IsExecutablePathKnown { get; }

    /// <summary>
    /// Gets a formatted string containing the names and versions of all assemblies currently loaded in the application domain.
    /// </summary>
    /// <remarks>The assemblies are listed in the order they are loaded into the current application domain.
    /// The index is zero-padded based on the total number of assemblies to ensure consistent alignment.</remarks>
    public static string FormattedLoadedAssemblies
    {
        get
        {
            var sb = new StringBuilder();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembliesLengthPad = assemblies.Length.ToString().Length;
            for (var i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i].GetName();
                sb.AppendLine(string.Format($"{{0:D{assembliesLengthPad}}}: {{1}}, Version={{2}}", i + 1, assembly.Name, assembly.Version));
            }
            return sb.ToString().TrimEnd();
        }
    }

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

        ProcessName = Path.GetFileName(Environment.ProcessPath) ?? Process.GetCurrentProcess().ProcessName;
        if (!string.IsNullOrWhiteSpace(ProcessName) && OperatingSystem.IsWindows() && !ProcessName.Contains('.')) ProcessName += ".exe";
        IsRunningFromDotNetProcess = ProcessName is "dotnet" or "dotnet.exe";

        ExecutablePath = DotNetSingleFileAppPath
                        ?? LinuxAppImagePath
                        ?? LinuxFlatpakPath
                        ?? MacOSAppBundlePath
                        ?? (IsRunningFromDotNetProcess && !string.IsNullOrWhiteSpace(AssemblyLocation) ? AssemblyLocation : Environment.ProcessPath);

        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            BaseDirectory = AppContext.BaseDirectory;
        }
        else
        {
            ExecutableFileName = Path.GetFileName(ExecutablePath);
            BaseDirectory = Path.GetDirectoryName(ExecutablePath);
            IsExecutablePathKnown = true;
        }

        if (IsDotNetSingleFileApp) BundleType = ApplicationBundleType.DotNetSingleFile;
        else if (IsLinuxAppImage) BundleType = ApplicationBundleType.LinuxAppImage;
        else if (IsLinuxFlatpak) BundleType = ApplicationBundleType.LinuxFlatpak;
        else if (IsMacOSAppBundle) BundleType = ApplicationBundleType.MacOSAppBundle;
        else BundleType = ApplicationBundleType.None;
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

        try
        {
            if (IsRunningFromDotNetProcess)
            {
                Utilities.StartProcess(Environment.ProcessPath ?? ProcessName, $"\"{ExecutablePath}\" {runArguments}");
            }
            else
            {
                Utilities.StartProcess(ExecutablePath, runArguments);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Returns a string representation of the entry application information.
    /// </summary>
    /// <returns></returns>
    public new static string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{nameof(AssemblyTargetFramework)}: {AssemblyTargetFramework?.FrameworkDisplayName}  ({AssemblyTargetFramework?.FrameworkName})");
        sb.AppendLine($"{nameof(AssemblyConfiguration)}: {AssemblyConfiguration}");
        sb.AppendLine($"{nameof(AssemblyTitle)}: {AssemblyTitle}");
        sb.AppendLine($"{nameof(AssemblyProduct)}: {AssemblyProduct}");
        sb.AppendLine($"{nameof(AssemblyName)}: {AssemblyName}");
        sb.AppendLine($"{nameof(AssemblyDescription)}: {AssemblyDescription}");
        sb.AppendLine($"{nameof(AssemblyCopyright)}: {AssemblyCopyright}");
        sb.AppendLine($"{nameof(AssemblyCompany)}: {AssemblyCompany}");
        sb.AppendLine($"{nameof(AssemblyTrademark)}: {AssemblyTrademark}");
        sb.AppendLine($"{nameof(AssemblyAuthors)}: {AssemblyAuthors}");
        sb.AppendLine($"{nameof(AssemblyRepositoryUrl)}: {AssemblyRepositoryUrl}");
        sb.AppendLine($"{nameof(AssemblyLocation)}: {AssemblyLocation}");
        sb.AppendLine($"{nameof(AssemblyVersion)}: {AssemblyVersion}");
        sb.AppendLine($"{nameof(AssemblyVersionString)}: {AssemblyVersionString}");
        sb.AppendLine($"{nameof(AssemblyFileVersion)}: {AssemblyFileVersion}");
        sb.AppendLine($"{nameof(AssemblyInformationalVersion)}: {AssemblyInformationalVersion}");
        sb.AppendLine($"{nameof(ProcessName)}: {ProcessName}");

        // Bundle type
        sb.AppendLine($"{nameof(BundleType)}: {BundleType}");
        sb.AppendLine($"{nameof(IsAppBundled)}: {IsAppBundled}");
        sb.AppendLine($"{nameof(IsSingleFileApp)}: {IsSingleFileApp}");

        // DotNet specific
        sb.AppendLine($"{nameof(IsRunningFromDotNetProcess)}: {IsRunningFromDotNetProcess}");
        sb.AppendLine($"{nameof(IsDotNetSingleFileApp)}: {IsDotNetSingleFileApp}");
        if (IsDotNetSingleFileApp) sb.AppendLine($"{nameof(DotNetSingleFileAppPath)}: {DotNetSingleFileAppPath}");

        // Linux specific
        sb.AppendLine($"{nameof(IsLinuxAppImage)}: {IsLinuxAppImage}");
        if (IsLinuxAppImage) sb.AppendLine($"{nameof(LinuxAppImagePath)}: {LinuxAppImagePath}");

        // Linux specific
        sb.AppendLine($"{nameof(IsLinuxFlatpak)}: {IsLinuxFlatpak}");
        if (IsLinuxFlatpak) sb.AppendLine($"{nameof(LinuxFlatpakPath)}: {LinuxFlatpakPath}");

        // MacOS specific
        sb.AppendLine($"{nameof(IsMacOSAppBundle)}: {IsMacOSAppBundle}");
        if (IsMacOSAppBundle) sb.AppendLine($"{nameof(MacOSAppBundlePath)}: {MacOSAppBundlePath}");

        // Paths
        sb.AppendLine($"{nameof(BaseDirectory)}: {BaseDirectory}");
        sb.AppendLine($"{nameof(ExecutablePath)}: {ExecutablePath}");
        sb.AppendLine($"{nameof(ExecutableFileName)}: {ExecutableFileName}");
        sb.AppendLine($"{nameof(IsExecutablePathKnown)}: {IsExecutablePathKnown}");
        return sb.ToString();
    }
}