# [![Logo](https://raw.githubusercontent.com/sn4k3/Updatum/main/media/Updatum-32.png)](#) Updatum

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

## Auto updater strategy

You can opt to install the update manually or automatically.
If automatic, Updatum will:

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

<details>
<summary>Console output:</summary>


```
Checking for updates for sn4k3/UVtools
Updater state changed: CheckingForUpdate
Updater state changed: None
Update found: True

Changelog:
## v5.0.1

> Release date: 12/19/2024 06:56:33 +00:00
> Release diff: 1

- (Fix) Windows MSI: System.IO.Compression.Native is missing when upgrading from 4.4.3 (#957)
- (Fix) Index out of range when saving some file formats (#957)
- (Change) Exposure time finder: Allow using 0 for the normal step in the multiple exposure generator (#958)

---

## v5.0.2

> Release date: 12/19/2024 15:28:39 +00:00
> Release diff: 2

- (Fix) Remove a condition that prevents the new Anycubic file format from being used
- (Upgrade) AvaloniaUI from 11.1.3 to 11.2.2

---

## v5.0.3

> Release date: 12/28/2024 19:22:42 +00:00
> Release diff: 3

- Anycubic file format:
  - (Fix) Reset TSMC values to comply with globals when decoding file and AdvancedMode is disabled (#971)
  - (Fix) Setting the LiftHeight2 was setting the base value to BottomLiftHeight2
  - (Fix) Setting the BottomRetractSpeed was not applying the value in the base property
- Multiple exposure finder:
   - (Fix) Counter triangles not taking all the new left space
   - (Fix) When doing multiple heights the text label always show the base height
- (Improvement) Layer image viewer internal handling
- (Fix) Settings - Send to process: Unable to pick a process file, it was selecting folder instead
- (Fix) Save As can show incorrect file extension description when there are other file formats with the same extension

---

## v5.0.4

> Release date: 01/08/2025 02:45:34 +00:00
> Release diff: 4

- PCB Exposure:
  - (Fix) Polygon primitive vertex count not parsing correctly when having argument (#976)
  - (Fix) Obround aperture to follow the correct implementation (two semicircles connected by parallel lines tangent to their endpoints) (#976)
  - (Fix) Implement the "hole diameter" argument in all apertures (#976)
  - (Fix) Implement the "rotation" argument for the polygon aperture

---

## v5.0.5

> Release date: 01/09/2025 03:19:04 +00:00
> Release diff: 5

- (Add) PrusaSlicer printer: Elegoo Saturn 4 Ultra 16K
- (Improvement) Goo: Implement and support the tilting vat printers
- (Improvement) All shapes in pixel editor will now respect the non-equal pixel pitch and compensate the lower side to print a regular shape, this also affects the polygons on PCB exposure tool and other tools as well
- (Fix) PCB Exposure: Use raw polygons instead of angle aligned polygons to respect the gerber implementation (#976)

---

## v5.0.6

> Release date: 01/31/2025 02:21:16 +00:00
> Release diff: 6

- **PCB Exposure:**
  - (Fix) When importing gerber files via drag and drop to the main window the file was created with 0mm layer height and no exposure set
  - (Fix) Merging multiple gerber files with mirror active was mirroring the image in each draw causing the wrong output (#980)
  - (Fix) Excellon drill format does not load tools when they have spindle parameters [F/C] (#980)
  - (Fix) Excellon drill format to respect the integer and decimal digit count when specifying them (#980)
- **Stress Tower:**
  - (Improvement) Allow to pause and cancel the operation
  - (Improvement) Process layers in a more efficient way to reduce allocations and be able to produce the test without RAM hogging
- (Upgrade) .NET from 9.0.0 to 9.0.1
- (Upgrade) OpenCV from 4.9.0 to 4.10.0

---

## v5.0.7

> Release date: 02/15/2025 20:46:22 +00:00
> Release diff: 7

- **Layer previewer: (#990)**
  - (Add) Shortcuts: Ctrl/? + to zoom in and Ctrl/? - to zoom out in the layer previewer
  - (Add) Allow to horizontal scroll the image with the mouse dispacement buttons and/or wheel (Only for mouse with such buttons)
  - (Add) Hold Ctrl key while use the mouse wheel to vertical scroll the image instead of zoom
  - (Add) Zoom behavior: Zoom with pre-defined levels or native incremental zoom (Configurable in settings, default: Levels)
  - (Add) Zoom debounce time to prevent the zoom to be triggered multiple times when using a trackpad (Configurable in settings, default: 20ms)
- (Improvement) Linux: Show app icon on AppImage after integration with the desktop environment
- (Fix) Unable to find PrusaSlicer >= 2.9.0 on Linux (Flatpak folder change) (#1000)
- (Fix) PrusaSlicer: Anycubic Photon Mono M7 Max incorrect extension (#995)
- (Upgrade) .NET from 9.0.1 to 9.0.2
- (Upgrade) AvaloniaUI from 11.2.3 to 11.2.4

---

## v5.0.8

> Release date: 03/10/2025 04:05:17 +00:00
> Release diff: 8

- (Fix) Ignore "org.freedesktop.DBus.Error.ServiceUnknown" exception to prevent crash on Linux (#964)
- (Upgrade) AvaloniaUI from 11.2.3 to 11.2.4

---

## v5.0.9

> Release date: 04/04/2025 00:14:04 +00:00
> Release diff: 9

- (Add) PrusaSlicer printer: Elegoo Mars 5 Ultra (#1006)
- (Fix) Ignore the "org.freedesktop.DBus.Error.UnknownMethod" exception to prevent crash on Linux (#964)
- (Fix) Goo: Bad print when using tilting VAT printer (#1013)
- (Upgrade) .NET from 9.0.2 to 9.0.3
- (Upgrade) AvaloniaUI from 11.2.5 to 11.2.6

---

## v5.1.0

> Release date: 04/22/2025 01:40:17 +00:00
> Release diff: 10

- (Add) Pixel Arithmetic - Brightness Step: Mutates the initial brightness with a step that is added/subtracted to the current value dependent on the processed layer count (#1014)
- (Fix) Anycubic ZIP: Implement the missing fields from manifest file and allow to tune TSMC and regular global values (#1018)
- (Fix) Handle floating precision error when calculating the `PerLayerSettings` flag (#1013)
- (Fix) Linux: Pixel editor drawing cursor preview not visible (#1019)
- (Fix) Use `async Task` instead of `async void` where possible
- (Improvement) Use some refactorings for NET 9.0 features
- (Change) Compile openCV with lower linux requirement (#1015)
- (Upgrade) .NET from 9.0.3 to 9.0.4
- (Upgrade) AvaloniaUI from 11.2.6 to 11.3.0-beta2

---

## v5.1.1

> Release date: 05/09/2025 19:04:22 +00:00
> Release diff: 11

- (Fix) Anycubic ZIP: `System.InvalidOperationException: Sequence contains no elements` when having empty layers (#1023)
- (Improvement) CTB and GOO: Set all lift properties instead some of them for the tilting vat printers
- (Improvement) Convert most Linq to ZLinq
- (Upgrade) AvaloniaUI from 11.3.0-beta2 to 11.3.0

---


Do you want to download the v5.1.1 update? (y/yes/n/no)
y
Downloading UVtools_win-x64_v5.1.1.msi...
Updater state changed: DownloadingUpdate
Downloaded: 0 MB / 78.16 MB  (0 %)
Downloaded: 20.01 MB / 78.16 MB  (25.6 %)
Downloaded: 40.8 MB / 78.16 MB  (52.2 %)
Downloaded: 61.66 MB / 78.16 MB  (78.89 %)
Downloaded: 78.16 MB / 78.16 MB  (100 %)
Updater state changed: None
Download finished: C:\Users\tiago\AppData\Local\Temp\UVtools_win-x64_v5.1.1.msi
Do you want to install the update? (y/yes/n/no)
```

