using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Solutions;

namespace Updatum.Fallout;

/// <summary>
/// Provides the Updatum build pipeline: solution and project discovery, software metadata resolved from
/// MSBuild properties, and the restore/compile/run/publish targets together with their platform bundles.
/// </summary>
public partial class UpdatumBuild : FalloutBuild
{
    private static readonly string[] MainProjectPropertyNames =
    [
        "ArtifactsPath", "SolutionName", "ProductName", "Company", "CompanyRDNS", "Authors", "Summary",
        "Description", "Version", "Copyright", "PackageLicenseExpression", "RepositoryUrl", "PackageTags",
        nameof(BuildRuntimeCacheFileName)
    ];

    private readonly Dictionary<string, IReadOnlyDictionary<string, string?>> _evaluatedProjectProperties =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _loggedProjectEvaluationFallback;

    /// <summary>
    /// The configuration parameter
    /// </summary>
    [Parameter("Configuration to build - Default is 'Release'")]
    public BuildConfiguration Configuration { get; protected set; } = BuildConfiguration.Release;

    /// <summary>
    /// Gets a value indicating whether every architecture of a platform is combined into one bundle, so the
    /// resulting application selects the matching architecture at run time.
    /// </summary>
    [Parameter(
        "When publish set this variable(true) to bundle all arch in a single bundle, eg: x64 and arm64 together, app will then run the required arch. Default: False")]
    public bool PublishMultiArch { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether bundle creation (zip, apps, installers) is skipped after publishing.
    /// </summary>
    [Parameter(
        "When publish set this variable(true) to not create the bundles (zip, apps, installers). Default: False")]
    public bool PublishNoBundles { get; protected set; }

    /// <summary>
    /// Gets a value indicating whether the raw compilation folders are removed once the bundles are created.
    /// </summary>
    [Parameter(
        "When publish set this variable(true) to keep only the bundles (zip, apps, installers), compilation folders will be removed. Default: False")]
    public bool PublishDiscardNonBundles { get; protected set; }

    /// <summary>
    /// Gets the runtime identifiers to publish.
    /// </summary>
    [Parameter(
        "RIDs to publish separated by space, Default: 'win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64'")]
    public string[] RIds { get; protected set; } =
    [
        "win-x64", "win-arm64",
        "osx-x64", "osx-arm64",
        "linux-x64", "linux-arm64"
    ];

    /// <summary>
    /// Gets the solution discovered from the build root, preferring <c>.slnx</c> over <c>.sln</c>.
    /// </summary>
    public virtual Solution Solution => field ??= LoadSolution(FindSolutionFile(SolutionSearchDirectory));

    /// <summary>
    /// Gets the directory searched for a solution file.
    /// </summary>
    protected virtual AbsolutePath SolutionSearchDirectory => RootDirectory;

    /// <summary>
    /// Gets the list of invalid project names.
    /// </summary>
    public List<string> ExcludedProjectNameTokens { get; } =
    [
        "test",
        "tests",
        "xunit",
        "unit",
        "unittest",
        "unittests",
        "integration",
        "integrationtests",
        "e2e",
        "functional",
        "acceptance",

        "benchmark",
        "benchmarks",
        "perf",
        "performance",

        "build",
        "tools",
        "tooling",
        "scripts",

        "sample",
        "samples",
        "example",
        "examples",
        "demo",
        "demos",

        "mock",
        "mocks",
        "stub",
        "stubs",
        "fake",
        "fakeapp",
        "fakes",

        "docs",
        "documentation"
    ];

    /// <summary>
    /// Gets the main project of the solution, excluding projects with invalid names, see: <see cref="ExcludedProjectNameTokens"/>
    /// </summary>
    /// <remarks>
    /// The <em>last</em> runnable, non-excluded project in solution order wins, so the result depends on how the
    /// solution file orders its projects. Override this property when the solution holds more than one candidate.
    /// </remarks>
    /// <exception cref="Exception">Thrown when no valid runnable project is found in the solution that is not excluded by name.</exception>
    [field: AllowNull]
    [field: MaybeNull]
    public virtual Project MainProject
    {
        get
        {
            var projects = Solution.Projects;
            return field ??= projects.LastOrDefault(p =>
                                 !IsExcludedByName(p) &&
                                 IsRunnableProject(p))
                             ?? throw new InvalidOperationException(
                                 "No valid runnable project found in the solution that is not excluded by name. Please define MainProject directly.");
        }
    }

    /// <summary>
    /// Gets the artifacts directory, which is the output directory for build artifacts.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public AbsolutePath ArtifactsDirectory
    {
        get
        {
            field ??= GetMainProjectProperty("ArtifactsPath");
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the publish directory, which is the output directory for published artifacts.
    /// </summary>
    public virtual AbsolutePath PublishDirectory => ArtifactsDirectory / "publish";

    /// <summary>
    /// Gets the directory holding the packaging media (application icons) used by the bundle creators.
    /// </summary>
    public virtual AbsolutePath MediaDirectory => RootDirectory / "media";

    /// <summary>
    /// Gets the changelog file path, which is the path to the changelog file in the root directory.
    /// </summary>
    public virtual AbsolutePath ChangelogFile => RootDirectory / "CHANGELOG.md";

    /// <summary>
    /// Gets the release notes file path, which is the path to the release notes file in the root directory.
    /// </summary>
    public virtual AbsolutePath ReleaseNotesFile => RootDirectory / "RELEASE_NOTES.md";

    /// <summary>
    /// Gets the solution name, which is retrieved from the main project's properties or the solution's name.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SolutionName
    {
        get
        {
            if (field is null)
            {
                field = GetMainProjectProperty("SolutionName");
                if (string.IsNullOrWhiteSpace(field))
                {
                    field = Solution.Name ?? GetMainProjectProperty("ProductName");
                }
            }

            ThrowIfMissingProperty(field);

            return field;
        }
    }

    /// <summary>
    /// Gets the software name, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public virtual string SoftwareName
    {
        get
        {
            field ??= SolutionName;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software company name, which is retrieved from the main project's properties.
    /// </summary>
    /// <example>MyCompany</example>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareCompany
    {
        get
        {
            field ??= GetMainProjectProperty("Company")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software company RDNS, which is retrieved from the main project's properties.
    /// </summary>
    /// <example>com.example</example>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareCompanyRdns
    {
        get
        {
            field ??= GetMainProjectProperty("CompanyRDNS")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software RDNS, which is a combination of the company RDNS and software name.
    /// </summary>
    /// <example>com.example.MySoftware</example>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareRDNS => field ??= $"{SoftwareCompanyRdns}.{SoftwareName}";

    /// <summary>
    /// Gets the software authors, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareAuthors
    {
        get
        {
            field ??= GetMainProjectProperty("Authors")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software summary, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareSummary
    {
        get
        {
            var summary = field ?? GetMainProjectProperty("Summary");
            field = string.IsNullOrWhiteSpace(summary) ? SoftwareDescription : summary;

            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software description, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareDescription
    {
        get
        {
            field ??= GetMainProjectProperty("Description")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software version, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public virtual string SoftwareVersion
    {
        get
        {
            if (field is null)
            {
                var version = GetMainProjectProperty("Version");
                if (version is not null && version.EndsWith("-dev", StringComparison.Ordinal))
                    version = version[..^4];

                field = version!;
            }

            ThrowIfMissingProperty(field);

            return field;
        }
    }

    /// <summary>
    /// Gets the software copyright information, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareCopyright
    {
        get
        {
            field ??= GetMainProjectProperty("Copyright")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software license information, which is retrieved from the main project's properties.
    /// </summary>
    /// <example>MIT</example>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareLicense
    {
        get
        {
            field ??= GetMainProjectProperty("PackageLicenseExpression")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software repository URL, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwareRepositoryUrl
    {
        get
        {
            field ??= GetMainProjectProperty("RepositoryUrl")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software package tags, which are retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string SoftwarePackageTags
    {
        get
        {
            field ??= GetMainProjectProperty("PackageTags")!;
            ThrowIfMissingProperty(field);
            return field;
        }
    }

    /// <summary>
    /// Gets the software package tags as a list of individual tags.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public string[] SoftwarePackageTagsList =>
        field ??= SoftwarePackageTags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToArray();

    /// <summary>
    /// Gets the build runtime cache file name, which is retrieved from the main project's properties.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public virtual string BuildRuntimeCacheFileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                var fileName = GetBuildRuntimeCacheFileName();
                if (!string.IsNullOrWhiteSpace(fileName))
                    field = fileName;
            }

            return field ??= "build-runtime.json";
        }
    }

    /// <summary>
    /// Gets the root directory used for temporary publish staging payloads.
    /// </summary>
    protected virtual AbsolutePath PublishStagingDirectory => TemporaryDirectory / "publish-staging";

    /// <summary>
    /// Gets or sets the options used to create macOS application bundles.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public MacAppBundleOptions MacAppBundleOptions
    {
        get => field ??= CreateMacAppBundleOptions();
        set;
    }

    /// <summary>
    /// Creates the default macOS application bundle options.
    /// </summary>
    /// <remarks>
    /// Resolved lazily on first use so derived builds can customize the options without forcing MSBuild
    /// project evaluation from their constructor.
    /// </remarks>
    /// <returns>The default macOS application bundle options.</returns>
    protected virtual MacAppBundleOptions CreateMacAppBundleOptions()
    {
        return new MacAppBundleOptions(this);
    }

    /// <summary>
    /// Gets or sets the options used to create Linux application bundles.
    /// </summary>
    [field: AllowNull]
    [field: MaybeNull]
    public LinuxAppBundleOptions LinuxAppBundleOptions
    {
        get => field ??= CreateLinuxAppBundleOptions();
        set;
    }

    /// <summary>
    /// Creates the default Linux application bundle options.
    /// </summary>
    /// <remarks>
    /// Resolved lazily on first use so derived builds can customize the options without forcing MSBuild
    /// project evaluation from their constructor.
    /// </remarks>
    /// <returns>The default Linux application bundle options.</returns>
    protected virtual LinuxAppBundleOptions CreateLinuxAppBundleOptions()
    {
        return new LinuxAppBundleOptions(this);
    }

    /// <summary>
    /// Gets the list of targets that this build depends on. These targets will be executed before the current build target.
    /// </summary>
    protected Target[] DependOnTargets { get; set; } = [];


    /// <summary>
    /// Gets the public build variables written by the <see cref="Print"/> target.
    /// </summary>
    /// <returns>A name/value map ordered by variable name.</returns>
    protected virtual IReadOnlyDictionary<string, string> GetPrintVariables()
    {
        var variables = new SortedDictionary<string, string>(StringComparer.Ordinal);
        AddPrintVariable(variables, nameof(RootDirectory), () => RootDirectory);
        AddPrintVariable(variables, nameof(TemporaryDirectory), () => TemporaryDirectory);
        AddPrintVariable(variables, nameof(BuildAssemblyDirectory), () => BuildAssemblyDirectory);
        AddPrintVariable(variables, nameof(BuildAssemblyFile), () => BuildAssemblyFile);
        AddPrintVariable(variables, nameof(BuildProjectDirectory), () => BuildProjectDirectory);
        AddPrintVariable(variables, nameof(BuildProjectFile), () => BuildProjectFile);
        AddPrintVariable(variables, "SolutionDirectory", () => Solution.Directory);

        var properties = GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead &&
                               property.GetIndexParameters().Length == 0 &&
                               property.DeclaringType is not null &&
                               typeof(UpdatumBuild).IsAssignableFrom(property.DeclaringType) &&
                               !typeof(Target).IsAssignableFrom(property.PropertyType));

        foreach (var property in properties)
        {
            try
            {
                variables[property.Name] = FormatPrintValue(property.GetValue(this));
            }
            catch (Exception exception)
            {
                var failure = (exception as TargetInvocationException)?.InnerException ?? exception;
                variables[property.Name] = $"<error: {failure.Message}>";
            }
        }

        variables["IsLinux"] = EnvironmentInfo.IsLinux.ToString();
        variables["IsOsx"] = EnvironmentInfo.IsOsx.ToString();
        variables["IsWin"] = EnvironmentInfo.IsWin.ToString();
        variables["IsWsl"] = EnvironmentInfo.IsWsl.ToString();

        return variables;
    }

    private static void AddPrintVariable(IDictionary<string, string> variables, string name,
        Func<object?> getValue)
    {
        try
        {
            variables[name] = FormatPrintValue(getValue());
        }
        catch (Exception exception)
        {
            variables[name] = $"<error: {exception.Message}>";
        }
    }

    private static string FormatPrintValue(object? value)
    {
        if (value is null)
            return "<null>";

        if (value is string text)
            return text;

        if (value is global::Updatum.Fallout.MacAppBundleOptions or
            global::Updatum.Fallout.LinuxAppBundleOptions)
            return JsonSerializer.Serialize(value, value.GetType());

        if (value is Solution solution)
            return solution.Path.ToString();

        if (value is Delegate callback)
            return $"{callback.Method.DeclaringType?.FullName}.{callback.Method.Name}";

        if (value is IEnumerable collection)
            return $"[{string.Join(", ", collection.Cast<object?>().Select(FormatPrintValue))}]";

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Executes the build script. This is the entry point for the build process.
    /// </summary>
    /// <returns>The exit code of the build process.</returns>
    public static int Main()
    {
        return Execute<UpdatumBuild>(x => x.Compile);
    }
}