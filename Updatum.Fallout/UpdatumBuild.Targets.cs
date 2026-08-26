using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using Serilog;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// Prints the build variables to the console. This target is useful for debugging and verifying the build configuration.
    /// </summary>
    public virtual Target Print => d => d
        .Executes(() =>
        {
            foreach (var variable in GetPrintVariables())
                Log.Information("{Name} = {Value}", variable.Key, variable.Value);
        });

    /// <summary>
    /// Cleans the build artifacts and directories.
    /// </summary>
    public virtual Target Clean => d => d
        .Before(Restore)
        .Executes(() =>
        {
            DotNetClean();
            ArtifactsDirectory.DeleteDirectory();
        });

    /// <summary>
    /// Restores the NuGet packages for the main project.
    /// </summary>
    public virtual Target Restore => d => d
        .Executes(() =>
        {
            DotNetRestore(options => options
                .SetProjectFile(MainProject)
            );
        });

    /// <summary>
    /// Runs the main project after restoring dependencies. This target depends on the Restore target and any additional targets specified in DependOnTargets.
    /// </summary>
    public virtual Target Compile => d => d
        .DependsOn(Restore)
        .DependsOn(DependOnTargets)
        .Executes(() =>
        {
            DotNetBuild(options => options
                .SetProjectFile(MainProject)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
            );
        });

    /// <summary>
    /// Executes the 'Run' target, which runs the main project after restoring dependencies and compiling it.
    /// </summary>
    public virtual Target Run => d => d
        .DependsOn(Compile)
        .DependsOn(DependOnTargets)
        .Executes(() =>
        {
            DotNetRun(options => options
                .SetProjectFile(MainProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
            );
        });
}