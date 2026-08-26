using Fallout.Common.IO;
using Serilog;
using Updatum.Fallout;

namespace Updatum.Build;

sealed class Build : UpdatumBuild
{
    public Build()
    {
        BeforePublishRid = context =>
            Log.Information("Publishing {Rid} to {Path}",
                context.RuntimeIdentifier, context.PublishPath);

        // In this case, remove these tokens to MainProject to detect
        ExcludedProjectNameTokens.Remove("fake");
        ExcludedProjectNameTokens.Remove("fakeapp");

        LinuxAppBundleOptions.AppRunScriptBeforeExec = $$"""
                                                         function help() {
                                                             echo '         _       _'                   
                                                             echo ' /\ /\ _ __   __| | __ _| |_ _   _ _ __ ___ '
                                                             echo '/ / \ \ '_ \ / _` |/ _` | __| | | | '_ ` _ \'
                                                             echo '\ \_/ / |_) | (_| | (_| | |_| |_| | | | | | |'
                                                             echo ' \___/| .__/ \__,_|\__,_|\__|\__,_|_| |_| |_|'
                                                             echo '      |_|'
                                                                   
                                                             echo "
                                                          --------------------------------------------------------------------------
                                                             All the great {{SoftwareName}} functionality inside an AppImage package.
                                                          --------------------------------------------------------------------------
                                                          (This package uses the AppImage software packaging technology for Linux
                                                           ['One App == One File'] for easy availability of the newest {{SoftwareName}}
                                                           releases across all major Linux distributions.)
                                                          Usage:  --help, -h
                                                          ------     # This message
                                                                  --appimage-extract
                                                                     # Unpack this AppImage into a local sub-directory [currently named 'squashfs-root']
                                                                  --appimage-help
                                                                     # Show available AppImage options
                                                         "
                                                         }

                                                         if [ "$1" == "--help" -o "$1" == "-h" ]; then
                                                             help
                                                             exit $?
                                                         fi

                                                         """;
    }

    public override AbsolutePath MacOSIconFile => MediaDirectory / $"{SolutionName}.icns";

    public override AbsolutePath LinuxIconFile => MediaDirectory / $"{SolutionName}.svg";

    /// <summary>
    /// Gets the FakeApp product name, which differs from the solution name.
    /// </summary>
    public override string SoftwareName => MainProject.Name;

    /// <summary>
    /// Executes the build script. This is the entry point for the build process.
    /// </summary>
    /// <returns>The exit code of the build process.</returns>
    public new static int Main() => Execute<Build>(x => x.Compile);
}
