using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Octokit;
using Updatum.Extensions;

namespace Updatum;

/// <summary>
/// Represents the Updatum class.
/// </summary>
public partial class Updatum : INotifyPropertyChanged
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
    /// Occurs when the auto install is completed.
    /// </summary>
    public event EventHandler<UpdatumDownloadedAsset>? InstallUpdateCompleted;


    #endregion

    #region Constants

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
    /// Default file extension for windows installers.
    /// </summary>
    private static string[] WindowsInstallerFileExtensions => [".msi", ".exe"];

    #endregion

    #region Static Properties

    /// <summary>
    /// Gets the current version of this library (<see cref="Updatum"/>).
    /// </summary>
    public static Version LibraryVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    private static Version EntryAssemblyVersion => EntryApplication.AssemblyVersion ?? new Version(0, 0, 0, 0);

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+){0,2}", RegexOptions.IgnoreCase)]
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
                new ProductInfoHeaderValue(EntryApplication.AssemblyName ?? nameof(Updatum), EntryAssemblyVersion.ToString())
            }
        }
    };
    #endregion

    #region Members
    private System.Timers.Timer? _autoUpdateCheckTimer;
    private bool _fetchOnlyLatestRelease;
    private DateTime _lastCheckDateTime = DateTime.MinValue;
    private IReadOnlyList<Release> _releases = [];
    private IReadOnlyList<Release> _releasesAhead = [];
    private string _assetRegexPattern = EntryApplication.GenericRuntimeIdentifier;
    private string? _assetExtensionFilter;
    private int _checkForUpdateCount;
    private double _downloadProgressUpdateFrequencySeconds = 0.5;
    private long _downloadSizeBytes = -1;
    private long _downloadedBytes;
    private string? _installUpdateWindowsInstallerArguments;
    private string? _installUpdateSingleFileExecutableName;
    private string? _installUpdateInjectCustomScript;
    private UpdatumState _state;
    private bool _installUpdateCodesignMacOSApp;

    #endregion

    #region Properties
    /// <summary>
    /// Gets the GitHub client used to access the GitHub API.
    /// </summary>
    public GitHubClient GithubClient { get; } = new(new Octokit.ProductHeaderValue(EntryApplication.AssemblyName ?? nameof(Updatum), EntryAssemblyVersion.ToString()));

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
    /// The owner of the repository.
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// The name of the repository.
    /// </summary>
    public required string Repository { get; init; }

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
            if(!RaiseAndSetIfChanged(ref _assetRegexPattern, value)) return;
            AssetRegex = string.IsNullOrWhiteSpace(value) ? null : new Regex(_assetRegexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
    /// Initializes a new instance of the <see cref="Updatum"/> class.
    /// </summary>
    public Updatum()
    {
        if (!string.IsNullOrWhiteSpace(_assetRegexPattern))
        {
            AssetRegex = new Regex(_assetRegexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Updatum"/> class with the specified parameters.
    /// </summary>
    /// <param name="owner">The repository owner</param>
    /// <param name="repository">The repository name</param>
    /// <param name="currentVersion">Your app version that is current running, if <c>null</c>, it will fetch the version from EntryAssembly.</param>
    /// <param name="gitHubCredentials">Pass the GitHub credentials if required, for extra tokens or visibility.</param>
    [SetsRequiredMembers]
    public Updatum(string owner, string repository, Version? currentVersion = null, Credentials? gitHubCredentials = null) : this()
    {
        Owner = owner;
        Repository = repository;
        if (currentVersion is not null) CurrentVersion = currentVersion;
        if (gitHubCredentials is not null) GithubClient.Credentials = gitHubCredentials;
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
    /// <returns><c>True</c> if update found relative to given <see cref="CurrentVersion"/>, otherwise <c>false</c>.</returns>
    /// <exception cref="ApiException"/>
    public async Task<bool> CheckForUpdatesAsync()
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
                var release = await GithubClient.Repository.Release.GetLatest(Owner, Repository);
                Releases = [release];
            }
            else
            {
                Releases = await GithubClient.Repository.Release.GetAll(Owner, Repository, GitHubApiOptions);
            }

            var releasesAheadList = new List<Release>();

            foreach (var release in Releases)
            {
                if (release.Draft // Skip draft releases
                    || release.PublishedAt is null // Skip not published releases
                    || release.Assets.Count == 0 // Skip releases without assets
                    || !char.IsAsciiDigit(release.TagName[^1])) // Skip tag names that don't end with a digit (eg: v1.0.0-alpha)
                    continue;

                var tagVersion = release.GetTagVersion();

                if (tagVersion is null) continue;
                if (tagVersion.CompareTo(CurrentVersion) <= 0)
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
    /// Gets the correct and compatible <see cref="ReleaseAsset"/> for the running system.
    /// </summary>
    /// <param name="release"></param>
    /// <returns>The <see cref="ReleaseAsset"/> for the current system, if not found, return <c>null</c>.</returns>
    public ReleaseAsset? GetCompatibleReleaseAsset(Release release)
    {
        if (release.Assets.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(AssetRegexPattern) || AssetRegex is null) return release.Assets[0];

        foreach (var asset in release.Assets)
        {
            if (!AssetRegex.IsMatch(asset.Name)) continue;
            if (!string.IsNullOrWhiteSpace(AssetExtensionFilter) && !asset.Name.EndsWith(AssetExtensionFilter)) continue;
            return asset;
        }

        return null;
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
    /// <exception cref="ArgumentNullException"/>
    /// <exception cref="OperationCanceledException"/>
    /// <exception cref="HttpRequestException"/>
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
            using var response = await HttpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            DownloadedBytes = 0;
            DownloadSizeBytes = totalBytes > 0 ? totalBytes : -1;

            await using (var fileStream = new FileStream(targetPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, DefaultBufferSize, true))
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[DefaultBufferSize];
                long totalRead = 0;
                int bytesRead;
                var lastReportTime = DateTime.Now;
                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    // Display progress every x seconds or on final chunk
                    if ((DateTime.Now - lastReportTime).TotalSeconds >= DownloadProgressUpdateFrequencySeconds || totalRead == totalBytes)
                    {
                        DownloadedBytes = totalRead;
                        lastReportTime = DateTime.Now;
                    }
                }
            }

            State = UpdatumState.None;
            var download = new UpdatumDownloadedAsset(release, asset, targetPath);

            DownloadCompleted?.Invoke(this, download);

            return download;
        }
        catch (Exception)
        {
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
        var download = await DownloadUpdateAsync(release, cancellationToken);
        if (download is null) return false;
        cancellationToken.ThrowIfCancellationRequested();
        return await InstallUpdateAsync(download);
    }

    /// <summary>
    /// Tries to auto installs the update.
    /// </summary>
    /// <param name="downloadedAsset">The downloaded asset to upgrade from.</param>
    /// <param name="runArguments"><p>Arguments to pass when run the upgraded application.</p>
    /// <p>Use the token: <see cref="NoRunAfterUpgradeToken"/> to prevent app from rerun after upgrade.</p></param>
    /// <exception cref="FileNotFoundException"/>
    /// <exception cref="NotSupportedException"/>
    /// <exception cref="IOException"/>
    /// <exception cref="UnauthorizedAccessException"/>
    public Task<bool> InstallUpdateAsync(UpdatumDownloadedAsset downloadedAsset, string? runArguments = null)
    {
        if (!downloadedAsset.FileExists) throw new FileNotFoundException("File not found", downloadedAsset.FilePath);

        State = UpdatumState.InstallingUpdate;

        try
        {
            return Task.Run(() =>
            {
                var filePath = downloadedAsset.FilePath;
                var fileName = Path.GetFileName(filePath);
                var fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                var fileExtension = Path.GetExtension(fileName);

                var tmpPath = Path.GetTempPath();
                var currentVersion = EntryApplication.AssemblyVersion ?? CurrentVersion;
                var newVersionStr = downloadedAsset.TagVersionStr;

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
                                    ? InstallUpdateSingleFileExecutableName
                                    : EntryApplication.AssemblyName
                                      ?? Path.GetFileNameWithoutExtension(EntryApplication.ExecutableFileName)
                                      ?? fileNameNoExt);
                        }

                        var di = new DirectoryInfo(targetDirectoryPath);

                        if (OperatingSystem.IsWindows())
                        {
                            var upgradeScriptFilePath = Path.Combine(tmpPath, $"{fileNameNoExt}-UpdatumAutoUpgrade.bat");

                            using (var stream = File.CreateText(upgradeScriptFilePath))
                            {
                                stream.WriteLine($"REM Autogenerated by {nameof(Updatum)} v{LibraryVersion.ToString(3)}");
                                stream.WriteLine($"REM {EntryApplication.AssemblyName} upgrade script");
                                stream.WriteLine("@echo off");
                                stream.WriteLine("setlocal enabledelayedexpansion");
                                stream.WriteLine($"echo {EntryApplication.AssemblyName} v{currentVersion} -> {newVersionStr} updater script");
                                stream.WriteLine();
                                stream.WriteLine("set DIR=%~dp0%");
                                stream.WriteLine($"set \"oldVersion={currentVersion}\"");
                                stream.WriteLine($"set \"newVersion={newVersionStr}\"");
                                stream.WriteLine($"set \"DOWNLOAD_FILEPATH={downloadedAsset.FilePath}\"");
                                stream.WriteLine($"set \"SOURCE_PATH={extractDirectoryPath}\"");
                                stream.WriteLine($"set \"DEST_PATH={targetDirectoryPath}\"");
                                stream.WriteLine();

                                // Source path verification
                                stream.WriteLine("if not exist \"%SOURCE_PATH%\" (");
                                stream.WriteLine("  echo - Error: Source path does not exist");
                                stream.WriteLine("  exit /b -1");
                                stream.WriteLine(')');
                                stream.WriteLine();

                                var killCommands = new List<string>(2);

                                if (EntryApplication.IsRunningFromDotNetProcess)
                                {
                                    // Dangerous if script run again latter.
                                    //stream.WriteLine($"taskkill /pid {Environment.ProcessId} /f /t");
                                }
                                else if (!string.IsNullOrWhiteSpace(EntryApplication.ProcessName))
                                {
                                    killCommands.Add($"taskkill /IM \"{EntryApplication.ProcessName}\" /T");
                                }

                                if (!string.IsNullOrWhiteSpace(EntryApplication.AssemblyName))
                                {
                                    var name = $"{EntryApplication.AssemblyName}.exe";
                                    if (killCommands.Count == 0 || EntryApplication.ProcessName != name)
                                    {
                                        killCommands.Add($"taskkill /IM \"{name}\" /T");
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

                                stream.WriteLine("echo - Sync/copying files over");
                                stream.WriteLine("where robocopy >nul 2>&1 && (");
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
                                stream.WriteLine(") || (");
                                stream.WriteLine("  xcopy \"%SOURCE_PATH%\\*\" \"%DEST_PATH%\" /E /H /Y /C");
                                stream.WriteLine("   REM /E - Copies all subdirectories, including empty ones.");
                                stream.WriteLine("   REM /H - Copies hidden and system files.");
                                stream.WriteLine("   REM /Y - Suppresses prompting to overwrite files.");
                                stream.WriteLine("   REM /C - Continues copying even if errors occur.");
                                stream.WriteLine(")");
                                stream.WriteLine();

                                // Replace folder name with the new version name when required
                                if (Version.TryParse(newVersionStr, out var newVersion) && !currentVersion.Equals(newVersion))
                                {
                                    var newDirectoryName = SanitizeFileNameWithVersion(di.Name, newVersionStr);
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
                                                stream.WriteLine($"move / Y \"%DEST_PATH%\" \"{newTargetDirectoryPath}\"");
                                                stream.WriteLine($"SET \"%DEST_PATH%={newTargetDirectoryPath}\"");

                                                // Update executable path to the new directory
                                                if (!string.IsNullOrWhiteSpace(newExecutingFilePath))
                                                    newExecutingFilePath = newExecutingFilePath.Replace(di.FullName, newTargetDirectoryPath);

                                                targetDirectoryPath = newTargetDirectoryPath;

                                            }
                                            stream.WriteLine();
                                        }
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(InstallUpdateInjectCustomScript))
                                {
                                    stream.WriteLine("REM Custom script provided by the author.");
                                    stream.WriteLine(InstallUpdateInjectCustomScript);
                                    stream.WriteLine("REM End of custom script provided by the author.");
                                    stream.WriteLine();
                                }

                                if (!string.IsNullOrWhiteSpace(newExecutingFilePath) && runArguments != NoRunAfterUpgradeToken)
                                {
                                    stream.WriteLine("echo - Execute the upgraded application");
                                    stream.WriteLine($"if exist \"{newExecutingFilePath}\" (");
                                    stream.WriteLine(EntryApplication.IsRunningFromDotNetProcess
                                        ? $"  start \"\" \"{Environment.ProcessPath}\" \"{newExecutingFilePath}\" {runArguments}"
                                        : $"  start \"\" \"{newExecutingFilePath}\" {runArguments}");
                                    stream.WriteLine(") else (");
                                    stream.WriteLine($"  echo File not found: {newExecutingFilePath}, not executing!");
                                    stream.WriteLine(")");
                                }
                                else
                                {
                                    stream.WriteLine("echo - Skip execution of application, by the configuration or unable to locate the entry point.");
                                }

                                stream.WriteLine();

                                stream.WriteLine("echo - Removing temp source files");
                                stream.WriteLine("del /F /Q \"%DOWNLOAD_FILEPATH%\"");
                                stream.WriteLine(" REM /F - Force deleting of read-only files.");
                                stream.WriteLine(" REM /Q - Quiet mode, do not ask if ok to delete on global wildcard.");
                                stream.WriteLine("rmdir /S /Q \"%SOURCE_PATH%\"");
                                stream.WriteLine(" REM /S - Removes all directories and files in the specified directory in addition to the directory itself. Used to remove a directory tree.");
                                stream.WriteLine(" REM /Q - Quiet mode, do not ask if ok to remove a directory tree with /S.");
                                stream.WriteLine();

#if !DEBUG
                                stream.WriteLine("echo - Removing self");
                                stream.WriteLine("del /F /Q \"%~f0\"");
#endif

                                stream.WriteLine("endlocal");
                                stream.WriteLine("echo '- Completed'");
                                stream.WriteLine("REM End of script");
                                stream.WriteLine("pause");
                            }

                            InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                            using var process = Process.Start(
                                new ProcessStartInfo("cmd.exe", $"/c \"{upgradeScriptFilePath}\"")
                                {
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WorkingDirectory = tmpPath
                                });
                        }
                        else // Linux or macOS
                        {
                            var upgradeScriptFilePath = Path.Combine(tmpPath, $"{fileNameNoExt}-UpdatumAutoUpgrade.sh");

                            using (var stream = File.CreateText(upgradeScriptFilePath))
                            {
                                stream.NewLine = "\n";

                                // Shebang line
                                stream.WriteLine("#!/bin/bash");
                                stream.WriteLine($"# Autogenerated by {nameof(Updatum)} v{LibraryVersion.ToString(3)}");
                                stream.WriteLine($"# {EntryApplication.AssemblyName} upgrade script");
                                stream.WriteLine($"echo \"{EntryApplication.AssemblyName} v{currentVersion} -> {newVersionStr} updater script\"");
                                stream.WriteLine();

                                // Set variables
                                stream.WriteLine("cd \"$(dirname \"$0\")\"");
                                stream.WriteLine($"oldVersion=\"{currentVersion}\"");
                                stream.WriteLine($"newVersion=\"{newVersionStr}\"");
                                stream.WriteLine($"DOWNLOAD_FILEPATH=\"{downloadedAsset.FilePath}\"");
                                stream.WriteLine($"SOURCE_PATH=\"{extractDirectoryPath}\"");
                                stream.WriteLine($"DEST_PATH=\"{targetDirectoryPath}\"");
                                stream.WriteLine();

                                // Source path verification
                                stream.WriteLine("if [ ! -d \"$SOURCE_PATH\" ]; then");
                                stream.WriteLine("  echo \"- Error: Source path does not exist\"");
                                stream.WriteLine("  exit -1");
                                stream.WriteLine("fi");
                                stream.WriteLine();

                                var killCommands = new List<string>(3);

                                if (EntryApplication.IsRunningFromDotNetProcess)
                                {
                                    // Dangerous if script run again latter.
                                    //stream.WriteLine($"kill -9 {Environment.ProcessId}");
                                }
                                else if (!string.IsNullOrWhiteSpace(EntryApplication.ProcessName))
                                {
                                    killCommands.Add($"-f \"{EntryApplication.ProcessName}\" || true");
                                }

                                if (!string.IsNullOrWhiteSpace(EntryApplication.AssemblyName))
                                {
                                    if (killCommands.Count == 0 || EntryApplication.ProcessName != EntryApplication.AssemblyName)
                                    {
                                        killCommands.Add($"-f \"{EntryApplication.AssemblyName}\" || true");
                                    }
                                }
                                if (!string.IsNullOrWhiteSpace(EntryApplication.AssemblyLocation))
                                {
                                    killCommands.Add($"-f \"dotnet.+{Path.GetFileName(Regex.Escape(EntryApplication.AssemblyLocation))}\" || true");
                                }

                                if (killCommands.Count > 0)
                                {
                                    // Kill processes
                                    stream.WriteLine("echo \"- Killing processes\"");
                                    stream.WriteLine("sleep 0.5");
                                    stream.WriteLine();

                                    foreach (var killCommand in killCommands)
                                    {
                                        stream.WriteLine($"pkill -TERM {killCommand}");
                                    }
                                    stream.WriteLine("sleep 2");
                                    foreach (var killCommand in killCommands)
                                    {
                                        stream.WriteLine($"pkill -KILL {killCommand}");
                                    }
                                    stream.WriteLine("sleep 0.5");
                                    stream.WriteLine();
                                }


                                if (OperatingSystem.IsMacOS())
                                {
                                    stream.WriteLine("echo \"- Removing com.apple.quarantine flag\"");
                                    stream.WriteLine("find \"$SOURCE_PATH\" -print0 | xargs -0 xattr -d com.apple.quarantine &> /dev/null");
                                    stream.WriteLine();

                                    if (InstallUpdateCodesignMacOSApp)
                                    {
                                        stream.WriteLine("echo \"- Force codesign to allow the app to run directly\"");
                                        stream.WriteLine("find \"$SOURCE_PATH\" -maxdepth 1 -type d -name \"*.app\" -print0 | xargs -0 -I {} codesign --force --sign - \"{}\"");
                                        stream.WriteLine();
                                    }
                                }

                                // Copy files
                                stream.WriteLine("echo \"- Syncing/copying files over\"");
                                stream.WriteLine("if command -v rsync >/dev/null 2>&1; then");
                                stream.WriteLine("  rsync -arctxv --remove-source-files --stats \"$SOURCE_PATH/\" \"$DEST_PATH/\"");
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
                                stream.WriteLine("  cp -fR \"$SOURCE_PATH/\"* \"$DEST_PATH/\"");
                                stream.WriteLine("   # -f: if an existing destination file cannot be opened, remove it and try again");
                                stream.WriteLine("   # -R: recursive copy");
                                stream.WriteLine("fi");
                                stream.WriteLine();


                                // Replace folder name with the new version name when required
                                if (Version.TryParse(newVersionStr, out var newVersion) && !currentVersion.Equals(newVersion))
                                {
                                    var newDirectoryName = SanitizeFileNameWithVersion(di.Name, newVersionStr);
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
                                                stream.WriteLine($"mv -f \"$DEST_PATH\" \"{newTargetDirectoryPath}\"");
                                                stream.WriteLine($"DEST_PATH=\"{newTargetDirectoryPath}\"");

                                                // Update executable path to the new directory
                                                if (!string.IsNullOrWhiteSpace(newExecutingFilePath))
                                                    newExecutingFilePath = newExecutingFilePath.Replace(di.FullName, newTargetDirectoryPath);

                                                targetDirectoryPath = newTargetDirectoryPath;
                                            }
                                            stream.WriteLine();
                                        }
                                    }
                                }

                                // Custom script injection
                                if (!string.IsNullOrWhiteSpace(InstallUpdateInjectCustomScript))
                                {
                                    stream.WriteLine("# Custom script provided by the author.");
                                    stream.WriteLine(InstallUpdateInjectCustomScript);
                                    stream.WriteLine("# End of custom script provided by the author.");
                                    stream.WriteLine();
                                }

                                // Execute the upgraded application
                                if (!string.IsNullOrWhiteSpace(newExecutingFilePath) && runArguments != NoRunAfterUpgradeToken)
                                {
                                    stream.WriteLine("echo \"- Execute the upgraded application\"");
                                    stream.WriteLine($"if [ -f \"{newExecutingFilePath}\" ]; then");
                                    if (EntryApplication.IsRunningFromDotNetProcess)
                                    {
                                        //stream.WriteLine($"  \"{EntryApplication.ExecutablePath}\" \"{newExecutingFilePath}\" {runArguments} & disown -h $!");
                                        stream.WriteLine($"  nohup \"{Environment.ProcessPath}\" \"{newExecutingFilePath}\" {runArguments} >/dev/null 2>&1 &");
                                    }
                                    else
                                    {
                                        // Make executable if it's not
                                        stream.WriteLine($"  chmod +x \"{newExecutingFilePath}\"");
                                        //stream.WriteLine($"  \"{newExecutingFilePath}\" {runArguments} & disown -h $!");
                                        stream.WriteLine($"  nohup \"{newExecutingFilePath}\" {runArguments} >/dev/null 2>&1 &");
                                    }

                                    stream.WriteLine("  sleep 1"); // Let the process start
                                    stream.WriteLine("  if ps -p $! >/dev/null; then");
                                    stream.WriteLine("    echo \"- Success: Application running (PID: $!)\"");
                                    stream.WriteLine("  else");
                                    stream.WriteLine("    echo \"- Error: Process failed to start\"");
                                    stream.WriteLine("  fi");
                                    stream.WriteLine("else");
                                    stream.WriteLine($"  echo \"- File not found: {newExecutingFilePath}, not executing!\"");
                                    stream.WriteLine("fi");
                                }
                                else
                                {
                                    stream.WriteLine("echo \"- Skip execution of application, by the configuration or unable to locate the entry point.\"");
                                }

                                stream.WriteLine();

                                // Cleanup
                                stream.WriteLine("echo \"- Removing temp source files\"");
                                stream.WriteLine("rm -f \"$DOWNLOAD_FILEPATH\"");
                                stream.WriteLine("rm -rf \"$SOURCE_PATH\"");
                                stream.WriteLine();

#if !DEBUG
                                stream.WriteLine("echo \"- Removing self\"");
                                stream.WriteLine("rm -f -- \"$0\"");
#endif

                                stream.WriteLine("echo \"- Completed\"");
                                stream.WriteLine("# End of script");
                            }

                            // Make the script executable
                            File.SetUnixFileMode(upgradeScriptFilePath, Utilities.Unix755FileMode);

                            InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                            // Execute the script
                            using var process = Process.Start(
                                new ProcessStartInfo("/bin/bash", $"\"{upgradeScriptFilePath}\"")
                                {
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    WorkingDirectory = tmpPath
                                });
                        }

                        Environment.Exit(0);
                    }
                }

                ///////////////////////////////////////////////////////////
                // Handle single-file apps / executables for all systems //
                ///////////////////////////////////////////////////////////
                if (fileExtension == string.Empty
                    || fileExtension.Equals(LinuxAppImageFileExtension, StringComparison.OrdinalIgnoreCase)
                    || (fileExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && EntryApplication.IsSingleFileApp)
                    )
                {
                    if (fileExtension == string.Empty && OperatingSystem.IsWindows())
                        throw new NotSupportedException("This file type is only supported on Unix systems.");
                    if (fileExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows())
                        throw new NotSupportedException("This file type is only supported on Windows.");
                    if (fileExtension.Equals(LinuxAppImageFileExtension, StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsLinux())
                        throw new NotSupportedException("This file type is only supported on Linux.");

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

                    if (!string.IsNullOrWhiteSpace(currentExecutablePath)) // Infer from previous filename and use it instead
                    {
                        targetFileName = SanitizeFileNameWithVersion(Path.GetFileNameWithoutExtension(currentExecutablePath), newVersionStr);
                    }
                    else if (!string.IsNullOrWhiteSpace(InstallUpdateSingleFileExecutableName)) // Manual filename
                    {
                        targetFileName = string.Format(InstallUpdateSingleFileExecutableName, newVersionStr);
                    }

                    var targetFilePath = Path.Combine(targetDirectoryPath, $"{targetFileName}{fileExtension}");

                    File.Move(downloadedAsset.FilePath, targetFilePath, true);

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
                    Environment.Exit(0);

                    return true;
                }


                ////////////////////////
                // Windows Installers //
                ////////////////////////
                if (WindowsInstallerFileExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
                {
                    if (!OperatingSystem.IsWindows()) throw new NotSupportedException("This file type is only supported on Windows.");
                    Utilities.StartProcess(filePath, InstallUpdateWindowsInstallerArguments);

                    InstallUpdateCompleted?.Invoke(this, downloadedAsset);

                    Environment.Exit(0); // Exit the application to install

                    return true;
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
    /// Sanitizes a file name with the new version name if it uses one and remove the hash if present.
    /// </summary>
    /// <param name="filename">The filename or directory to sanitize.</param>
    /// <param name="newVersion">The new version to replace in filename if it uses a version in it.</param>
    public static string SanitizeFileNameWithVersion(string filename, string newVersion)
    {
        var fileNameNoExt = Path.GetFileNameWithoutExtension(filename);
        var fileExtension = Path.GetExtension(filename);

        var index = fileNameNoExt.LastIndexOf('_', 1);
        if (index >= 0)
        {
            // Check if the filename has a hash at the end and strip it
            var hash = fileNameNoExt[index..];
            if (hash.Length >= 32) fileNameNoExt = fileNameNoExt[..index];
        }

        // Check and replace if the filename has a version in name
        fileNameNoExt = ExtractVersionRegex().Replace(fileNameNoExt, newVersion);
        return $"{fileNameNoExt}{fileExtension}";
    }

    /// <summary>
    /// Sanitizes a file name with the new version name if it uses one and remove the hash if present.
    /// </summary>
    /// <param name="filename">The filename or directory to sanitize.</param>
    /// <param name="newVersion">The new version to replace in filename if it uses a version in it.</param>
    public static string SanitizeFileNameWithVersion(string filename, Version newVersion)
    {
        return SanitizeFileNameWithVersion(filename, newVersion.ToString());
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

}