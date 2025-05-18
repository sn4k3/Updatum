using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Updatum.FakeApp;

internal class Program
{
    // https://github.com/sn4k3/UVtools/releases
    internal static readonly UpdatumManager AppUpdater = new("sn4k3", "UVtools")
    {
        // Regex filter to get the correct asset from running system
        // Defaults would work here too: EntryApplication.GenericRuntimeIdentifier
        AssetRegexPattern = $"^UVtools_{EntryApplication.GenericRuntimeIdentifier}_v",
        // Displays a basic user interface for MSI package
        // This will show the installer UI installing without any interaction
        InstallUpdateWindowsInstallerArguments = "/qb",
        // Fallback name if unable to determine the executable name from the entry application
        // This is safe to omit, but as we are using a fake app, we need to set it
        InstallUpdateSingleFileExecutableName = "UVtools",
        InstallUpdateCodesignMacOSApp = true,
    };

    private static async Task Main()
    {
        if (OperatingSystem.IsWindows())
        {
            AppUpdater.AssetExtensionFilter = ".msi"; // Force installer because zip also available on assets
        }
        else if (OperatingSystem.IsLinux())
        {
            AppUpdater.AssetExtensionFilter = ".AppImage"; // Force AppImage because zip also available on assets
        }

        Console.WriteLine(EntryApplication.ToString());

        AppUpdater.PropertyChanged += AppUpdaterOnPropertyChanged;
        Console.WriteLine($"Checking for updates for {AppUpdater.Owner}/{AppUpdater.Repository}");

        await Task.Delay(1000);

        try
        {
            var updateFound = await AppUpdater.CheckForUpdatesAsync();
            Console.WriteLine($"Update found: {updateFound}");

            if (!updateFound) return;

            var release = AppUpdater.LatestRelease!;
            var asset = AppUpdater.GetCompatibleReleaseAsset(release)!;

            Console.WriteLine();
            Console.WriteLine("Changelog:");
            Console.WriteLine(AppUpdater.GetChangelog(true));
            await Task.Delay(1000);

            while (true)
            {
                Console.WriteLine($"Do you want to download the {release.TagName} update? (y/yes/n/no)");
                var input = Console.ReadLine()?.ToLower();
                if (input is "y" or "yes")
                {
                    break;
                }
                if (input is "n" or "no")
                {
                    return;
                }
            }

            Console.WriteLine($"Downloading {asset.Name}...");
            var download = await AppUpdater.DownloadUpdateAsync();
            if (download is null)
            {
                Console.WriteLine($"Download failed");
                return;
            }


            Console.WriteLine($"Download finished: {download.FilePath}");

            while (true)
            {
                Console.WriteLine("Do you want to install the update? (y/yes/n/no)");
                var input = Console.ReadLine()?.ToLower();
                if (input is "y" or "yes")
                {
                    break;
                }
                if (input is "n" or "no")
                {
                    return;
                }
            }

            Console.WriteLine("Installing...");
            await Task.Delay(1000);
            await AppUpdater.InstallUpdateAsync(download);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static void AppUpdaterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatumManager.State))
        {
            Console.WriteLine($"Updater state changed: {AppUpdater.State}");
        }
        else if (e.PropertyName == nameof(UpdatumManager.DownloadedPercentage))
        {
            Console.WriteLine($"Downloaded: {AppUpdater.DownloadedMegabytes} MB / {AppUpdater.DownloadSizeMegabytes} MB  ({AppUpdater.DownloadedPercentage} %)");
        }
    }
}