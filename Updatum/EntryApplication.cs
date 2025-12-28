using System;
using System.Collections.Generic;
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
    #region Lazy Caches

    /// <summary>
    /// Provides a lazily initialized runtime identifier string for the current operating system and process
    /// architecture.
    /// </summary>
    /// <remarks>The runtime identifier is formatted as 'win-architecture', 'osx-architecture', or
    /// 'linux-architecture' depending on the detected platform. If the platform is not recognized, the default runtime
    /// identifier from the system is used. This value is computed only once and cached for subsequent
    /// accesses.</remarks>
    private static readonly Lazy<string> GenericRuntimeIdentifierLazy = new(() =>
        OperatingSystem.IsWindows() ? $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" :
        OperatingSystem.IsMacOS()   ? $"osx-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" :
        OperatingSystem.IsLinux()   ? $"linux-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}" :
        RuntimeInformation.RuntimeIdentifier);

    /// <summary>
    /// Provides lazy, thread-safe initialization of the entry assembly for the current application domain.
    /// </summary>
    /// <remarks>The entry assembly is typically the process executable in which the application started. In
    /// some hosting scenarios, such as unit tests or certain application models, the entry assembly may be
    /// null.</remarks>
    private static readonly Lazy<Assembly?> EntryAssemblyLazy = new(Assembly.GetEntryAssembly);

    /// <summary>
    /// Provides lazy initialization of the target framework attribute for the entry assembly.
    /// </summary>
    /// <remarks>The value is retrieved only once, on first access, and cached for subsequent accesses. If the
    /// entry assembly does not define a TargetFrameworkAttribute, the value will be null.</remarks>
    private static readonly Lazy<TargetFrameworkAttribute?> AssemblyTargetFrameworkLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<TargetFrameworkAttribute>());

    /// <summary>
    /// Provides lazy initialization for retrieving the configuration string from the entry assembly's
    /// AssemblyConfigurationAttribute, if present.
    /// </summary>
    /// <remarks>The value is obtained from the entry assembly's AssemblyConfigurationAttribute. If the
    /// attribute is not defined or the entry assembly is unavailable, the value will be null.</remarks>
    private static readonly Lazy<string?> AssemblyConfigurationLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration);

    /// <summary>
    /// Provides lazy initialization of the entry assembly's simple name.
    /// </summary>
    /// <remarks>The value is retrieved from the entry assembly at the time of first access. If the entry
    /// assembly is unavailable, the value will be null.</remarks>
    private static readonly Lazy<string?> AssemblyNameLazy = new(() =>
        EntryAssembly?.GetName().Name);

    /// <summary>
    /// Provides lazy initialization of the assembly title, retrieving it from the entry assembly's
    /// AssemblyTitleAttribute if available; otherwise, uses the assembly name.
    /// </summary>
    /// <remarks>This field is intended for internal use to efficiently obtain the assembly title only when
    /// needed. The value is computed once and cached for subsequent accesses.</remarks>
    private static readonly Lazy<string?> AssemblyTitleLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? AssemblyName);

    /// <summary>
    /// Provides lazy initialization for the product name of the entry assembly, if available.
    /// </summary>
    /// <remarks>If the entry assembly does not define an AssemblyProductAttribute, the assembly name is used
    /// as a fallback. The value is computed only once and cached for subsequent accesses.</remarks>
    private static readonly Lazy<string?> AssemblyProductLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? AssemblyName);

    /// <summary>
    /// Provides lazy initialization for retrieving the description of the entry assembly, if available.
    /// </summary>
    /// <remarks>The value is obtained from the <see cref="AssemblyDescriptionAttribute"/> of the entry
    /// assembly. If the entry assembly does not have a description attribute, the value will be <see
    /// langword="null"/>.</remarks>
    private static readonly Lazy<string?> AssemblyDescriptionLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description);

    /// <summary>
    /// Provides lazy initialization for retrieving the copyright information from the entry assembly's metadata.
    /// </summary>
    /// <remarks>The value is obtained from the <see cref="AssemblyCopyrightAttribute"/> of the entry
    /// assembly, if present. Accessing this member does not trigger assembly loading if the entry assembly is not
    /// already loaded.</remarks>
    private static readonly Lazy<string?> AssemblyCopyrightLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);

    /// <summary>
    /// Provides lazy initialization for retrieving the company name defined in the entry assembly's
    /// AssemblyCompanyAttribute.
    /// </summary>
    /// <remarks>The value is evaluated only once, upon first access. If the entry assembly does not define an
    /// AssemblyCompanyAttribute, the value will be null.</remarks>
    private static readonly Lazy<string?> AssemblyCompanyLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);

    /// <summary>
    /// Provides lazy initialization for retrieving the trademark information from the entry assembly's
    /// AssemblyTrademarkAttribute.
    /// </summary>
    /// <remarks>The value is obtained from the entry assembly's AssemblyTrademarkAttribute, if present. If
    /// the entry assembly does not define a trademark, the value will be null.</remarks>
    private static readonly Lazy<string?> AssemblyTrademarkLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyTrademarkAttribute>()?.Trademark);

    /// <summary>
    /// Provides lazy initialization for retrieving the value of the "Authors" assembly metadata attribute from the
    /// entry assembly.
    /// </summary>
    /// <remarks>The value is obtained from the first <see cref="AssemblyMetadataAttribute"/> with a key of
    /// "Authors" applied to the entry assembly. If the attribute is not present or the entry assembly is unavailable,
    /// the value will be <see langword="null"/>.</remarks>
    private static readonly Lazy<string?> AssemblyAuthorsLazy = new(() =>
        EntryAssembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "Authors")?.Value);

    /// <summary>
    /// Provides lazy access to the repository URL specified in the entry assembly's metadata, if available.
    /// </summary>
    /// <remarks>The repository URL is retrieved from the entry assembly's AssemblyMetadataAttribute with the
    /// key "RepositoryUrl". If the attribute is not present, the value will be null.</remarks>
    private static readonly Lazy<string?> AssemblyRepositoryUrlLazy = new(() =>
        EntryAssembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "RepositoryUrl")?.Value);

    /// <summary>
    /// Provides lazy initialization for the file system location of the entry assembly, or null if the location is
    /// unavailable.
    /// </summary>
    /// <remarks>The value is determined on first access and cached for subsequent use. The location may be
    /// null if the entry assembly is not available or its location is not set, such as in certain hosting
    /// scenarios.</remarks>
    private static readonly Lazy<string?> AssemblyLocationLazy = new(() =>
    {
        var location = EntryAssembly?.Location;
        return string.IsNullOrWhiteSpace(location) ? null : location;
    });

    /// <summary>
    /// Provides lazy initialization of the version information for the entry assembly.
    /// </summary>
    /// <remarks>The value is retrieved from the entry assembly's metadata when first accessed. If the entry
    /// assembly is not available, the value will be null.</remarks>
    private static readonly Lazy<Version?> AssemblyVersionLazy = new(() =>
        EntryAssembly?.GetName().Version);

    /// <summary>
    /// Provides lazy initialization for retrieving the file version of the entry assembly, if available.
    /// </summary>
    /// <remarks>The value is obtained from the <see cref="AssemblyFileVersionAttribute"/> of the entry
    /// assembly. If the entry assembly does not define this attribute, the value will be <see
    /// langword="null"/>.</remarks>
    private static readonly Lazy<string?> AssemblyFileVersionLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version);

    /// <summary>
    /// Provides lazy initialization for retrieving the informational version of the entry assembly, if available.
    /// </summary>
    /// <remarks>The informational version is typically specified using the
    /// AssemblyInformationalVersionAttribute in the assembly metadata. The value is retrieved only once, on first
    /// access, and cached for subsequent calls. If the entry assembly does not define an informational version, the
    /// value will be null.</remarks>
    private static readonly Lazy<string?> AssemblyInformationalVersionLazy = new(() =>
        EntryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Lazily retrieves the version string of the entry assembly, excluding any build metadata if present.
    /// </summary>
    /// <remarks>The version string is determined by checking the entry assembly's AssemblyVersionAttribute,
    /// then AssemblyInformationalVersion, and finally AssemblyVersion. If the version string contains build metadata
    /// (indicated by a '+' character), only the portion before the '+' is returned. Returns null if no version
    /// information is available.</remarks>
    private static readonly Lazy<string?> AssemblyVersionStringLazy = new(() =>
    {
        var version = EntryAssembly?.GetCustomAttribute<AssemblyVersionAttribute>()?.Version
                      ?? AssemblyInformationalVersion
                      ?? AssemblyVersion?.ToString();
        if (version is null) return null;
        var indexOf = version.IndexOf('+');
        return indexOf <= 0 ? version : version[..indexOf];
    });

    /// <summary>
    /// Provides lazy initialization for the path to the AppImage executable on Linux systems, or null if not running on
    /// Linux or if the environment variable is not set.
    /// </summary>
    /// <remarks>The value is determined by reading the 'APPIMAGE' environment variable only if the current
    /// operating system is Linux. On non-Linux systems, the value is always null.</remarks>
    private static readonly Lazy<string?> LinuxAppImagePathLazy = new(() =>
        OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("APPIMAGE") : null);

    /// <summary>
    /// Provides lazy initialization for the Flatpak application ID on Linux systems.
    /// </summary>
    /// <remarks>The value is retrieved from the FLATPAK_ID environment variable if the current operating
    /// system is Linux; otherwise, the value is null. Accessing this member does not trigger environment variable
    /// lookup until the value is requested.</remarks>
    private static readonly Lazy<string?> LinuxFlatpakIdLazy = new(() =>
        OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("FLATPAK_ID") : null);

    /// <summary>
    /// Provides lazy initialization for the value of the "container" environment variable on Linux systems.
    /// </summary>
    /// <remarks>The value is retrieved only if the current operating system is Linux; otherwise, it is null.
    /// This can be used to detect if the application is running inside a Flatpak or similar containerized environment
    /// on Linux.</remarks>
    private static readonly Lazy<string?> LinuxFlatpakPathLazy = new(() =>
        OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("container") : null);

    /// <summary>
    /// Provides a lazily initialized path to the root of the current application's macOS .app bundle, if running on
    /// macOS; otherwise, returns null.
    /// </summary>
    /// <remarks>This value is determined based on the application's base directory and is only available when
    /// running on macOS within a standard .app bundle structure. If the application is not running on macOS or is not
    /// packaged as a .app bundle, the value will be null.</remarks>
    private static readonly Lazy<string?> MacOSAppBundlePathLazy = new(() =>
    {
        if (!OperatingSystem.IsMacOS()) return null;

        const string appSuffix = ".app";
        var pathRequirement = $"{appSuffix}{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS{Path.DirectorySeparatorChar}";
        var index = AppContext.BaseDirectory.IndexOf(pathRequirement, StringComparison.OrdinalIgnoreCase);

        if (index >= 1)
        {
            var endIndex = index + appSuffix.Length;
            if (endIndex <= AppContext.BaseDirectory.Length)
            {
                return AppContext.BaseDirectory[..endIndex];
            }
        }

        return null;
    });

    /// <summary>
    /// Provides a lazily initialized string containing the current process name, including the ".exe" extension on
    /// Windows if not already present.
    /// </summary>
    /// <remarks>This value is determined based on the process path if available; otherwise, it falls back to
    /// the process name. On Windows, the ".exe" extension is appended if missing to ensure consistency with typical
    /// executable naming conventions.</remarks>
    private static readonly Lazy<string> ProcessNameLazy = new(() =>
    {
        var processName = Path.GetFileName(Environment.ProcessPath) ?? Process.GetCurrentProcess().ProcessName;
        if (!string.IsNullOrWhiteSpace(processName) && OperatingSystem.IsWindows() && !processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName += ".exe";
        }
        return processName;
    });

    /// <summary>
    /// Provides a lazily initialized value that indicates whether the current process is a .NET host process (such as
    /// 'dotnet' or 'dotnet.exe').
    /// </summary>
    /// <remarks>This value is computed once on first access and cached for subsequent use. Use this field to
    /// efficiently determine if the application is running under a generic .NET host, which may be relevant for
    /// diagnostics or environment-specific logic.</remarks>
    private static readonly Lazy<bool> IsRunningFromDotNetProcessLazy = new(() =>
        ProcessName is "dotnet" or "dotnet.exe");

    /// <summary>
    /// Lazily retrieves the path to the current executable when running as a .NET single-file application, or returns
    /// null if not applicable.
    /// </summary>
    /// <remarks>This value is determined by checking the 'DOTNET_HOST_PATH' environment variable, which is
    /// set when running as a single-file app. If the environment variable is not set, the process path is used as a
    /// fallback. Returns null if the application is running from a standard .NET process or if the assembly location is
    /// available, indicating that the app is not a single-file deployment.</remarks>
    private static readonly Lazy<string?> DotNetSingleFileAppPathLazy = new(() =>
    {
        if (IsRunningFromDotNetProcess || !string.IsNullOrWhiteSpace(AssemblyLocation))
            return null;

        // If not running from dotnet process, and assembly location is null or empty, is possibly a single-file app
        var executablePath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        // Fallback to process path if DOTNET_HOST_PATH is not set
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = Environment.ProcessPath;
        }

        return executablePath;
    });

    /// <summary>
    /// Lazily retrieves information about the current process's executable, including its path, name, and base
    /// directory.
    /// </summary>
    /// <remarks>This value attempts to determine the executable path using several platform-specific
    /// strategies. If the executable path cannot be determined, the tuple fields for path and name are null, the base
    /// directory is set to the application's base directory, and the flag is set to <see langword="false"/>. Accessing
    /// this value is thread-safe and the computation is performed only once.</remarks>
    private static readonly Lazy<(string? ExecutablePath, string? ExecutableName, string? BaseDirectory, bool IsExecutablePathKnown)> ExecutableInfoLazy = new(() =>
    {
        var executablePath = LinuxAppImagePath
                            ?? LinuxFlatpakPath
                            ?? MacOSAppBundlePath
                            ?? DotNetSingleFileAppPath
                            ?? (IsRunningFromDotNetProcess && !string.IsNullOrWhiteSpace(AssemblyLocation)
                                ? AssemblyLocation
                                : Environment.ProcessPath);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return (null, null, AppContext.BaseDirectory, false);
        }

        return (executablePath, Path.GetFileName(executablePath), Path.GetDirectoryName(executablePath), true);
    });

    /// <summary>
    /// Provides lazy initialization for the detected application bundle type based on the current runtime environment.
    /// </summary>
    /// <remarks>The bundle type is determined only once, on first access, and cached for subsequent use. This
    /// approach avoids repeated environment checks and improves performance when the bundle type is accessed multiple
    /// times.</remarks>
    private static readonly Lazy<ApplicationBundleType> BundleTypeLazy = new(() =>
    {
        if (IsLinuxAppImage) return ApplicationBundleType.LinuxAppImage;
        if (IsLinuxFlatpak) return ApplicationBundleType.LinuxFlatpak;
        if (IsMacOSAppBundle) return ApplicationBundleType.MacOSAppBundle;
        if (IsDotNetSingleFileApp) return ApplicationBundleType.DotNetSingleFile;
        return ApplicationBundleType.None;
    });

    #endregion

    #region Properties

    /// <summary>
    /// Gets the generic runtime identifier (RID) for the current platform.
    /// </summary>
    /// <remarks>The generic runtime identifier is a platform-agnostic string used to represent the current
    /// runtime environment. This value can be used for package resolution, deployment, or compatibility checks across
    /// different .NET platforms.</remarks>
    public static string GenericRuntimeIdentifier => GenericRuntimeIdentifierLazy.Value;

    /// <summary>
    /// Gets the process executable in the default application domain.
    /// </summary>
    /// <remarks>This property returns the assembly that started the process. In most cases, this is the entry
    /// point assembly of the application. If called from unmanaged code or in certain hosting scenarios (such as unit
    /// test runners or some ASP.NET environments), this property may return null.</remarks>
    public static Assembly? EntryAssembly => EntryAssemblyLazy.Value;

    /// <summary>
    /// Gets the target framework attribute of the currently executing assembly, if available, as specified by the <see cref="TargetFrameworkAttribute"/>.
    /// </summary>
    /// <remarks>Use this property to determine the target framework specified for the assembly at compile
    /// time. If the assembly does not define a target framework, the property returns null.</remarks>
    public static TargetFrameworkAttribute? AssemblyTargetFramework => AssemblyTargetFrameworkLazy.Value;

    /// <summary>
    /// Gets the assembly configuration string for the current application, such as "Debug" or "Release", as specified by the <see cref="AssemblyConfigurationAttribute"/>.
    /// </summary>
    /// <remarks>The assembly configuration is typically specified in the assembly's metadata and may be used
    /// to distinguish between different build configurations. If the configuration is not defined, this property
    /// returns null.</remarks>
    public static string? AssemblyConfiguration => AssemblyConfigurationLazy.Value;

    /// <summary>
    /// Gets the title of the entry assembly, as specified by the <see cref="AssemblyTitleAttribute"/>,
    /// this provides a human-friendly title for the assembly (e.g., for display in Windows Explorer or installer UIs).<br />
    /// If not found, it will return the <see cref="AssemblyName"/>.
    /// </summary>
    /// <example>My Awesome Application</example>
    public static string? AssemblyTitle => AssemblyTitleLazy.Value;

    /// <summary>
    /// Gets the product name of the entry assembly, as specified by the <see cref="AssemblyProductAttribute"/>,
    /// typically the broader software product this assembly is part of.<br />
    /// If not found, it will return the <see cref="AssemblyName"/>.
    /// </summary>
    /// <example>Microsoft Office</example>
    public static string? AssemblyProduct => AssemblyProductLazy.Value;

    /// <summary>
    /// Gets the simple name of the entry assembly (also known as the "short name").<br />
    /// This is the actual filename of the assembly (without the .dll or .exe extension).
    /// </summary>
    /// <example>If your assembly is MyApp.dll, <see cref="AssemblyName"/> returns "MyApp".</example>
    public static string? AssemblyName => AssemblyNameLazy.Value;

    /// <summary>
    /// Gets the description of the entry assembly
    /// </summary>
    public static string? AssemblyDescription => AssemblyDescriptionLazy.Value;

    /// <summary>
    /// Gets the copyright information of the entry assembly, as specified by the <see cref="AssemblyCopyrightAttribute"/>.
    /// </summary>
    public static string? AssemblyCopyright => AssemblyCopyrightLazy.Value;

    /// <summary>
    /// Gets the copyright information of the entry assembly, as specified by the <see cref="AssemblyCopyrightAttribute"/>.
    /// </summary>
    public static string? AssemblyCompany => AssemblyCompanyLazy.Value;

    /// <summary>
    /// Gets the trademark information of the entry assembly, as specified by the <see cref="AssemblyTrademarkAttribute"/>.
    /// </summary>
    public static string? AssemblyTrademark => AssemblyTrademarkLazy.Value;

    /// <summary>
    /// Gets the authors of the entry assembly, as specified by the <see cref="AssemblyMetadataAttribute"/> with key "Authors".
    /// </summary>
    /// <remarks>It must be included with:<br/>
    /// &lt;ItemGroup&gt;<br/>
    ///     &lt;AssemblyMetadata Include="Authors" Value="$(Authors)"/&gt;<br/>
    /// &lt;/ItemGroup&gt;</remarks>
    public static string? AssemblyAuthors => AssemblyAuthorsLazy.Value;

    /// <summary>
    /// Gets the repository URL of the entry assembly, as specified by the <see cref="AssemblyMetadataAttribute"/>.
    /// </summary>
    /// <example>https://github.com/sn4k3/Updatum</example>
    public static string? AssemblyRepositoryUrl => AssemblyRepositoryUrlLazy.Value;

    /// <summary>
    /// Gets the full path to the currently executing assembly, or null if the location is unavailable.
    /// </summary>
    public static string? AssemblyLocation => AssemblyLocationLazy.Value;

    /// <summary>
    /// Gets the entry assembly version, as specified by the <see cref="AssemblyVersionAttribute"/>.<br />
    /// </summary>
    /// <example>1.0.0.0</example>
    public static Version? AssemblyVersion => AssemblyVersionLazy.Value;

    /// <summary>
    /// Gets the entry assembly version, as specified by the <see cref="AssemblyVersionAttribute"/>, if null, fallbacks to the <see cref="AssemblyInformationalVersion"/>, but without the commit hash if present.
    /// </summary>
    /// <example>1.0.0-dev</example>
    public static string? AssemblyVersionString => AssemblyVersionStringLazy.Value;

    /// <summary>
    /// Gets the file version of the entry assembly, as specified by the <see cref="AssemblyFileVersionAttribute"/>.
    /// </summary>
    /// <example>1.0.0</example>
    public static string? AssemblyFileVersion => AssemblyFileVersionLazy.Value;

    /// <summary>
    /// Gets the informational version of the entry assembly, as specified by the <see cref="AssemblyInformationalVersionAttribute"/>.
    /// </summary>
    /// <example>1.0.0+1f288c6c1a39e887b3aa7035b0fed7a680522808</example>
    public static string? AssemblyInformationalVersion => AssemblyInformationalVersionLazy.Value;

    /// <summary>
    /// Gets the process identifier (PID) of the currently running process.
    /// </summary>
    public static int ProcessId => Environment.ProcessId;

    /// <summary>
    /// Gets the name of the current process.
    /// </summary>
    public static string ProcessName => ProcessNameLazy.Value;

    /// <summary>
    /// Gets the type of application bundle currently in use.
    /// </summary>
    public static ApplicationBundleType BundleType => BundleTypeLazy.Value;

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
    [MemberNotNullWhen(true, nameof(ProcessName), nameof(ExecutablePath), nameof(ExecutableName), nameof(BaseDirectory))]
    public static bool IsRunningFromDotNetProcess => IsRunningFromDotNetProcessLazy.Value;

    /// <summary>
    /// Gets the path to the running application if is a single-file app (PublishSingleFile) bundled by dotnet.
    /// </summary>
    public static string? DotNetSingleFileAppPath => DotNetSingleFileAppPathLazy.Value;

    /// <summary>
    /// Checks if the application is running under a single-file app (PublishSingleFile) bundled by dotnet.
    /// </summary>
    [MemberNotNullWhen(true, nameof(DotNetSingleFileAppPath), nameof(ExecutablePath), nameof(ExecutableName), nameof(BaseDirectory))]
    public static bool IsDotNetSingleFileApp => !string.IsNullOrWhiteSpace(DotNetSingleFileAppPath);

    /// <summary>
    /// Gets the path to the running linux application image (AppImage).
    /// </summary>
    public static string? LinuxAppImagePath => LinuxAppImagePathLazy.Value;

    /// <summary>
    /// Checks if the application is running under linux application image (AppImage).
    /// </summary>
    [MemberNotNullWhen(true, nameof(LinuxAppImagePath), nameof(ExecutablePath), nameof(ExecutableName), nameof(BaseDirectory))]
    public static bool IsLinuxAppImage => !string.IsNullOrWhiteSpace(LinuxAppImagePath);

    /// <summary>
    /// Gets the id to the running linux flatpak.
    /// </summary>
    public static string? LinuxFlatpakId => LinuxFlatpakIdLazy.Value;

    /// <summary>
    /// Gets the path to the running linux flatpak.
    /// </summary>
    public static string? LinuxFlatpakPath => LinuxFlatpakPathLazy.Value;

    /// <summary>
    /// Checks if the application is running under linux flatpak.
    /// </summary>
    [MemberNotNullWhen(true, nameof(LinuxFlatpakPath), nameof(ExecutablePath), nameof(ExecutableName), nameof(BaseDirectory))]
    public static bool IsLinuxFlatpak => !string.IsNullOrWhiteSpace(LinuxFlatpakId);

    /// <summary>
    /// Gets the path to the running macOS application bundle if is a macOS app bundle.
    /// </summary>
    public static string? MacOSAppBundlePath => MacOSAppBundlePathLazy.Value;

    /// <summary>
    /// Checks if the application is running under a macOS app bundle.
    /// </summary>
    [MemberNotNullWhen(true, nameof(MacOSAppBundlePath), nameof(ExecutablePath), nameof(ExecutableName), nameof(BaseDirectory))]
    public static bool IsMacOSAppBundle => !string.IsNullOrWhiteSpace(MacOSAppBundlePath);

    /// <summary>
    /// Gets the base directory of the entry executable of the running application.<br/>
    /// This is the directory where the entry executable is located.
    /// </summary>
    public static string? BaseDirectory => ExecutableInfoLazy.Value.BaseDirectory;

    /// <summary>
    /// Gets the full path to the entry executable of the running application.
    /// </summary>
    /// <remarks>Note the executable is from entry point and not the app executable itself.<br/>
    /// It's expected to be different from <see cref="Environment.ProcessPath"/> in some cases.<br/>
    /// Example: The MyApp.AppImage, MyApp.app will be returned instead of the app executable.<br/>
    /// If running from dotnet, it will return the AssemblyLocation, eg: myapp.dll.</remarks>
    public static string? ExecutablePath => ExecutableInfoLazy.Value.ExecutablePath;

    /// <summary>
    /// Gets the file name of the currently running executable, including the extension but without the path.
    /// </summary>
    public static string? ExecutableName => ExecutableInfoLazy.Value.ExecutableName;

    /// <summary>
    /// Gets a value indicating whether the path to the current executable is known.
    /// </summary>
    /// <remarks>When this property is <see langword="true"/>, the <see cref="ExecutablePath"/>, <see
    /// cref="ExecutableName"/>, and <see cref="BaseDirectory"/> properties are guaranteed to be non-null. If <see
    /// langword="false"/>, these properties may not be available.</remarks>
    [MemberNotNullWhen(true, nameof(ExecutablePath), nameof(ExecutableName), nameof(BaseDirectory))]
    public static bool IsExecutablePathKnown => ExecutableInfoLazy.Value.IsExecutablePathKnown;

    /// <summary>
    /// Gets a formatted string containing the names and versions of all assemblies currently loaded in the application domain.
    /// </summary>
    /// <remarks>The assemblies are listed in the order they are loaded into the current application domain.
    /// The index is zero-padded based on the total number of assemblies to ensure consistent alignment.</remarks>
    public static string FormattedLoadedAssemblies
    {
        get
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembliesLengthPad = assemblies.Length.ToString().Length;
            var sb = new StringBuilder(assemblies.Length * 48);
            for (var i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i].GetName();
                sb.AppendLine(string.Format($"{{0:D{assembliesLengthPad}}}: {{1}}, Version={{2}}", i + 1, assembly.Name, assembly.Version));
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Gets a formatted string containing key application information, such as version, environment, and other
    /// metadata.
    /// </summary>
    /// <remarks>The returned string includes multiple lines, each representing a key-value pair of
    /// application details. This information can be useful for diagnostics, logging, or display purposes.</remarks>
    public static string ApplicationInfo
    {
        get
        {
            var info = GetApplicationInfoDict();
            var sb = new StringBuilder(info.Count * 50);

            foreach (var kvp in info)
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value?.ReplaceLineEndings("\\n")}");
            }

            return sb.ToString();
        }
    }


    #endregion

    #region Methods
    /// <summary>
    /// Returns a dictionary of the entry application information.
    /// </summary>
    /// <returns>Dictionary containing application information key-value pairs.</returns>
    public static Dictionary<string, string?> GetApplicationInfoDict()
    {
        var info = new Dictionary<string, string?>(32);

        // Assembly information
        info[nameof(AssemblyTargetFramework)] = AssemblyTargetFramework is null
            ? null
            : $"{AssemblyTargetFramework.FrameworkDisplayName}  ({AssemblyTargetFramework.FrameworkName})";
        info[nameof(AssemblyConfiguration)] = AssemblyConfiguration;
        info[nameof(AssemblyTitle)] = AssemblyTitle;
        info[nameof(AssemblyProduct)] = AssemblyProduct;
        info[nameof(AssemblyName)] = AssemblyName;
        info[nameof(AssemblyDescription)] = AssemblyDescription;
        info[nameof(AssemblyCopyright)] = AssemblyCopyright;
        info[nameof(AssemblyCompany)] = AssemblyCompany;
        info[nameof(AssemblyTrademark)] = AssemblyTrademark;
        info[nameof(AssemblyAuthors)] = AssemblyAuthors;
        info[nameof(AssemblyRepositoryUrl)] = AssemblyRepositoryUrl;
        info[nameof(AssemblyLocation)] = AssemblyLocation;
        info[nameof(AssemblyVersion)] = AssemblyVersion?.ToString();
        info[nameof(AssemblyVersionString)] = AssemblyVersionString;
        info[nameof(AssemblyFileVersion)] = AssemblyFileVersion;
        info[nameof(AssemblyInformationalVersion)] = AssemblyInformationalVersion;

        // Process information
        info[nameof(ProcessId)] = ProcessId.ToString();
        info[nameof(ProcessName)] = ProcessName;

        // Bundle type
        info[nameof(BundleType)] = BundleType.ToString();
        info[nameof(IsAppBundled)] = IsAppBundled.ToString();
        info[nameof(IsSingleFileApp)] = IsSingleFileApp.ToString();

        // DotNet specific
        info[nameof(IsRunningFromDotNetProcess)] = IsRunningFromDotNetProcess.ToString();
        info[nameof(IsDotNetSingleFileApp)] = IsDotNetSingleFileApp.ToString();
        if (IsDotNetSingleFileApp) info[nameof(DotNetSingleFileAppPath)] = DotNetSingleFileAppPath;

        // Linux specific
        if (OperatingSystem.IsLinux())
        {
            info[nameof(IsLinuxAppImage)] = IsLinuxAppImage.ToString();
            if (IsLinuxAppImage) info[nameof(LinuxAppImagePath)] = LinuxAppImagePath;

            info[nameof(IsLinuxFlatpak)] = IsLinuxFlatpak.ToString();
            if (IsLinuxFlatpak)
            {
                info[nameof(LinuxFlatpakId)] = LinuxFlatpakId;
                info[nameof(LinuxFlatpakPath)] = LinuxFlatpakPath;
            }
        }

        // MacOS specific
        if (OperatingSystem.IsMacOS())
        {
            info[nameof(IsMacOSAppBundle)] = IsMacOSAppBundle.ToString();
            if (IsMacOSAppBundle) info[nameof(MacOSAppBundlePath)] = MacOSAppBundlePath;
        }

        // Paths
        info[nameof(BaseDirectory)] = BaseDirectory;
        info[nameof(ExecutablePath)] = ExecutablePath;
        info[nameof(ExecutableName)] = ExecutableName;
        info[nameof(IsExecutablePathKnown)] = IsExecutablePathKnown.ToString();

        return info;
    }

    /// <summary>
    /// Launches a new instance of the application with the given arguments.
    /// </summary>
    /// <param name="runArguments">Arguments to pass within the application</param>
    /// <returns>True if able to launch a new instance, otherwise false.</returns>
    public static bool LaunchNewInstance(string? runArguments = null)
    {
        if (!IsExecutablePathKnown) return false;

        try
        {
            if (OperatingSystem.IsMacOS() && IsMacOSAppBundle)
            {
                if (Directory.Exists(ExecutablePath))
                {
                    return Utilities.StartProcess("open",
                        $"\"{ExecutablePath}\"{(runArguments is null ? string.Empty : $" --args {runArguments}")}") == 0;
                }
            }

            if (!File.Exists(ExecutablePath)) return false;

            int exitCode;
            if (IsRunningFromDotNetProcess)
            {
                exitCode = Utilities.StartProcess(Environment.ProcessPath ?? ProcessName, $"\"{ExecutablePath}\" {runArguments}");
            }
            else
            {
                exitCode = Utilities.StartProcess(ExecutablePath, runArguments);
            }

            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}