</details>

## Usage


```csharp
// Create an instance of UpdatumManager, keep it global and single instance.
// By default it will fetch your current version from Assembly.GetEntryAssembly().GetName().Version
// If you want to be safe and strict pass the current version, you can set 3rd argument as: Assembly.GetExecutingAssembly().GetName().Version
internal static readonly UpdatumManager AppUpdater = new("sn4k3", "UVtools")
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

## Screenshots


![UVtools screenshot](https://raw.githubusercontent.com/sn4k3/Updatum/main/media/assets/UVtools_screenshot.png)
![NetSonar screenshot 1](https://raw.githubusercontent.com/sn4k3/Updatum/main/media/assets/NetSonar_screenshot_no_updates.png)
![NetSonar screenshot 2](https://raw.githubusercontent.com/sn4k3/Updatum/main/media/assets/NetSonar_screenshot_update_found.png)
![NetSonar screenshot 3](https://raw.githubusercontent.com/sn4k3/Updatum/main/media/assets/NetSonar_screenshot_update_changelog.png)
![NetSonar screenshot 4](https://raw.githubusercontent.com/sn4k3/Updatum/main/media/assets/NetSonar_screenshot_update_download.png)

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
    if (e.PropertyName == nameof(UpdatumManager.DownloadedPercentage))
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