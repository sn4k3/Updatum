# [![](https://github.com/sn4k3/Updatum/raw/main/media/Updatum-32.png)](#) Updatum

[![License](https://img.shields.io/github/license/sn4k3/Updatum?style=for-the-badge)](https://github.com/sn4k3/Updatum/blob/master/LICENSE)
[![GitHub repo size](https://img.shields.io/github/repo-size/sn4k3/Updatum?style=for-the-badge)](#)
[![Code size](https://img.shields.io/github/languages/code-size/sn4k3/Updatum?style=for-the-badge)](#)
[![Nuget](https://img.shields.io/nuget/v/Updatum?style=for-the-badge)](https://www.nuget.org/packages/Updatum)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/sn4k3?color=red&style=for-the-badge)](https://github.com/sponsors/sn4k3)

Updatum is a lightweight and easy-to-integrate C# library designed to automate your application updates using **GitHub Releases**.  
It simplifies the update process by checking for new versions, retrieving release notes, and optionally downloading and launching installers or executables.  
Whether you're building a desktop tool or a larger application, Updatum helps you ensure your users are always on the latest version — effortlessly.

## Features

- **💻 Cross-Platform:** Works on Windows, Linux, and MacOS.
- **⚙️ Flexible Integration:** Easily embed into your WPF, WinForms, or console applications.
- **🔍 Update Checker:** Manually and/or automatically checks GitHub for the latest release version.
- **📦 Asset Management:** Automatically fetches the latest release assets based on your platform and architecture.
- **📄 Changelog Support:** Retrive release(s) notes directly from GitHub Releases.
- **⬇️ Download with Progress Tracking:** Download and track progress handler.
- **🔄 Auto-Upgrade Support:** Automatically upgrades your application to a new release.
- **📦 No External Dependencies:** Minimal overhead and no need for complex update infrastructure.

## Requirements

1. Publish your application to GitHub Releases.
1. Name your assets acordingly the platform and architecture:
   - Windows: `MyApp_win-x64_v1.0.0.exe`, `MyApp_win-x64_v1.0.0.msi`, `MyApp_win-x64_v1.0.0.zip`
   - Linux: `MyApp_linux-x64_v1.0.0.AppImage`, `MyApp_linux-x64_v1.0.0.zip`
   - MacOS x64: `MyApp_osx-arm64_v1.0.0.zip`
   - [Example for assets](https://github.com/sn4k3/UVtools/releases/latest)
   - **NOTE:** The asset fetching can be configurable

## Auto installer strategy

You can opt to install the update manually or automatically.
If automatic installation is called, Updatum will:

- Check if asset is a zip file, if so, and if only one asset is found, it will extract the file to a temporary folder, and continue with the other checks.
  - Otherwise, if the zip file contains multiple files it will be extracted to a temporary folder, and handled as a portable application.
  - A script will be created and executed to perform checks, kill instances, merge files, rename the version in folder name and execute the new instance.
- If file is an single-file application such as dotnet single-file executables or linux AppImage, it will be moved to the current folder and rename it to the current name and version.
- If file is an installer, it will be executed and follow the normal installation process.

### Compatibility

- Portable applications (zip)
- Dotnet single-files publishes
- Windows Installer (exe and msi)
- Linux [AppImage](https://appimage.org/)
- macOS app bunble

## Example

Check the [Updatum.FakeApp](https://github.com/sn4k3/Updatum/blob/main/Updatum.FakeApp/Program.cs) project for a example of how to use Updatum.

## Usage


```csharp
// Create an instance of Updatum, keep it global and single instance.
// By default it will fetch your current version from Assembly.GetEntryAssembly().GetName().Version
// If you want to be safe and strict pass the current version, you can set 3rd argument as: Assembly.GetExecutingAssembly().GetName().Version
internal static readonly Updatum AppUpdater = new("sn4k3", "UVtools")
{
    InstallUpdateWindowsInstallerArguments = "/qb" // Displays a basic user interface for MSI package
};


public static async Task Main(string[] args)
{
    try
    {
        // Check for updates
        // Return true if a new version is available, otherwise false
        var updateFound = await AppUpdater.CheckForUpdatesAsync();

        // Stop if no update is found
        if (!updateFound) return;

        // Optional show a message to the user with the changelog
        Console.WriteLine("Changelog:");
        Console.WriteLine(AppUpdater.GetChangelog());

        // Downloads the update to temporary folder.
        // Returns a UpdatumDownloadAsset object with the download information
        // Returns null if failed to download the asset
        var downloadedAsset = await AppUpdater.DownloadUpdateAsync();

        if (downloadedAsset == null)
        {
            Console.WriteLine("Failed to download the update.");
            return;
        }

        // You can manually handle the installation or call the Install method:
        // Returns false if the installation failed ortherwise it will never return true as the process will be terminated to complete the installation.
        await AppUpdater.InstallUpdateAsync(downloadedAsset);
    }
    catch (Exception ex)
    {
        // Handle exceptions
        Console.WriteLine($"Error: {ex.Message}");
    }
}
```

## FAQs

<details>
<summary>How to provide a custom asset pattern</summary>

### Customize the asset pattern

Your asset naming convention may differ from the default one, and you can customize the asset fetcher to suit your needs.  
By using the property `AssetRegexPattern` you can provide a regex pattern to match your assets.

```cssharp
// Expect assets to be named like: MyApp_winx64_v1.0.0
AppUpdater.AssetRegexPattern = $"{RuntimeInformation.RuntimeIdentifier.Replace("-", string.Empty)}";
```
</details>



<details>
<summary>I have multiple assets with same name, but different extension</summary>

### Customize the asset extension filter

If you have multiple assets with the same name but different extensions, 
for example `MyApp_win-x64_v1.0.0.zip` (The portable) and `MyApp_win-x64_v1.0.0.msi` (The installer),
you can use the `AssetExtensionFilter` property to filter them out.  
You will require some sort of file included in the application folder to know if user is running the portable or the installer version.
If you omit this step, the first asset will be used.


```cssharp
if (IsPortableApp) AppUpdater.AssetExtensionFilter = "zip";
```

**Notes:** 

- The `AssetRegexPattern` can also be used for the same porpose, but it is not recommended.
</details>



<details>
<summary>How to listen for the download progress</summary>

### Listen for the download progress

If you using a binding framework like WPF, WinUI or Avalonia, you can use the properties directly:
- `DownloadedMegabytes` to bind to a progress bar text
- `DownloadTotalSizeMegabytes` to bind to a progress bar text
- `DownloadedPercentage` to bind to a progress bar
- Example: {0} / {1} Megabytes

As all properties raises changes the UI will reflect changes on such frameworks.  
If you require to listen for the download progress or redirect the value, you can subscribe `PropertyChanged` event.

```csharp
private static void AppUpdaterOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName == nameof(Updatum.DownloadedPercentage))
    {
        Console.WriteLine($"Downloaded: {AppUpdater.DownloadedMegabytes} MB / {AppUpdater.DownloadTotalSizeMegabytes} MB  ({AppUpdater.DownloadedPercentage} %)");
    }
}
```

**Notes:**

- The frequency of progress change can be adjusted with: `DownloadProgressUpdateFrequencySeconds`
</details>

<details>
<summary>How to check for updates in a time basis</summary>

### Check for updates in a time basis

You can make use of built-in timer object: `AutoUpdateCheckTimer` and listen for `UpdateFound` event.

```csharp
AppUpdater.AutoUpdateCheckTimer.Interval = TimeSpan.FromHours(1).TotalMilliseconds;
AppUpdater.AutoUpdateCheckTimer.Start();
```
</details>