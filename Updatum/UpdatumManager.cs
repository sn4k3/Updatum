using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Octokit;
using Updatum.Extensions;

namespace Updatum;

/// <summary>
/// Represents the Updatum class.
/// </summary>
public partial class UpdatumManager : INotifyPropertyChanged, IDisposable
{
    #region Events
    /// <summary>
    ///     Multicast event for property change notifications.
    /// </summary>
    private PropertyChangedEventHandler? _propertyChanged;

    /// <summary>
    /// /// Occurs when a property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add
        {
            _propertyChanged -= value;
            _propertyChanged += value;
        }
        remove => _propertyChanged -= value;
    }

    /// <summary>
    /// Occurs when the check for update is completed.
    /// </summary>
    public event EventHandler? CheckForUpdateCompleted;

    /// <summary>
    /// Occurs when an update is found.
    /// </summary>
    public event EventHandler? UpdateFound;

    /// <summary>
    /// Occurs when the download is completed.
    /// </summary>
    public event EventHandler<UpdatumDownloadedAsset>? DownloadCompleted;

    /// <summary>
    /// Occurs when the second stage of auto install is completed.<br/>
    /// This is after unpack and script creation but before actual install/replace and the killing of the current process.<br/>
    /// This event is useful to perform any custom action before the process is killed, like saving the current state or settings.
    /// </summary>
    public event EventHandler<UpdatumDownloadedAsset>? InstallUpdateCompleted;

    #endregion

    #region Constants

    /// <summary>
    /// The URL of the GitHub homepage.
    /// </summary>
    private const string GitHubUrl = "https://github.com";

    /// <summary>
    /// Token to prevent app from rerun after upgrade.
    /// </summary>
    public const string NoRunAfterUpgradeToken = "$NORUN!";

    /// <summary>
    /// Default buffer size for the download stream.
    /// </summary>
    private const int DefaultBufferSize = 8192;

    /// <summary>
    /// Default file extension for Linux AppImage files.
    /// </summary>
    private const string LinuxAppImageFileExtension = ".AppImage";

    /// <summary>
    /// Default file extension for Linux flatpak files.
    /// </summary>
    private const string LinuxFlatpakFileExtension = ".flatpak";

    /// <summary>
    /// Default file extension for Windows installers.
    /// </summary>
    private static readonly string[] WindowsInstallerFileExtensions = [".msi", ".exe"];

    #endregion

    #region Static Properties

    /// <summary>
    /// Gets the current version of this library (<see cref="UpdatumManager"/>).
    /// </summary>
    public static Version LibraryVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private static Version EntryAssemblyVersion => EntryApplication.AssemblyVersion ?? new Version(0, 0, 0, 0);

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+){0,2}(?:[-_](?:dev|alpha|beta|preview|rc|nightly|canary)\d*)?", RegexOptions.IgnoreCase)]
    private static partial Regex ExtractVersionRegex();

    /// <summary>
    /// The default GitHub API options, defaults to save tokens and maximize output up to 30 releases.
    /// </summary>
    /// <remarks>You can use up to 100 releases in PageSize.</remarks>
    public static readonly ApiOptions GitHubApiOptions = new()
    {
        PageCount = 1,
        PageSize = 30,
    };

    /// <summary>
    /// Gets the HTTP client used to access the GitHub API.
    /// </summary>
    public static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            UserAgent =
            {
                new ProductInfoHeaderValue(EntryApplication.AssemblyName ?? nameof(UpdatumManager), EntryAssemblyVersion.ToString())
            }
        }
    };
    #endregion

    #region Members
    private bool _disposed;
    private System.Timers.Timer? _autoUpdateCheckTimer;
    private bool _fetchOnlyLatestRelease;
    private DateTime _lastCheckDateTime = DateTime.MinValue;
    private IReadOnlyList<Release> _releases = [];
    private IReadOnlyList<Release> _releasesAhead = [];
    private string _assetRegexPattern = EntryApplication.GenericRuntimeIdentifier;
    private string? _assetExtensionFilter;
    private int _checkForUpdateCount;
    private double _downloadProgressUpdateFrequencySeconds = 0.1; // 10 FPS
    private long _downloadSizeBytes = -1;
    private long _downloadedBytes;
    private UpdatumWindowsExeType _installUpdateWindowsExeType;
    private string? _installUpdateWindowsInstallerArguments;
    private UpdatumSingleFileExecutableNameStrategy _installUpdateSingleFileExecutableNameStrategy;
    private string? _installUpdateSingleFileExecutableName;
    private string? _installUpdateInjectCustomScript;
    private UpdatumState _state;
    private bool _installUpdateCodesignMacOSApp;

    #endregion

    #region Properties
    /// <summary>
    /// Gets the GitHub client used to access the GitHub API.
    /// </summary>
    public GitHubClient GithubClient { get; } = new(new Octokit.ProductHeaderValue(EntryApplication.AssemblyName ?? nameof(UpdatumManager), EntryAssemblyVersion.ToString()));

    /// <summary>
    /// Gets the auto updater timer. Use this to start or stop the timer for your timed auto checks.
    /// </summary>
    public System.Timers.Timer AutoUpdateCheckTimer
    {
        get
        {
            if (_autoUpdateCheckTimer is null)
            {
                _autoUpdateCheckTimer = new(TimeSpan.FromHours(12))
                {
                    AutoReset = true,
                };

                _autoUpdateCheckTimer.Elapsed += AutoUpdateCheckTimerOnElapsed;
            }

            return _autoUpdateCheckTimer;
        }
    }

    /// <summary>
    /// Gets or sets the current version of the application.
    /// </summary>
    public Version CurrentVersion { get; init; } = EntryAssemblyVersion;

    /// <summary>
    /// Gets the owner of the repository.
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// Gets the name of the repository.
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// Gets the full GitHub repository URL.
    /// </summary>
    public string RepositoryUrl => $"{GitHubUrl}/{Owner}/{Repository}";

    /// <summary>
    /// Gets or sets whatever to fetch only the latest release or all releases.
    /// Note that fetching all releases can waste more tokens and memory.
    /// </summary>
    /// <remarks>By default, it will only fetch 100 releases and 1 page to spare tokens, can be configurable via <see cref="GitHubApiOptions"/></remarks>
    public bool FetchOnlyLatestRelease
    {
        get => _fetchOnlyLatestRelease;
        set => RaiseAndSetIfChanged(ref _fetchOnlyLatestRelease, value);
    }

    /// <summary>
    /// Gets the last time the repository was checked for updates.
    /// </summary>
    public DateTime LastCheckDateTime
    {
        get => _lastCheckDateTime;
        private set => RaiseAndSetIfChanged(ref _lastCheckDateTime, value);
    }

    /// <summary>
    /// Gets the list of all releases (unfiltered) for the repository.
    /// </summary>
    /// <remarks>Returns empty when never checked for update.</remarks>
    public IReadOnlyList<Release> Releases
    {
        get => _releases;
        private set => RaiseAndSetIfChanged(ref _releases, value);
    }

    /// <summary>
    /// Gets the releases ahead of the current version.
    /// </summary>
    /// <remarks>Returns empty when never checked for update.</remarks>
    public IReadOnlyList<Release> ReleasesAhead
    {
        get => _releasesAhead;
        private set
        {
            if (!RaiseAndSetIfChanged(ref _releasesAhead, value)) return;
            RaisePropertyChanged(nameof(ReleasesAheadCount));
            RaisePropertyChanged(nameof(LatestRelease));
            RaisePropertyChanged(nameof(LatestReleaseTagVersionStr));
            RaisePropertyChanged(nameof(IsUpdateAvailable));
        }
    }

    /// <summary>
    /// Gets the number of releases ahead of the current version.
    /// </summary>
    public int ReleasesAheadCount => ReleasesAhead.Count;

    /// <summary>
    /// Gets if there are any updates available.
    /// </summary>
    [MemberNotNullWhen(true, nameof(LatestRelease), nameof(LatestReleaseTagVersionStr))]
    public bool IsUpdateAvailable => ReleasesAheadCount > 0;


    /// <summary>
    /// Gets the latest release for the repository.
    /// </summary>
    public Release? LatestRelease => ReleasesAhead.Count > 0 ? ReleasesAhead[0] : null;

    /// <summary>
    /// Gets the latest release tag version.
    /// </summary>
    public string? LatestReleaseTagVersionStr => LatestRelease?.GetTagVersionStr();

    /// <summary>
    /// Gets the <see cref="Regex"/> object used to match the asset name.
    /// </summary>
    public Regex? AssetRegex { get; private set; }

    /// <summary>
    /// Gets or sets the regex pattern to match with the asset name.
    /// </summary>
    /// <remarks>Default to <see cref="EntryApplication.GenericRuntimeIdentifier"/>.</remarks>
    public string AssetRegexPattern
    {
        get => _assetRegexPattern;
        set
        {
            if (!RaiseAndSetIfChanged(ref _assetRegexPattern, value)) return;
            AssetRegex = string.IsNullOrWhiteSpace(value)
                ? null
                : new Regex(value, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>
    /// Gets or sets the asset required extension in case when there are multiple assets for the same platform.
    /// </summary>
    /// <remarks>Use this option when you have multiple assets for same platform, ie: Windows in MSI and ZIP.</remarks>
    public string? AssetExtensionFilter
    {
        get => _assetExtensionFilter;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                value = value.Trim();
                if (value[0] != '.') value = $".{value}";
            }

            RaiseAndSetIfChanged(ref _assetExtensionFilter, value);
        }
    }


    /// <summary>
    /// Gets the number of times the updater checked for updates.
    /// </summary>
    public int CheckForUpdateCount
    {
        get => _checkForUpdateCount;
        private set => RaiseAndSetIfChanged(ref _checkForUpdateCount, value);
    }


    /// <summary>
    /// Gets or sets the interval in seconds to update the download progress statistics.
    /// </summary>
    /// <remarks>A value of 0 will always report the progress on each read chunk.</remarks>
    public double DownloadProgressUpdateFrequencySeconds
    {
        get => _downloadProgressUpdateFrequencySeconds;
        set => RaiseAndSetIfChanged(ref _downloadProgressUpdateFrequencySeconds, value);
    }

    /// <summary>
    /// Gets the total size of the download in bytes.
    /// </summary>
    public long DownloadSizeBytes
    {
        get => _downloadSizeBytes;
        private set
        {
            if (!RaiseAndSetIfChanged(ref _downloadSizeBytes, value)) return;
            RaisePropertyChanged(nameof(DownloadSizeMegabytes));
            RaisePropertyChanged(nameof(DownloadedPercentage));
        }
    }

    /// <summary>
    /// Gets the current downloaded size in bytes.
    /// </summary>
    public long DownloadedBytes
    {
        get => _downloadedBytes;
        private set
        {
            if (!RaiseAndSetIfChanged(ref _downloadedBytes, value)) return;
            RaisePropertyChanged(nameof(DownloadedMegabytes));
            RaisePropertyChanged(nameof(DownloadedPercentage));
        }
    }

    /// <summary>
    /// Gets the total size of the download in megabytes.
    /// </summary>
    public double DownloadSizeMegabytes => DownloadSizeBytes > 0 ? Math.Round(DownloadSizeBytes / 1024.0 / 1024.0, 2, MidpointRounding.AwayFromZero) : double.NaN;

    /// <summary>
    /// Gets the current downloaded size in megabytes.
    /// </summary>
    public double DownloadedMegabytes => DownloadedBytes > 0 ? Math.Round(DownloadedBytes / 1024.0 / 1024.0, 2, MidpointRounding.AwayFromZero) : 0.0;

    /// <summary>
    /// Gets the current downloaded percentage of the progress from 0% to 100%.
    /// </summary>
    public double DownloadedPercentage => DownloadSizeBytes > 0 ? Math.Round(DownloadedBytes / (double)DownloadSizeBytes * 100.0, 2, MidpointRounding.AwayFromZero) : 0.0;

    /// <summary>
    /// Gets or sets the type of Windows executable used to install updates.<br/>
    /// Use <see cref="UpdatumWindowsExeType.Auto"/> to let the updater infer the type based on the asset file signature.<br/>
    /// Use <see cref="UpdatumWindowsExeType.Installer"/> for installer packages (.exe installers).<br/>
    /// Use <see cref="UpdatumWindowsExeType.SingleFileApp"/> for single-file executables (.exe).<br/>
    /// </summary>
    /// <remarks>Executable files (.exe) can either be installers or single-file apps, the recommendations are:<br/>
    /// - If you have the two types on your assets leave this to <see cref="UpdatumWindowsExeType.Auto"/>,
    /// this can lead to false positives if your app have raw strings that share installer signatures, eg. 'Inno Setup'.<br/>
    /// - If you build and only have single-file app or installer, configure accordingly to prevent false positives.</remarks>
    public UpdatumWindowsExeType InstallUpdateWindowsExeType
    {
        get => _installUpdateWindowsExeType;
        set => RaiseAndSetIfChanged(ref _installUpdateWindowsExeType, value);
    }

    /// <summary>
    /// Gets or sets the arguments to pass to the installer when using the auto installer under Windows.
    /// </summary>
    /// <remarks>For msi, exe. Can be used for a silent installation.</remarks>
    /// <example>/qb = Basic MSI installation.</example>
    public string? InstallUpdateWindowsInstallerArguments
    {
        get => _installUpdateWindowsInstallerArguments;
        set => RaiseAndSetIfChanged(ref _installUpdateWindowsInstallerArguments, value);
    }

    /// <summary>
    /// Gets or sets the strategy used to determine the executable file name when installing an update for single-file applications.
    /// </summary>
    public UpdatumSingleFileExecutableNameStrategy InstallUpdateSingleFileExecutableNameStrategy
    {
        get => _installUpdateSingleFileExecutableNameStrategy;
        set => RaiseAndSetIfChanged(ref _installUpdateSingleFileExecutableNameStrategy, value);
    }

    /// <summary>
    /// <p>Gets or sets the fallback name of the single file executable or directory to use for the auto updater when unable to infer from current running file.</p>
    /// <p>Use {0} token to be replaced with the downloaded tag version.</p>
    /// <p>A null or empty value will use the downloaded file name instead as fallback.</p>
    /// </summary>
    /// <example>MyAppName_v{0}</example>
    public string? InstallUpdateSingleFileExecutableName
    {
        get => _installUpdateSingleFileExecutableName;
        set => RaiseAndSetIfChanged(ref _installUpdateSingleFileExecutableName, value);
    }

    /// <summary>
    /// Gets or sets if the auto updater should locally codesign the macOS applications.
    /// </summary>
    public bool InstallUpdateCodesignMacOSApp
    {
        get => _installUpdateCodesignMacOSApp;
        set => RaiseAndSetIfChanged(ref _installUpdateCodesignMacOSApp, value);
    }

    /// <summary>
    /// Gets or sets a custom script to inject into the auto updater script.
    /// Will be injected before run the upgraded application.
    /// </summary>
    public string? InstallUpdateInjectCustomScript
    {
        get => _installUpdateInjectCustomScript;
        set => RaiseAndSetIfChanged(ref _installUpdateInjectCustomScript, value);
    }

    /// <summary>
    /// Gets the current state of the updater.
    /// </summary>
    public UpdatumState State
    {
        get => _state;
        private set
        {
            if (!RaiseAndSetIfChanged(ref _state, value)) return;
            RaisePropertyChanged(nameof(IsBusy));
        }
    }

    /// <summary>
    /// Gets if the updater is busy doing any check or operation.
    /// </summary>
    public bool IsBusy => State != UpdatumState.None;
    #endregion

    #region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatumManager"/> class and try to infer the <see cref="Owner"/> and <see cref="Repository"/> from RepositoryUrl from the assembly metadata.
    /// </summary>
    /// <remarks>Warning: Only use this constructor when the RepositoryUrl is well-defined on your entry assembly, or it will throw exceptions.</remarks>
    /// <exception cref="InvalidOperationException">When unable to infer from the RepositoryUrl.</exception>
    public UpdatumManager()
    {
        if (!string.IsNullOrWhiteSpace(_assetRegexPattern))
        {
            AssetRegex = new Regex(_assetRegexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatumManager"/> class with the specified parameters.
    /// </summary>
    /// <param name="repositoryUrl">The full GitHub repository url, must starts with: https://github.com/, if null or empty it will try to infer from the assembly RepositoryUrl metadata.</param>
    /// <param name="currentVersion">Your app version that is current running, if <c>null</c>, it will fetch the version from EntryAssembly.</param>
    /// <param name="gitHubCredentials">Pass the GitHub credentials if required, for extra tokens or visibility.</param>
    /// <exception cref="ArgumentException">When unable to infer from the <paramref name="repositoryUrl"/>.</exception>
    [SetsRequiredMembers]
    public UpdatumManager(string? repositoryUrl, Version? currentVersion = null, Credentials? gitHubCredentials = null) : this()
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl)) repositoryUrl = EntryApplication.AssemblyRepositoryUrl;

        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            throw new ArgumentNullException(nameof(repositoryUrl), "Unable to infer from the RepositoryUrl, maybe missing from assembly metadata.");
        }

        if (!repositoryUrl.StartsWith(GitHubUrl, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unable to infer from the url, expecting to start with: <{GitHubUrl}>, got <{repositoryUrl}>.", nameof(repositoryUrl));
        }

        var match = Regex.Match(repositoryUrl, @$"{Regex.Escape(GitHubUrl)}\/([a-zA-Z0-9-]+)\/([a-zA-Z\d\-_.]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            throw new ArgumentException("Unable to infer from the url, regex failed to acquire owner/repo.", nameof(repositoryUrl));
        }

        if (match.Groups.Count < 3)
        {
            throw new ArgumentException($"Unable to infer from the url, regex failed to acquire the groups, expecting >=3, got {match.Groups.Count}.", nameof(repositoryUrl));
        }

        if (currentVersion is not null) CurrentVersion = currentVersion;
        if (gitHubCredentials is not null) GithubClient.Credentials = gitHubCredentials;
        Owner = match.Groups[1].Value;
        Repository = match.Groups[2].Value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdatumManager"/> class with the specified parameters.
    /// </summary>
    /// <param name="owner">The repository owner</param>
    /// <param name="repository">The repository name</param>
    /// <param name="currentVersion">Your app version that is current running, if <c>null</c>, it will fetch the version from EntryAssembly.</param>
    /// <param name="gitHubCredentials">Pass the GitHub credentials if required, for extra tokens or visibility.</param>
    [SetsRequiredMembers]
    public UpdatumManager(string owner, string repository, Version? currentVersion = null, Credentials? gitHubCredentials = null) : this()
    {
        if (currentVersion is not null) CurrentVersion = currentVersion;
        if (gitHubCredentials is not null) GithubClient.Credentials = gitHubCredentials;
        Owner = owner;
        Repository = repository;
    }
    #endregion

    #region Timer
    /// <summary>
    /// Starts the auto update check timer.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    /// <exception cref="ApiException"/>
    private void AutoUpdateCheckTimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
    #endregion

    #region Methods
    /// <summary>
    /// Checks for updates in the repository.
    /// </summary>
    /// <returns><c>True</c> if update found relative to given <paramref name="baseVersion"/>, otherwise <c>false</c>.</returns>
    /// <remarks>Can be used to force trigger an update by pass an initial version.</remarks>
    /// <exception cref="Octokit.ApiException"/>
    /// <exception cref="System.Net.Http.HttpRequestException">No such host is known. (api.github.com:443)</exception>
    /// <exception cref="System.Net.Sockets.SocketException">No such host is known.</exception>
    public async Task<bool> CheckForUpdatesAsync(Version baseVersion)
    {
        if (IsBusy) return false;
        State = UpdatumState.CheckingForUpdate;
        LastCheckDateTime = DateTime.Now;
        CheckForUpdateCount++;

        // If the timer is enabled, stop it to avoid multiple calls to CheckForUpdatesAsync
        var timerState = _autoUpdateCheckTimer is not null && _autoUpdateCheckTimer.Enabled;
        if (timerState)
        {
            AutoUpdateCheckTimer.Stop();
        }

        try
        {
            if (FetchOnlyLatestRelease)
            {
                var release = await GithubClient.Repository.Release.GetLatest(Owner, Repository).ConfigureAwait(false);
                Releases = [release];
            }
            else
            {
                Releases = await GithubClient.Repository.Release.GetAll(Owner, Repository, GitHubApiOptions).ConfigureAwait(false);
            }

            var releasesAheadList = new List<Release>(Releases.Count);

            foreach (var release in Releases)
            {
                if (release.Draft // Skip draft releases
                    || release.PublishedAt is null // Skip not published releases
                    || release.Assets.Count == 0 // Skip releases without assets
                    || !char.IsAsciiDigit(release.TagName[^1])) // Skip tag names that don't end with a digit (eg: v1.0.0-alpha)
                    continue;

                var tagVersion = release.GetTagVersion();

                if (tagVersion is null) continue;
                if (tagVersion.CompareTo(baseVersion) <= 0)
                    break; // If the release version is less than or equal to the current version, break it.
                if (GetCompatibleReleaseAsset(release) is null) continue; // Skip releases without matching assets

                releasesAheadList.Add(release);
            }

            ReleasesAhead = releasesAheadList;
            State = UpdatumState.None;

            CheckForUpdateCompleted?.Invoke(this, EventArgs.Empty);
            if (IsUpdateAvailable) UpdateFound?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            State = UpdatumState.None;
            if (timerState)
            {
                AutoUpdateCheckTimer.Start();
            }
        }

        return IsUpdateAvailable;
    }

    /// <summary>
    /// Checks for updates in the repository.
    /// </summary>
    /// <returns><c>True</c> if update found relative to given <see cref="CurrentVersion"/>, otherwise <c>false</c>.</returns>
    /// <exception cref="Octokit.ApiException"/>
    /// <exception cref="System.Net.Http.HttpRequestException">No such host is known. (api.github.com:443)</exception>
    /// <exception cref="System.Net.Sockets.SocketException">No such host is known.</exception>
    public Task<bool> CheckForUpdatesAsync()
    {
        return CheckForUpdatesAsync(CurrentVersion);
    }

    /// <summary>
    /// Gets the formated changelog of the releases ahead of the current version.
    /// </summary>
    /// <param name="maxReleases">The maximum number of releases to return. Use a value &lt;= 0 to return all updates.</param>
    /// <param name="reverseDisplayOrder"><c>True</c> will reverse the output order, that is, the latest versions shows in end of the output instead of top.</param>
    /// <returns>Formated changelog, otherwise <c>null</c> if no update available.</returns>
    public string? GetChangelog(int maxReleases = -1, bool reverseDisplayOrder = false)
    {
        if (!IsUpdateAvailable) return null;

        var sb = new StringBuilder();

        var count = 0;
        var releaseDiffNumber = ReleasesAhead.Count;
        var list = reverseDisplayOrder ? ReleasesAhead.Reverse() : ReleasesAhead;

        foreach (var release in list)
        {
            count++;
            if (maxReleases > 0 && count > maxReleases) break;

            sb.AppendLine($"## {release.Name}");
            sb.AppendLine();
            sb.AppendLine($"> Release date: {release.PublishedAt}  ");
            sb.AppendLine($"> Release diff: {(reverseDisplayOrder ? count : releaseDiffNumber)}");
            sb.AppendLine();
            sb.AppendLine(release.Body);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            releaseDiffNumber--;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets the formated changelog of the releases ahead of the current version.
    /// </summary>
    /// <param name="reverseDisplayOrder"><c>True</c> will reverse the output order, that is, the latest versions shows in end of the output instead of top.</param>
    /// <param name="maxReleases">The maximum number of releases to return. Use a value &lt;= 0 to return all updates.</param>
    /// <returns>Formated changelog, otherwise <c>null</c> if no update available.</returns>
    public string? GetChangelog(bool reverseDisplayOrder, int maxReleases = -1)
    {
        return GetChangelog(maxReleases, reverseDisplayOrder);
    }

    /// <summary>
    /// Gets the correct and compatible <see cref="ReleaseAsset"/> for the running system and application type
    /// based on the <see cref="AssetRegexPattern"/> and <see cref="AssetExtensionFilter"/>.<br/>
    /// </summary>
    /// <param name="release">The release where you want to get the compatible asset.</param>
    /// <remarks>
    /// When multiple matching assets are found without providing <see cref="AssetExtensionFilter"/>,
    /// it will try to infer based on `EntryApplication` bundle type, which searches and defaults to:<br/>
    ///   - Windows:<br/>
    ///     - <c>.exe</c> if running under single-file (`PublishSingleFile`)<br/>
    ///     - Otherwise, defaults to <c>.msi</c><br/>
    ///   - Linux:<br/>
    ///     - <c>AppImage</c> if running under AppImage<br/>
    ///     - <c>Flatpak</c> if running under Flatpak<br/>
    ///     - Otherwise, defaults to <c>.zip</c><br/>
    ///   - If none of the above matches, it will fall back to the first matching asset
    /// </remarks>
    /// <returns>The <see cref="ReleaseAsset"/> for the current system and app type, if not found, return <c>null</c>.</returns>
    public ReleaseAsset? GetCompatibleReleaseAsset(Release release)
    {
        if (release.Assets.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(AssetRegexPattern) || AssetRegex is null) return release.Assets[0];

        var candidateAssets = new List<ReleaseAsset>();
        foreach (var asset in release.Assets)
        {
            if (!AssetRegex.IsMatch(asset.Name)) continue;
            if (!string.IsNullOrWhiteSpace(AssetExtensionFilter) && !asset.Name.EndsWith(AssetExtensionFilter)) continue;
            candidateAssets.Add(asset);
        }

        if (candidateAssets.Count == 0) return null;
        if (candidateAssets.Count == 1) return candidateAssets[0];

        // Multiple assets found, no extension filter is set, perform extra guess check.
        // Try to infer the best asset based on EntryApplication bundle.
        if (string.IsNullOrWhiteSpace(AssetExtensionFilter))
        {
            string? extension = null;

            if (OperatingSystem.IsWindows())
            {
                extension = EntryApplication.IsDotNetSingleFileApp
                    ? ".exe"
                    : ".msi"; // Default to MSI installer
            }
            else if (OperatingSystem.IsLinux())
            {
                if (EntryApplication.IsLinuxAppImage)
                {
                    extension = LinuxAppImageFileExtension;
                }
                else if (EntryApplication.IsLinuxFlatpak)
                {
                    extension = LinuxFlatpakFileExtension;
                }
                else
                {
                    extension = ".zip"; // Default to ZIP archive
                }
            }

            if (!string.IsNullOrWhiteSpace(extension))
            {
                return candidateAssets.FirstOrDefault(asset => asset.Name.EndsWith(extension), candidateAssets[0]);
            }
        }

        return candidateAssets[0];
    }

    /// <summary>
    /// Downloads the latest release for the current system.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="UpdatumDownloadedAsset"/> object, otherwise returns null if failed.</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="HttpRequestException"/>
    public Task<UpdatumDownloadedAsset?> DownloadUpdateAsync(CancellationToken cancellationToken)
    {
        return DownloadUpdateAsync(null, cancellationToken);
    }

    /// <summary>
    /// Downloads a release for the current system.
    /// </summary>
    /// <param name="release">The release to download.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="UpdatumDownloadedAsset"/> object, otherwise returns null if failed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when release is null after resolution.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    /// <exception cref="HttpRequestException">Thrown when the HTTP request fails.</exception>
    /// <exception cref="IOException">Thrown when file operations fail.</exception>
    public async Task<UpdatumDownloadedAsset?> DownloadUpdateAsync(Release? release = null, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return null;
        State = UpdatumState.DownloadingUpdate;

        release ??= LatestRelease;
        ArgumentNullException.ThrowIfNull(release);

        var asset = GetCompatibleReleaseAsset(release);
        if (asset is null) return null;

        var targetPath = Path.Combine(Path.GetTempPath(), asset.Name);  // Build temp file path

        try
        {
            using var response = await HttpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            DownloadedBytes = 0;
            DownloadSizeBytes = totalBytes > 0 ? totalBytes : -1;

            await using (var fileStream = new FileStream(targetPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize, true))
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                long totalRead = 0;
                var lastReportTime = Stopwatch.GetTimestamp();
                var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
                try
                {
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), cancellationToken).ConfigureAwait(false);
                        totalRead += bytesRead;

                        // Display progress every x seconds or on final chunk
                        var currentTimestamp = Stopwatch.GetTimestamp();
                        var elapsedTimeSpan = Stopwatch.GetElapsedTime(lastReportTime, currentTimestamp);
                        if (elapsedTimeSpan.TotalSeconds >= DownloadProgressUpdateFrequencySeconds || totalRead == totalBytes)
                        {
                            DownloadedBytes = totalRead;
                            lastReportTime = currentTimestamp;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            State = UpdatumState.None;
            var download = new UpdatumDownloadedAsset(release, asset, targetPath);

            DownloadCompleted?.Invoke(this, download);

            return download;
        }
        catch
        {
            DownloadedBytes = 0;
            State = UpdatumState.None;

            try
            {
                // Delete the temporary if it exists
                if (File.Exists(targetPath)) File.Delete(targetPath);
            }
            catch
            {
                // ignored
            }

            throw;
        }
    }

    /// <summary>
    /// Downloads and installs the latest release and install for the current system.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="UpdatumDownloadedAsset"/> object, otherwise returns null if failed.</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="HttpRequestException"/>
    /// <remarks>Note this function will never return True as program is terminated to upgrade.</remarks>
    public Task<bool> DownloadAndInstallUpdateAsync(CancellationToken cancellationToken)
    {
        return DownloadAndInstallUpdateAsync(null, cancellationToken);
    }

    /// <summary>
    /// Downloads and installs a release for the current system.
    /// </summary>
    /// <param name="release">The release to download.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="UpdatumDownloadedAsset"/> object, otherwise returns null if failed.</returns>
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="HttpRequestException"/>
    /// <remarks>Note this function will never return True as program is terminated to upgrade.</remarks>
    public async Task<bool> DownloadAndInstallUpdateAsync(Release? release = null, CancellationToken cancellationToken = default)
    {
        var download = await DownloadUpdateAsync(release, cancellationToken).ConfigureAwait(false);
        if (download is null) return false;
        cancellationToken.ThrowIfCancellationRequested();
        return await InstallUpdateAsync(download).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs the specified downloaded update asset asynchronously, replacing the current application or its components
    /// as needed.
    /// </summary>
    /// <remarks>This method supports a variety of update asset types, including portable executables, archives,
    /// Windows installers, and Linux Flatpak or AppImage files. The installation process may involve extracting files,
    /// running platform-specific scripts, or invoking system installers. On successful installation, the current
    /// application may be terminated and the updated version launched, depending on the parameters provided. The method is
    /// cross-platform and handles platform-specific behaviors internally. If the update cannot be installed due to an
    /// unrecognized file type, the method returns false without throwing an exception.</remarks>
    /// <param name="downloadedAsset">The downloaded update asset to install. Must reference a valid, existing file containing the update package or
    /// installer.</param>
    /// <param name="forceTerminate">true to forcefully terminate the current application after starting the update installation; otherwise, false. If
    /// set to true, the process will exit to allow the update to complete safely.</param>
    /// <param name="runArguments">Optional command-line arguments to pass when launching the updated application after installation. If null or
    /// omitted, the default launch behavior is used. If a special token is provided to suppress relaunch, the application
    /// will not be started after the update.</param>
    /// <returns>A task that represents the asynchronous installation operation. The task result is true if the update was
    /// successfully initiated; otherwise, false if the file type was not recognized or installation could not proceed.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file specified by downloadedAsset does not exist.</exception>
    /// <exception cref="NotSupportedException">Thrown if the update file type is not supported on the current operating system.</exception>
    /// <exception cref="IOException">Thrown if an error occurs during installation, such as a failure to install a Flatpak package.</exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public Task<bool> InstallUpdateAsync(UpdatumDownloadedAsset downloadedAsset, bool forceTerminate = true, string? runArguments = null)
    {
        if (!downloadedAsset.FileExists) throw new FileNotFoundException("File not found", downloadedAsset.FilePath);

        State = UpdatumState.InstallingUpdate;

        var filePath = downloadedAsset.FilePath;
        var fileName = Path.GetFileName(filePath);
        var fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
        var fileExtension = Path.GetExtension(fileName);

        var tmpPath = Path.GetTempPath();
        var currentVersion = EntryApplication.AssemblyVersion ?? CurrentVersion;
        var newVersionStr = downloadedAsset.TagVersionStr;

        var scriptFileName = $"{fileNameNoExt}-UpdatumAutoUpgrade";

        StreamWriter CreateScriptFile(out string scriptFilePath)
        {
            if (OperatingSystem.IsWindows())
            {
                scriptFilePath = Path.Combine(tmpPath, $"{scriptFileName}.bat");
                return File.CreateText(scriptFilePath);
            }

            scriptFilePath = Path.Combine(tmpPath, $"{scriptFileName}.sh");
            var stream = File.CreateText(scriptFilePath);
            stream.NewLine = "\n"; // Use Unix line endings
            return stream;
        }

        // *************
        // ** Windows **
        // *************
        void WriteWindowsScriptHeader(StreamWriter stream)
        {
            stream.WriteLine("@echo off");
            stream.WriteLine("setlocal enabledelayedexpansion");
            stream.WriteLine();
            stream.WriteLine($"REM Autogenerated by {nameof(UpdatumManager)} v{LibraryVersion.ToString(3)} [{DateTime.Now}]");
            stream.WriteLine($"REM {EntryApplication.AssemblyName} upgrade script");
            stream.WriteLine($"echo \"{EntryApplication.AssemblyName} v{EntryApplication.AssemblyVersionString} -> {newVersionStr} updater script\"");
            stream.WriteLine("set \"DIR=%~dp0\"");
            stream.WriteLine("cd /d \"%DIR%\"");
            stream.WriteLine();

            // Set EntryApplication variables
            stream.WriteLine($"REM {nameof(EntryApplication)} variables");
            var info = EntryApplication.GetApplicationInfoDict();
            foreach (var kp in info)
            {
                stream.WriteLine($"set \"{kp.Key}={Utilities.BatchSetValue(kp.Value)}\"");
            }
            stream.WriteLine();

            // Set variables
            stream.WriteLine("REM Variables");
            stream.WriteLine($"set \"oldVersion={Utilities.BatchSetValue(EntryApplication.AssemblyVersionString)}\"");
            stream.WriteLine($"set \"newVersion={Utilities.BatchSetValue(newVersionStr)}\"");
            stream.WriteLine($"set \"DOWNLOAD_FILEPATH={Utilities.BatchSetValue(downloadedAsset.FilePath)}\"");
            stream.WriteLine($"set \"FILEPATH={Utilities.BatchSetValue(filePath)}\"");
            stream.WriteLine($"set \"RUN_AFTER_UPGRADE={runArguments != NoRunAfterUpgradeToken}\"");
            stream.WriteLine($"set \"RUN_ARGUMENTS={Utilities.BatchSetValue(runArguments)}\"");
            stream.WriteLine();
        }

        void WriteWindowsScriptFileValidation(StreamWriter stream)
        {
            // Downloaded file path verification
            stream.WriteLine("if not exist \"%DOWNLOAD_FILEPATH%\" (");
            stream.WriteLine("  echo - Error: The expected downloaded file does not exist");
            stream.WriteLine("  exit /b 1");
            stream.WriteLine(')');
            stream.WriteLine("if not exist \"%FILEPATH%\" (");
            stream.WriteLine("  echo - Error: The expected filepath does not exist");
            stream.WriteLine("  exit /b 1");
            stream.WriteLine(')');
            stream.WriteLine();
        }

        void WriteWindowsScriptKillInstances(StreamWriter stream)
        {
            var killCommands = new List<string>(2);

            if (EntryApplication.IsRunningFromDotNetProcess)
            {
                // Dangerous if script run again latter.
                //stream.WriteLine($"taskkill /pid {Environment.ProcessId} /f /t >nul 2>&1");
            }
            else if (!string.IsNullOrWhiteSpace(EntryApplication.ProcessName))
            {
                killCommands.Add($"taskkill /IM \"%{nameof(EntryApplication.ProcessName)}%\" /T >nul 2>&1");
            }

            if (!string.IsNullOrWhiteSpace(EntryApplication.AssemblyName))
            {
                var name = $"{EntryApplication.AssemblyName}.exe";
                if (killCommands.Count == 0 || EntryApplication.ProcessName != name)
                {
                    killCommands.Add($"taskkill /IM \"{name}\" /T >nul 2>&1");
                }
            }

            if (killCommands.Count > 0)
            {
                // Kill processes
                stream.WriteLine("echo \"- Killing processes\"");
                stream.WriteLine("timeout /t 1 /NOBREAK");
                stream.WriteLine();

                foreach (var killCommand in killCommands)
                {
                    stream.WriteLine(killCommand);
                }

                stream.WriteLine("timeout /t 2 /NOBREAK");

                foreach (var killCommand in killCommands)
                {
                    stream.WriteLine($"{killCommand} /F");
                }

                stream.WriteLine(" REM IM - Image name (process filename)");
                stream.WriteLine(" REM /F - Forceful termination (without this, it tries graceful first)");
                stream.WriteLine(" REM /T - Terminate child processes");
                stream.WriteLine("timeout /t 1 /NOBREAK");
                stream.WriteLine();
            }
        }

        void WriteWindowsScriptInjectCustomScript(StreamWriter stream)
        {
            if (!string.IsNullOrWhiteSpace(InstallUpdateInjectCustomScript))
            {
                stream.WriteLine("REM Custom script provided by the author.");
                stream.WriteLine(InstallUpdateInjectCustomScript);
                stream.WriteLine("REM End of custom script provided by the author.");
                stream.WriteLine();
            }
        }

        void WriteWindowsScriptEnd(StreamWriter stream)
        {
            stream.WriteLine("echo - Removing temp source files");
            stream.WriteLine("call :DeleteIfSafe \"%DOWNLOAD_FILEPATH%\"");
            stream.WriteLine("call :DeleteIfSafe \"%FILEPATH%\"");
            stream.WriteLine();


            stream.WriteLine($"if \"%{nameof(EntryApplication.AssemblyConfiguration)}%\"==\"Release\" (");
            stream.WriteLine("  start \"\" /b cmd /c \"timeout /t 1 >nul & call :DeleteIfSafe \"\"%~f0\"\" \"\"- Removing self\"\"\"");
            stream.WriteLine(")");
            stream.WriteLine();

            stream.WriteLine("endlocal");
            stream.WriteLine("echo - Completed");
            stream.WriteLine("REM End of script");
            stream.WriteLine();

            stream.WriteLine("REM ----------------------------------------");
            stream.WriteLine("REM Usage:");
            stream.WriteLine("REM   call :DeleteIfSafe \"C:\\path\\to\\file.tmp\" \"Removing temp file\"");
            stream.WriteLine("REM Returns:");
            stream.WriteLine("REM   0 = deleted OR not found");
            stream.WriteLine("REM   1 = unsafe path OR failed to delete");
            stream.WriteLine("REM ----------------------------------------");
            stream.WriteLine(":DeleteIfSafe");
            stream.WriteLine("setlocal EnableExtensions");
            stream.WriteLine("set \"FILE=%~1\"");
            stream.WriteLine("set \"MSG=%~2\"");
            stream.WriteLine();
            stream.WriteLine("if defined MSG echo(%MSG%");
            stream.WriteLine();
            stream.WriteLine("REM Safety: empty");
            stream.WriteLine("if not defined FILE (");
            stream.WriteLine("  echo(  [SKIP] Empty path");
            stream.WriteLine("  exit /b 1");
            stream.WriteLine(")");
            stream.WriteLine();
            stream.WriteLine("REM Safety: \"\\\"");
            stream.WriteLine("if \"%FILE%\"==\"\\\" (");
            stream.WriteLine("  echo(  [SKIP] Unsafe path: \"\\\"");
            stream.WriteLine("  exit /b 1");
            stream.WriteLine(")");
            stream.WriteLine();
            stream.WriteLine("REM Safety: block drive root like C:\\, D:\\");
            stream.WriteLine("if \"%FILE:~1,2%\"==\":\\\" if \"%FILE:~3%\"==\"\" (");
            stream.WriteLine("  echo(  [SKIP] Unsafe path: drive root \"%FILE%\"");
            stream.WriteLine("  exit /b 1");
            stream.WriteLine(")");
            stream.WriteLine();
            stream.WriteLine("REM Not found = not an error (change to exit /b 1 if you prefer)");
            stream.WriteLine("if not exist \"%FILE%\" (");
            stream.WriteLine("  REM echo(  [SKIP] Not found \"%FILE%\"");
            stream.WriteLine("  exit /b 0");
            stream.WriteLine(")");
            stream.WriteLine();
            stream.WriteLine("del /F /Q \"%FILE%\" >nul 2>&1");
            stream.WriteLine(" REM /F - Force deleting of read-only files.");
            stream.WriteLine(" REM /Q - Quiet mode, do not ask if ok to delete on global wildcard.");
            stream.WriteLine();
            stream.WriteLine("if exist \"%FILE%\" (");
            stream.WriteLine("  echo(  [FAIL] Could not delete \"%FILE%\"");
            stream.WriteLine("  exit /b 1");
            stream.WriteLine(")");
            stream.WriteLine();
            stream.WriteLine("echo(  [OK] Deleted \"%FILE%\"");
            stream.WriteLine("exit /b 0");
            stream.WriteLine("REM End of :DeleteIfSafe subroutine");
        }

        // ***********
        // ** Linux **
        // ***********
        void WriteLinuxScriptHeader(StreamWriter stream)
        {
            // Shebang line
            stream.WriteLine("#!/usr/bin/env bash");
            stream.WriteLine($"# Autogenerated by {nameof(UpdatumManager)} v{LibraryVersion.ToString(3)} [{DateTime.Now}]");
            stream.WriteLine($"# {EntryApplication.AssemblyName} upgrade script");
            stream.WriteLine($"echo \"{EntryApplication.AssemblyName} v{currentVersion} -> {newVersionStr} updater script\"");
            stream.WriteLine("cd \"$(dirname \"$0\")\"");
            stream.WriteLine();

            // Set EntryApplication variables
            stream.WriteLine($"# {nameof(EntryApplication)} variables");
            var info = EntryApplication.GetApplicationInfoDict();
            foreach (var kp in info)
            {
                stream.WriteLine($"{kp.Key}={Utilities.BashAnsiCString(kp.Value)}");
            }
            stream.WriteLine();

            // Set variables
            stream.WriteLine("# Variables");
            stream.WriteLine($"oldVersion={Utilities.BashAnsiCString(EntryApplication.AssemblyVersionString)}");
            stream.WriteLine($"newVersion={Utilities.BashAnsiCString(newVersionStr)}");
            stream.WriteLine($"DOWNLOAD_FILEPATH={Utilities.BashAnsiCString(downloadedAsset.FilePath)}");
            stream.WriteLine($"FILEPATH={Utilities.BashAnsiCString(filePath)}");
            stream.WriteLine($"RUN_AFTER_UPGRADE={Utilities.BashAnsiCString(runArguments != NoRunAfterUpgradeToken)}");
            stream.WriteLine($"RUN_ARGUMENTS={Utilities.BashAnsiCString(runArguments)}");
            stream.WriteLine();

            // Functions
            stream.WriteLine("# ----------------------------------------");
            stream.WriteLine("# Usage:");
            stream.WriteLine("#   delete_if_safe \"/path/to/file.tmp\" \"Removing temp file\"");
            stream.WriteLine("# Returns:");
            stream.WriteLine("#   0 = deleted OR not found");
            stream.WriteLine("#   1 = unsafe path OR failed to delete");
            stream.WriteLine("# ----------------------------------------");
            stream.WriteLine("delete_if_safe() {");
            stream.WriteLine("  local FILE=\"${1-}\"");
            stream.WriteLine("  local MSG=\"${2-}\"");
            stream.WriteLine();
            stream.WriteLine("  # Print message");
            stream.WriteLine("  if [[ -n \"$MSG\" ]]; then");
            stream.WriteLine("    echo \"$MSG\"");
            stream.WriteLine("  fi");
            stream.WriteLine();
            stream.WriteLine("  # Safety: empty");
            stream.WriteLine("  if [[ -z \"$FILE\" ]]; then");
            stream.WriteLine("    echo \"  [SKIP] Empty path\"");
            stream.WriteLine("    return 1");
            stream.WriteLine("  fi");
            stream.WriteLine();
            stream.WriteLine("  # Safety: root");
            stream.WriteLine("  if [[ \"$FILE\" == \"/\" ]]; then");
            stream.WriteLine("    echo \"  [SKIP] Unsafe path: \\\"/\\\"\"");
            stream.WriteLine("    return 1");
            stream.WriteLine("  fi");
            stream.WriteLine();
            stream.WriteLine("  # Safety: also block \".\" and \"..\" (optional but sensible)");
            stream.WriteLine("  if [[ \"$FILE\" == \".\" || \"$FILE\" == \"..\" ]]; then");
            stream.WriteLine("    echo \"  [SKIP] Unsafe path: \\\"$FILE\\\"\"");
            stream.WriteLine("    return 1");
            stream.WriteLine("  fi");
            stream.WriteLine();
            stream.WriteLine("  # Not found = not an error");
            stream.WriteLine("  if [[ ! -e \"$FILE\" ]]; then");
            stream.WriteLine("    # echo \"  [SKIP] Not found \\\"$FILE\\\"\"");
            stream.WriteLine("    return 0");
            stream.WriteLine("  fi");
            stream.WriteLine();
            stream.WriteLine("  # Delete (force, no prompt)");
            stream.WriteLine("  rm -f -- \"$FILE\" 2>/dev/null");
            stream.WriteLine();
            stream.WriteLine("  # Verify");
            stream.WriteLine("  if [[ -e \"$FILE\" ]]; then");
            stream.WriteLine("    echo \"  [FAIL] Could not delete \\\"$FILE\\\"\"");
            stream.WriteLine("    return 1");
            stream.WriteLine("  fi");
            stream.WriteLine();
            stream.WriteLine("  echo \"  [OK] Deleted \\\"$FILE\\\"\"");
            stream.WriteLine("  return 0");
            stream.WriteLine("}");
            stream.WriteLine("# End of delete_if_safe()");
            stream.WriteLine();

        }

        void WriteLinuxScriptKillInstances(StreamWriter stream)
        {
            var killCommands = new List<string>(3);

            if (EntryApplication.IsRunningFromDotNetProcess)
            {
                // Dangerous if script run again latter.
                //stream.WriteLine($"kill -9 {Environment.ProcessId}");
            }
            else if (!string.IsNullOrWhiteSpace(EntryApplication.ProcessName))
            {
                killCommands.Add($"-f \"${nameof(EntryApplication.ProcessName)}\"");
            }

            if (!string.IsNullOrWhiteSpace(EntryApplication.AssemblyName))
            {
                if (killCommands.Count == 0 || EntryApplication.ProcessName != EntryApplication.AssemblyName)
                {
                    killCommands.Add($"-f \"${nameof(EntryApplication.AssemblyName)}\"");
                }
            }
            if (!string.IsNullOrWhiteSpace(EntryApplication.AssemblyLocation))
            {
                killCommands.Add($"-f \"dotnet.+{Path.GetFileName(Regex.Escape(EntryApplication.AssemblyLocation))}\"");
            }

            if (killCommands.Count > 0)
            {
                // Kill processes
                stream.WriteLine("echo \"- Killing processes\"");
                stream.WriteLine("sleep 0.5");
                stream.WriteLine();

                foreach (var killCommand in killCommands)
                {
                    stream.WriteLine($"pkill -TERM {killCommand} || true");
                }
                stream.WriteLine("sleep 2");
                foreach (var killCommand in killCommands)
                {
                    stream.WriteLine($"pkill -KILL {killCommand} || true");
                }
                stream.WriteLine("sleep 0.5");
                stream.WriteLine();
            }
        }

        void WriteLinuxScriptInjectCustomScript(StreamWriter stream)
        {
            if (!string.IsNullOrWhiteSpace(InstallUpdateInjectCustomScript))
            {
                stream.WriteLine("# Custom script provided by the author.");
                // Ensure only LF is used on Linux
                stream.WriteLine(InstallUpdateInjectCustomScript.Replace("\r\n", "\n").Replace("\r", "\n"));
                stream.WriteLine("# End of custom script provided by the author.");
                stream.WriteLine();
            }
        }

        void WriteLinuxScriptEnd(StreamWriter stream)
        {
            stream.WriteLine("echo \"- Removing temp source files\"");

            stream.WriteLine("delete_if_safe \"$DOWNLOAD_FILEPATH\"");
            stream.WriteLine("delete_if_safe \"$FILEPATH\"");
            stream.WriteLine();

            stream.WriteLine($"if [ \"${nameof(EntryApplication.AssemblyConfiguration)}\" = \"Release\" ]; then");
            stream.WriteLine("  delete_if_safe \"$0\" \"- Removing self\"");
            stream.WriteLine("fi");
            stream.WriteLine();


            stream.WriteLine("echo \"- Completed\"");
            stream.WriteLine("# End of script");
        }

        try
        {
            return Task.Run(() =>
            {
                ///////////////////////////////////////////////////////////
                // This can be a portable, app or single file executable //
                // If single file extract it and use it instead          //
                ///////////////////////////////////////////////////////////
                if (fileExtension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = ZipFile.OpenRead(filePath);
                    if (archive.Entries.Count == 0) return false; // No entries in the archive
                    if (archive.Entries.Count == 1) // Check for single entry, can be a folder or file...
                    {
                        var entry = archive.Entries[0];
                        if (entry.IsFile()) // Check if the entry is a file
                        {
                            filePath = Path.Combine(tmpPath, entry.Name);
                            entry.ExtractToFile(filePath, true);

                            // Replace downloaded file with the extracted file and process next checks
                            fileName = Path.GetFileName(filePath);
                            fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                            fileExtension = Path.GetExtension(fileName);
                        }
                    }
                    else // Portable app
                    {
                        // Extract the archive to a temporary directory
                        var extractDirectoryPath = Utilities.GetTemporaryFolder($"{fileNameNoExt}-UpdateExtracted", true);
                        archive.ExtractToDirectory(extractDirectoryPath, true);

                        var targetDirectoryPath = EntryApplication.BaseDirectory;
                        var currentExecutablePath = EntryApplication.ExecutablePath;
                        var newExecutingFilePath = currentExecutablePath;

                        if (string.IsNullOrWhiteSpace(targetDirectoryPath))
                        {
                            targetDirectoryPath = Path.Combine(Utilities.CommonDefaultApplicationDirectory,
                                !string.IsNullOrWhiteSpace(InstallUpdateSingleFileExecutableName)
                                    ? string.Format(InstallUpdateSingleFileExecutableName, newVersionStr)
                                    : EntryApplication.AssemblyName
                                      ?? Path.GetFileNameWithoutExtension(EntryApplication.ExecutableName)
                                      ?? fileNameNoExt);
                        }

                        var di = new DirectoryInfo(targetDirectoryPath);

                        if (OperatingSystem.IsWindows())
                        {
                            string upgradeScriptFilePath;
                            using (var stream = CreateScriptFile(out upgradeScriptFilePath))
                            {
                                WriteWindowsScriptHeader(stream);

                                stream.WriteLine($"set \"SOURCE_PATH={Utilities.BatchSetValue(extractDirectoryPath)}\"");
                                stream.WriteLine($"set \"DEST_PATH={Utilities.BatchSetValue(targetDirectoryPath)}\"");
                                stream.WriteLine();

                                // Source path verification
                                stream.WriteLine("if not exist \"%SOURCE_PATH%\" (");
                                stream.WriteLine("  echo - Error: Source path does not exist");
                                stream.WriteLine("  exit /b 1");
                                stream.WriteLine(')');
                                stream.WriteLine();

                                WriteWindowsScriptKillInstances(stream);

                                stream.WriteLine("echo - Sync/copying files over");
                                stream.WriteLine("where robocopy >nul 2>&1");
                                stream.WriteLine(" if %errorlevel%==0 (");
                                stream.WriteLine("  echo copying using robocopy");
                                stream.WriteLine("  robocopy \"%SOURCE_PATH%\" \"%DEST_PATH%\" /E /COPY:DAT /Z /R:3 /W:3 /MT:4");
                                stream.WriteLine("   REM /E - Copies all subfolders, including empty ones.");
                                stream.WriteLine("   REM /MIR - Mirror (sync) source and destination (deletes extra files in destination).");
                                stream.WriteLine("   REM /COPY:DAT - Copies data, attributes, and timestamps (no admin needed).");
                                stream.WriteLine("   REM /Z - Uses restartable mode (better for large files).");
                                stream.WriteLine("   REM /ZB - Uses restartable mode with backup (Require privileges).");
                                stream.WriteLine("   REM /MOVE - Move files and directories (delete from source after copying)");
                                stream.WriteLine("   REM /R:n - Retries on failed copies.");
                                stream.WriteLine("   REM /W:n - Wait time between retries in seconds.");
                                stream.WriteLine("   REM /MT:n - Multithreaded copies (faster).");
                                stream.WriteLine(") else (");
                                stream.WriteLine("  if not exist \"%DEST_PATH%\" mkdir \"%DEST_PATH%\"");
                                stream.WriteLine("  xcopy \"%SOURCE_PATH%\\*\" \"%DEST_PATH%\\\" /E /H /Y /C");
                                stream.WriteLine("   REM /E - Copies all subdirectories, including empty ones.");
                                stream.WriteLine("   REM /H - Copies hidden and system files.");
                                stream.WriteLine("   REM /Y - Suppresses prompting to overwrite files.");
                                stream.WriteLine("   REM /C - Continues copying even if errors occur.");
                                stream.WriteLine(")");
                                stream.WriteLine();

                                // Replace folder name with the new version name when required
                                if (Version.TryParse(newVersionStr, out var newVersion) && !currentVersion.Equals(newVersion))
                                {
                                    var newDirectoryName = SanitizeDirectoryNameWithVersion(di.Name, newVersionStr);
                                    if (di.Name != newDirectoryName)
                                    {
                                        var parent = di.Parent;
                                        if (parent is not null)
                                        {
                                            stream.WriteLine("echo - Directory is able to rename version name");
                                            var newTargetDirectoryPath = Path.Combine(parent.FullName, newDirectoryName);

                                            if (Directory.Exists(newTargetDirectoryPath))
                                            {
                                                stream.WriteLine("echo - Could not rename directory to the new version name, a directory with same name already exists");
                                            }
                                            else
                                            {
                                                stream.WriteLine("echo - Attempt to rename directory");
                                                stream.WriteLine($"set \"{nameof(EntryApplication.BaseDirectory)}={Utilities.BatchSetValue(newTargetDirectoryPath)}\"");
                                                stream.WriteLine($"move /Y \"%DEST_PATH%\" \"%{nameof(EntryApplication.BaseDirectory)}%\"");
                                                stream.WriteLine($"set \"DEST_PATH=%{nameof(EntryApplication.BaseDirectory)}%\"");

                                                // Update executable path to the new directory
                                                if (!string.IsNullOrWhiteSpace(newExecutingFilePath))
                                                {
                                                    newExecutingFilePath = newExecutingFilePath.Replace(di.FullName, newTargetDirectoryPath);
                                                    stream.WriteLine($"set \"{nameof(EntryApplication.ExecutablePath)}={Utilities.BatchSetValue(newExecutingFilePath)}\"");
                                                }

                                                targetDirectoryPath = newTargetDirectoryPath;

                                            }
                                            stream.WriteLine();
                                        }
                                    }
                                }

                                WriteWindowsScriptInjectCustomScript(stream);

                                stream.WriteLine($"if not \"%{nameof(EntryApplication.ExecutablePath)}%\"==\"\" if /I \"%RUN_AFTER_UPGRADE%\"==\"True\" (");
                                stream.WriteLine($"  echo - Execute the upgraded application");
                                stream.WriteLine($"  if exist \"%{nameof(EntryApplication.ExecutablePath)}%\" (");
                                stream.WriteLine(EntryApplication.IsRunningFromDotNetProcess
                                               ? $"    start \"\" \"{Environment.ProcessPath}\" \"%{nameof(EntryApplication.ExecutablePath)}%\" %RUN_ARGUMENTS%"
                                               : $"    start \"\" \"%{nameof(EntryApplication.ExecutablePath)}%\" %RUN_ARGUMENTS%");
                                stream.WriteLine("  ) else (");
                                stream.WriteLine($"    echo File not found: %{nameof(EntryApplication.ExecutablePath)}%, not executing!");
                                stream.WriteLine("  )");
                                stream.WriteLine(") else (");
                                stream.WriteLine("  echo - Skip execution of application, by the configuration or unable to locate the entry point");
                                stream.WriteLine(")");

                                stream.WriteLine();

                                stream.WriteLine("if \"%SOURCE_PATH%\"==\"\" exit /b 1");
                                stream.WriteLine("if /I \"%SOURCE_PATH%\"==\"\\\" exit /b 1");
                                stream.WriteLine("if /I \"%SOURCE_PATH%\"==\"C:\\\" exit /b 1");
                                stream.WriteLine("rmdir /S /Q \"%SOURCE_PATH%\"");
                                stream.WriteLine(" REM /S - Removes all directories and files in the specified directory in addition to the directory itself. Used to remove a directory tree.");
                                stream.WriteLine(" REM /Q - Quiet mode, do not ask if ok to remove a directory tree with /S.");
                                WriteWindowsScriptEnd(stream);
                            }

                            InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                            using var process = Process.Start(
                                new ProcessStartInfo("cmd.exe")
                                {
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WorkingDirectory = tmpPath,
                                    ArgumentList = { "/D", "/C", upgradeScriptFilePath }
                                });
                        }
                        else // Linux or macOS
                        {
                            string upgradeScriptFilePath;
                            using (var stream = CreateScriptFile(out upgradeScriptFilePath))
                            {
                                WriteLinuxScriptHeader(stream);
                                stream.WriteLine($"SOURCE_PATH={Utilities.BashAnsiCString(extractDirectoryPath)}");
                                stream.WriteLine($"DEST_PATH={Utilities.BashAnsiCString(targetDirectoryPath)}");
                                stream.WriteLine();

                                // Source path verification
                                stream.WriteLine("if [ ! -d \"$SOURCE_PATH\" ]; then");
                                stream.WriteLine("  echo \"- Error: Source path does not exist\"");
                                stream.WriteLine("  exit 1");
                                stream.WriteLine("fi");
                                stream.WriteLine();

                                WriteLinuxScriptKillInstances(stream);


                                if (OperatingSystem.IsMacOS())
                                {
                                    stream.WriteLine("echo \"- Removing com.apple.quarantine flag\"");
                                    stream.WriteLine("find \"$SOURCE_PATH\" -print0 | xargs -0 xattr -d com.apple.quarantine &> /dev/null || true");
                                    stream.WriteLine();

                                    if (InstallUpdateCodesignMacOSApp)
                                    {
                                        stream.WriteLine("echo \"- Force codesign to allow the app to run directly\"");
                                        stream.WriteLine("find \"$SOURCE_PATH\" -maxdepth 1 -type d -name \"*.app\" -print0 | xargs -0 -I {} codesign --force --deep --sign - \"{}\" || true");
                                        stream.WriteLine();
                                    }
                                }

                                // Copy files
                                stream.WriteLine("echo \"- Syncing/copying files over\"");
                                stream.WriteLine("if command -v rsync >/dev/null 2>&1; then");
                                stream.WriteLine("  rsync -arctxv --remove-source-files --stats \"${SOURCE_PATH}/\" \"${DEST_PATH}/\"");
                                stream.WriteLine("   # -a: Archive mode (recursive, preserve permissions, etc.)");
                                stream.WriteLine("   # -r: Recurse into directories");
                                stream.WriteLine("   # -c: Skip based on checksum, not mod-time & size");
                                stream.WriteLine("   # -t: Preserve times");
                                stream.WriteLine("   # -x: Don't cross filesystem boundaries");
                                stream.WriteLine("   # -v: Increase verbosity");
                                stream.WriteLine("   # --delete: Deletes files in destination that no longer exist in source.");
                                stream.WriteLine("   # --remove-source-files: Sender removes synchronized files (non-dir)");
                                stream.WriteLine("   # --stats: Give some file transfer stats");
                                stream.WriteLine("else");
                                stream.WriteLine("  cp -af \"${SOURCE_PATH}/.\" \"${DEST_PATH}/\"");
                                stream.WriteLine("   # -a: same as -dR --preserve=all");
                                stream.WriteLine("   # -d: same as --no-dereference --preserve=links");
                                stream.WriteLine("   # -f: if an existing destination file cannot be opened, remove it and try again");
                                stream.WriteLine("   # -R: recursive copy");
                                stream.WriteLine("fi");
                                stream.WriteLine();


                                // Replace folder name with the new version name when required
                                if (Version.TryParse(newVersionStr, out var newVersion) && !currentVersion.Equals(newVersion))
                                {
                                    var newDirectoryName = SanitizeDirectoryNameWithVersion(di.Name, newVersionStr);
                                    if (di.Name != newDirectoryName)
                                    {
                                        var parent = di.Parent;
                                        if (parent is not null)
                                        {
                                            stream.WriteLine("echo \"- Directory is able to rename version name\"");
                                            var newTargetDirectoryPath = Path.Combine(parent.FullName, newDirectoryName);

                                            if (Directory.Exists(newTargetDirectoryPath))
                                            {
                                                stream.WriteLine("echo \"- Could not rename directory to the new version name, a directory with same name already exists\"");
                                            }
                                            else
                                            {
                                                stream.WriteLine("echo \"- Attempt to rename directory\"");
                                                stream.WriteLine($"{nameof(EntryApplication.BaseDirectory)}={Utilities.BashAnsiCString(newTargetDirectoryPath)}");
                                                stream.WriteLine($"mv -f \"$DEST_PATH\" \"${nameof(EntryApplication.BaseDirectory)}\"");
                                                stream.WriteLine($"DEST_PATH=\"${nameof(EntryApplication.BaseDirectory)}\"");

                                                // Update executable path to the new directory
                                                if (!string.IsNullOrWhiteSpace(newExecutingFilePath))
                                                {
                                                    newExecutingFilePath = newExecutingFilePath.Replace(di.FullName, newTargetDirectoryPath);
                                                    stream.WriteLine($"{nameof(EntryApplication.ExecutablePath)}={Utilities.BashAnsiCString(newExecutingFilePath)}");
                                                }

                                                targetDirectoryPath = newTargetDirectoryPath;
                                            }
                                            stream.WriteLine();
                                        }
                                    }
                                }

                                // Custom script injection
                                WriteLinuxScriptInjectCustomScript(stream);

                                // Execute the upgraded application
                                stream.WriteLine($"if [ -n \"${nameof(EntryApplication.ExecutablePath)}\" ] && [ \"${{RUN_AFTER_UPGRADE:-False}}\" = \"True\" ]; then");
                                stream.WriteLine("  echo \"- Execute the upgraded application\"");
                                stream.WriteLine($"  if [ -f \"${nameof(EntryApplication.ExecutablePath)}\" ]; then");
                                if (EntryApplication.IsRunningFromDotNetProcess)
                                {
                                    stream.WriteLine($"    nohup \"{Environment.ProcessPath}\" \"${nameof(EntryApplication.ExecutablePath)}\" $RUN_ARGUMENTS >/dev/null 2>&1 &");
                                }
                                else
                                {
                                    // Make executable if it's not
                                    stream.WriteLine($"    chmod +x \"${nameof(EntryApplication.ExecutablePath)}\"");
                                    stream.WriteLine($"    nohup \"${nameof(EntryApplication.ExecutablePath)}\" $RUN_ARGUMENTS >/dev/null 2>&1 &");
                                }
                                stream.WriteLine("    sleep 1"); // Let the process start
                                stream.WriteLine("    if ps -p $! >/dev/null; then");
                                stream.WriteLine("      echo \"- Success: Application running (PID: $!)\"");
                                stream.WriteLine("    else");
                                stream.WriteLine("      echo \"- Error: Process failed to start\"");
                                stream.WriteLine("    fi");
                                stream.WriteLine("  else");
                                stream.WriteLine($"    echo \"- File not found: ${nameof(EntryApplication.ExecutablePath)}, not executing!\"");
                                stream.WriteLine("  fi");
                                stream.WriteLine("else");
                                stream.WriteLine("  echo \"- Skip execution of application (RUN_AFTER_UPGRADE is not true).\"");
                                stream.WriteLine("fi");
                                stream.WriteLine();

                                // Cleanup
                                stream.WriteLine("echo \"- Removing temp source files\"");
                                stream.WriteLine("if [ -z \"${SOURCE_PATH}\" ] || [ \"${SOURCE_PATH}\" = \"/\" ]; then");
                                stream.WriteLine("  echo \"- Error: Refusing to remove SOURCE_PATH='${SOURCE_PATH}'\"");
                                stream.WriteLine("else");
                                stream.WriteLine("  rm -rf \"$SOURCE_PATH\"");
                                stream.WriteLine("fi");
                                stream.WriteLine("if [ -z \"${DEST_PATH}\" ] || [ \"${DEST_PATH}\" = \"/\" ]; then");
                                stream.WriteLine("  echo \"- Error: Refusing to use DEST_PATH='${DEST_PATH}'\"");
                                stream.WriteLine("fi");
                                stream.WriteLine();

                                WriteLinuxScriptEnd(stream);
                            }

                            // Make the script executable
                            File.SetUnixFileMode(upgradeScriptFilePath, Utilities.Unix755FileMode);

                            InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                            // Execute the script
                            using var process = Process.Start(new ProcessStartInfo("/usr/bin/env")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WorkingDirectory = tmpPath,
                                ArgumentList = { "bash", upgradeScriptFilePath },
                            });
                        }

                        if (forceTerminate) Environment.Exit(0);
                        return true;
                    }
                }

                ////////////////////////
                // Windows Installers //
                ////////////////////////
                if (OperatingSystem.IsWindows())
                {
                    var isWindowsInstaller = false;
                    if (fileExtension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
                    {
                        isWindowsInstaller = true;
                    }
                    else if (fileExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        switch (InstallUpdateWindowsExeType)
                        {
                            case UpdatumWindowsExeType.Auto:
                                isWindowsInstaller = Utilities.IsWindowsInstallerFile(filePath);
                                break;
                            case UpdatumWindowsExeType.Installer:
                                isWindowsInstaller = true;
                                break;
                            case UpdatumWindowsExeType.SingleFileApp:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(InstallUpdateWindowsExeType));
                        }
                    }

                    if (isWindowsInstaller)
                    {
                        //if (!OperatingSystem.IsWindows()) throw new NotSupportedException($"The file type ({fileExtension}) is only supported on Windows.");

                        string upgradeScriptFilePath;
                        using (var stream = CreateScriptFile(out upgradeScriptFilePath))
                        {
                            WriteWindowsScriptHeader(stream);
                            WriteWindowsScriptFileValidation(stream);
                            WriteWindowsScriptKillInstances(stream);
                            WriteWindowsScriptInjectCustomScript(stream);

                            stream.WriteLine("echo - Calling the installer");
                            stream.WriteLine($"start \"\" /WAIT \"%FILEPATH%\" {InstallUpdateWindowsInstallerArguments}");
                            stream.WriteLine(" REM /WAIT - Start application and wait for it to terminate.");
                            stream.WriteLine();

                            stream.WriteLine("if /I \"%RUN_AFTER_UPGRADE%\"==\"True\" (");
                            stream.WriteLine("  echo - Execute the upgraded application");
                            stream.WriteLine($"  if exist \"%{nameof(EntryApplication.ExecutablePath)}%\" (");
                            stream.WriteLine($"    start \"\" \"%{nameof(EntryApplication.ExecutablePath)}%\" %RUN_ARGUMENTS%");
                            stream.WriteLine("  ) else (");
                            stream.WriteLine($"    echo - File not found: \"%{nameof(EntryApplication.ExecutablePath)}%\", not executing!");
                            stream.WriteLine("  )");
                            stream.WriteLine(") else (");
                            stream.WriteLine("  echo - Skip execution of application (RUN_AFTER_UPGRADE is not true)");
                            stream.WriteLine(")");
                            stream.WriteLine();


                            WriteWindowsScriptEnd(stream);
                        }

                        InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                        using var process = Process.Start(
                            new ProcessStartInfo("cmd.exe")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WorkingDirectory = tmpPath,
                                ArgumentList = { "/D", "/C", upgradeScriptFilePath }
                            });

                        if (forceTerminate) Environment.Exit(0); // Exit the application to install
                        return true;
                    }
                }

                ////////////////////////////////
                // Handle Linux flatpak files //
                ////////////////////////////////
                if (fileExtension.Equals(LinuxFlatpakFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!OperatingSystem.IsLinux()) throw new NotSupportedException($"The file type ({fileExtension}) is only supported on Linux.");

                    if (!File.Exists("/usr/bin/flatpak")) throw new FileNotFoundException("Flatpak is not installed on this system.", "/usr/bin/flatpak");

                    // Update or install the Flatpak package
                    var exitCode = Utilities.StartProcess("/usr/bin/flatpak", $"--user install --or-update --noninteractive \"{filePath}\"", true, 60 * 1000);
                    if (exitCode != 0) throw new IOException($"Flatpak installation failed with error code: {exitCode}.");

                    // Start the Flatpak application
                    var flatpakName = fileNameNoExt;
                    if (EntryApplication.IsLinuxFlatpak)
                    {
                        flatpakName = Path.GetFileNameWithoutExtension(EntryApplication.LinuxFlatpakPath);
                    }

                    InstallUpdateCompleted?.Invoke(this, downloadedAsset);
                    Utilities.StartProcess("/usr/bin/flatpak", $"run {flatpakName}");

                    // Exit the application
                    Environment.Exit(0);
                    return true;
                }

                ///////////////////////////////////////////////////////////
                // Handle single-file apps / executables for all systems //
                ///////////////////////////////////////////////////////////
                if (fileExtension == string.Empty
                    || fileExtension.Equals(LinuxAppImageFileExtension, StringComparison.OrdinalIgnoreCase)
                    || fileExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (fileExtension == string.Empty && OperatingSystem.IsWindows())
                        throw new NotSupportedException($"The file type ({fileExtension}) is only supported on Unix systems.");
                    if (fileExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows())
                        throw new NotSupportedException($"The file type ({fileExtension}) is only supported on Windows.");
                    if (fileExtension.Equals(LinuxAppImageFileExtension, StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsLinux())
                        throw new NotSupportedException($"The file type ({fileExtension}) is only supported on Linux.");


                    var currentExecutablePath = EntryApplication.ExecutablePath;
                    var targetDirectoryPath = EntryApplication.BaseDirectory;

                    if (string.IsNullOrWhiteSpace(targetDirectoryPath))
                    {
                        if (OperatingSystem.IsLinux())
                        {
                            targetDirectoryPath = Utilities.LinuxDefaultApplicationDirectory;
                            Directory.CreateDirectory(targetDirectoryPath);
                        }
                        else if (OperatingSystem.IsMacOS())
                        {
                            targetDirectoryPath = Utilities.MacOSDefaultApplicationDirectory;
                        }
                        else
                        {
                            targetDirectoryPath = Utilities.CommonDefaultApplicationDirectory;
                        }
                    }

                    // By defaults uses same filename as currently downloaded
                    var targetFileName = fileNameNoExt;
                    var currentExecutableFileName = Path.GetFileName(currentExecutablePath);

                    // Infer from executing filename and use it if the first 3 characters are the same,
                    // assume it's the same base name and just change the version part
                    bool SetTargetNameFromCurrentExecutableName()
                    {
                        if (!string.IsNullOrWhiteSpace(currentExecutablePath)
                            && !string.IsNullOrWhiteSpace(currentExecutableFileName)
                            && !string.IsNullOrWhiteSpace(targetFileName)
                            && currentExecutableFileName.Length >= 3 && targetFileName.Length >= 3
                            && currentExecutableFileName[0] == targetFileName[0]
                            && currentExecutableFileName[1] == targetFileName[1]
                            && currentExecutableFileName[2] == targetFileName[2])
                        {
                            targetFileName =
                                Path.GetFileNameWithoutExtension(
                                    SanitizeFileNameWithVersion(currentExecutableFileName, newVersionStr));

                            return true;
                        }

                        return false;
                    }

                    // Custom filename
                    bool SetTargetNameWithCustomName()
                    {
                        if (!string.IsNullOrWhiteSpace(InstallUpdateSingleFileExecutableName))
                        {
                            targetFileName = string.Format(InstallUpdateSingleFileExecutableName, newVersionStr);
                            return true;
                        }

                        return false;
                    }


                    switch (InstallUpdateSingleFileExecutableNameStrategy)
                    {
                        case UpdatumSingleFileExecutableNameStrategy.EntryApplicationName:
                            if (!SetTargetNameFromCurrentExecutableName()) SetTargetNameWithCustomName();
                            break;
                        case UpdatumSingleFileExecutableNameStrategy.CustomName:
                            if (!SetTargetNameWithCustomName()) SetTargetNameFromCurrentExecutableName();
                            break;
                        case UpdatumSingleFileExecutableNameStrategy.DownloadName:
                            // Default behavior, already filled as, do nothing
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(InstallUpdateSingleFileExecutableNameStrategy));
                    }

                    var targetFilePath = Path.Combine(targetDirectoryPath, $"{targetFileName}{fileExtension}");

                    /*File.Move(filePath, targetFilePath, true);

                    if (currentExecutablePath != targetFilePath
                        && !string.IsNullOrWhiteSpace(currentExecutablePath)
                        && File.Exists(currentExecutablePath))
                    {
                        try
                        {
                            File.Delete(currentExecutablePath);
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    // Set executable permissions for non-windows systems
                    if (!OperatingSystem.IsWindows())
                    {
                        // 755 permissions
                        File.SetUnixFileMode(targetFilePath, Utilities.Unix755FileMode);
                    }

                    InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                    // Execute the new file
                    if (runArguments != NoRunAfterUpgradeToken) Utilities.StartProcess(targetFilePath, runArguments);

                    // Exit the application
                    if (forceTerminate) Environment.Exit(0);
                    */

                    // New WITH script
                    if (OperatingSystem.IsWindows())
                    {

                        string upgradeScriptFilePath;
                        using (var stream = CreateScriptFile(out upgradeScriptFilePath))
                        {
                            WriteWindowsScriptHeader(stream);
                            stream.WriteLine($"set \"SOURCE_FILEPATH={Utilities.BatchSetValue(filePath)}\"");
                            stream.WriteLine($"set \"TARGET_FILEPATH={Utilities.BatchSetValue(targetFilePath)}\"");
                            stream.WriteLine();

                            // Source path verification
                            stream.WriteLine("if not exist \"%SOURCE_FILEPATH%\" (");
                            stream.WriteLine("  echo - Error: Source file does not exist");
                            stream.WriteLine("  exit /b 1");
                            stream.WriteLine(')');
                            stream.WriteLine();

                            WriteWindowsScriptKillInstances(stream);
                            WriteWindowsScriptInjectCustomScript(stream);

                            stream.WriteLine($"call :DeleteIfSafe \"%{nameof(EntryApplication.ExecutablePath)}%\" \"- Removing old executable file\"");
                            stream.WriteLine();


                            stream.WriteLine("echo - Moving the program file");
                            stream.WriteLine("move /Y \"%SOURCE_FILEPATH%\" \"%TARGET_FILEPATH%\"");
                            stream.WriteLine();

                            stream.WriteLine($"if /I \"%RUN_AFTER_UPGRADE%\"==\"True\" (");
                            stream.WriteLine($"  echo - Execute the upgraded application");
                            stream.WriteLine($"  if exist \"%TARGET_FILEPATH%\" (");
                            stream.WriteLine($"    start \"\" \"%TARGET_FILEPATH%\" %RUN_ARGUMENTS%");
                            stream.WriteLine($"  ) else (");
                            stream.WriteLine($"    echo - File not found: \"%TARGET_FILEPATH%\", not executing!");
                            stream.WriteLine($"  )");
                            stream.WriteLine($") else (");
                            stream.WriteLine($"  echo - Skip execution of application (RUN_AFTER_UPGRADE is not true)");
                            stream.WriteLine($")");
                            stream.WriteLine();


                            WriteWindowsScriptEnd(stream);
                        }

                        InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                        using var process = Process.Start(
                            new ProcessStartInfo("cmd.exe")
                            {
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WorkingDirectory = tmpPath,
                                ArgumentList = { "/D", "/C", upgradeScriptFilePath }
                            });


                        if (forceTerminate) Environment.Exit(0); // Exit the application to install

                        return true;
                    }
                    else
                    {
                        string upgradeScriptFilePath;
                        using (var stream = CreateScriptFile(out upgradeScriptFilePath))
                        {
                            WriteLinuxScriptHeader(stream);
                            stream.WriteLine($"SOURCE_FILEPATH={Utilities.BashAnsiCString(filePath)}");
                            stream.WriteLine($"TARGET_FILEPATH={Utilities.BashAnsiCString(targetFilePath)}");
                            stream.WriteLine();

                            // Source path verification
                            stream.WriteLine("if [ ! -f \"$SOURCE_FILEPATH\" ]; then");
                            stream.WriteLine("  echo \"- Error: Source filepath does not exist\"");
                            stream.WriteLine("  exit 1");
                            stream.WriteLine("fi");
                            stream.WriteLine();

                            WriteLinuxScriptKillInstances(stream);
                            WriteLinuxScriptInjectCustomScript(stream);

                            stream.WriteLine($"delete_if_safe \"${nameof(EntryApplication.ExecutablePath)}\" \"- Removing old executable file\"");
                            stream.WriteLine();

                            stream.WriteLine("echo \"- Moving the program file\"");
                            stream.WriteLine("mkdir -p \"$(dirname \"$TARGET_FILEPATH\")\"");
                            stream.WriteLine("mv -f \"$SOURCE_FILEPATH\" \"$TARGET_FILEPATH\"");
                            stream.WriteLine();

                            stream.WriteLine("echo \"- Set permission\"");
                            stream.WriteLine("chmod +x \"$TARGET_FILEPATH\"");
                            stream.WriteLine();

                            if (OperatingSystem.IsMacOS())
                            {
                                stream.WriteLine("echo \"- Removing com.apple.quarantine flag\"");
                                stream.WriteLine("xattr -d com.apple.quarantine \"$TARGET_FILEPATH\" &> /dev/null || true");
                                stream.WriteLine();

                                if (InstallUpdateCodesignMacOSApp)
                                {
                                    stream.WriteLine("echo \"- Force codesign to allow the app to run directly\"");
                                    stream.WriteLine("codesign --force --deep --sign - \"$TARGET_FILEPATH\" || true");
                                    stream.WriteLine();
                                }
                            }

                            // Execute the upgraded application
                            stream.WriteLine("if [[ \"${RUN_AFTER_UPGRADE:-False}\" = \"True\" ]]; then");
                            stream.WriteLine("  if [[ -f \"$TARGET_FILEPATH\" ]]; then");
                            stream.WriteLine("    echo \"- Execute the upgraded application\"");
                            stream.WriteLine($"    nohup \"$TARGET_FILEPATH\" $RUN_ARGUMENTS >/dev/null 2>&1 &");
                            stream.WriteLine("    sleep 1"); // Let the process start
                            stream.WriteLine("    if ps -p $! >/dev/null; then");
                            stream.WriteLine("      echo \"- Success: Application running (PID: $!)\"");
                            stream.WriteLine("    else");
                            stream.WriteLine("      echo \"- Error: Process failed to start\"");
                            stream.WriteLine("    fi");
                            stream.WriteLine("  else");
                            stream.WriteLine("    echo \"- File not found: $TARGET_FILEPATH, not executing!\"");
                            stream.WriteLine("  fi");
                            stream.WriteLine("else");
                            stream.WriteLine("  echo \"- Skip execution of application (RUN_AFTER_UPGRADE is not true)\"");
                            stream.WriteLine("fi");
                            stream.WriteLine();

                            WriteLinuxScriptEnd(stream);
                        }

                        // Make the script executable
                        File.SetUnixFileMode(upgradeScriptFilePath, Utilities.Unix755FileMode);

                        InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                        using var process = Process.Start(new ProcessStartInfo("/usr/bin/env")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = tmpPath,
                            ArgumentList = { "bash", upgradeScriptFilePath },
                        });

                        if (forceTerminate) Environment.Exit(0); // Exit the application to install

                        return true;
                    }
                }

                // Unable to find a valid file type to install
                return false;
            });
        }
        finally
        {
            State = UpdatumState.None;
        }
    }

    /// <summary>
    /// <p>Sets <see cref="ReleasesAhead"/> lists to the specified release in order to trigger a forced update.</p>
    /// <p>Use this to test your application, for debug purposes or to force a downgrade.</p>
    /// </summary>
    /// <param name="release">The release to set as update.</param>
    public void ForceTriggerUpdateFromRelease(Release release)
    {
        ReleasesAhead = [release];
        UpdateFound?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears the <see cref="Releases"/> and <see cref="ReleasesAhead"/> lists.
    /// </summary>
    public void Clear()
    {
        Releases = [];
        ReleasesAhead = [];
    }

    #endregion

    #region Static Methods
    /// <summary>
    /// Sanitizes a directory name with the new version name if it uses one and remove the hash if present.
    /// </summary>
    /// <param name="directory">The directory to sanitize.</param>
    /// <param name="newVersion">The new version to replace in directory if it uses a version in it.</param>
    /// <returns>The sanitized directory name, without full path</returns>
    public static string SanitizeDirectoryNameWithVersion(string directory, string newVersion)
    {
        var fileName = Path.GetFileName(directory);

        // Check and replace if the filePath has a version in name
        return ExtractVersionRegex().Replace(fileName, newVersion);
    }

    /// <summary>
    /// Sanitizes a file name with the new version name if it uses one and remove the hash if present.
    /// </summary>
    /// <param name="filePath">The filePath to sanitize.</param>
    /// <param name="newVersion">The new version to replace in filePath if it uses a version in it.</param>
    /// <returns>The sanitized file name, without full path</returns>
    public static string SanitizeFileNameWithVersion(string filePath, string newVersion)
    {
        var filePathSpan = filePath.AsSpan();
        var fileNameNoExt = Path.GetFileNameWithoutExtension(filePathSpan);
        var fileExtension = Path.GetExtension(filePathSpan);

        // Check if the filePath has a hash at the end and strip it
        // - AppImages renames with hash when integrated
        var index = fileNameNoExt.LastIndexOf('_');
        if (index > 0 && fileNameNoExt.Length - index >= 32)
        {
            fileNameNoExt = fileNameNoExt[..index];
        }

        var fileName = SanitizeDirectoryNameWithVersion(fileNameNoExt.ToString(), newVersion);
        return $"{fileName}{fileExtension}";
    }

    /// <summary>
    /// Sanitizes a file name with the new version name if it uses one and remove the hash if present.
    /// </summary>
    /// <param name="filePath">The filePath to sanitize.</param>
    /// <param name="newVersion">The new version to replace in filePath if it uses a version in it.</param>
    /// <returns>The sanitized file name, without full path</returns>
    public static string SanitizeFileNameWithVersion(string filePath, Version newVersion)
    {
        return SanitizeFileNameWithVersion(filePath, newVersion.ToString());
    }
    #endregion

    #region Property Changed

    /// <summary>
    /// Called when a property changes.
    /// </summary>
    /// <param name="e"></param>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
    }

    /// <summary>
    /// Raises the property changed event for the specified property.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <param name="propertyName"></param>
    /// <returns></returns>
    protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    ///     Notifies listeners that a property value has changed.
    /// </summary>
    /// <param name="propertyName">
    ///     Name of the property used to notify listeners.  This
    ///     value is optional and can be provided automatically when invoked from compilers
    ///     that support <see cref="CallerMemberNameAttribute" />.
    /// </param>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var e = new PropertyChangedEventArgs(propertyName);
        OnPropertyChanged(e);
        _propertyChanged?.Invoke(this, e);
    }

    #endregion

    #region Dispose

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the resources used by the <see cref="UpdatumManager"/>.
    /// </summary>
    /// <param name="disposing"></param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _autoUpdateCheckTimer?.Dispose();
        }

        _disposed = true;
    }

    #endregion
}