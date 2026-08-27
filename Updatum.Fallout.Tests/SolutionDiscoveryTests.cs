using System;
using System.IO;
using Fallout.Common.IO;
using Xunit;

namespace Updatum.Fallout.Tests;

/// <summary>
/// Verifies automatic solution discovery and loading.
/// </summary>
public class SolutionDiscoveryTests
{
    [Fact]
    public void Solution_SlnxAndSlnExist_PrefersSlnxAndCachesResult()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var slnxPath = Path.Combine(directory, "Preferred.slnx");
            File.WriteAllText(slnxPath, "<Solution />");
            File.WriteAllText(Path.Combine(directory, "Fallback.sln"), CreateEmptySln());
            var build = new TestBuild((AbsolutePath)directory);

            var solution = build.Solution;

            Assert.Equal(Path.GetFullPath(slnxPath), solution.Path);
            Assert.Same(solution, build.Solution);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Solution_OnlySlnExists_LoadsSln()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var slnPath = Path.Combine(directory, "Fallback.sln");
            File.WriteAllText(slnPath, CreateEmptySln());

            var solution = new TestBuild((AbsolutePath)directory).Solution;

            Assert.Equal(Path.GetFullPath(slnPath), solution.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Solution_NoSolutionExists_ThrowsDescriptiveException()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                _ = new TestBuild((AbsolutePath)directory).Solution);

            Assert.Contains("No .slnx or .sln file was found", exception.Message, StringComparison.Ordinal);
            Assert.Contains(directory, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Solution_MultipleSlnxFilesExist_ThrowsDescriptiveException()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "One.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(directory, "Two.slnx"), "<Solution />");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                _ = new TestBuild((AbsolutePath)directory).Solution);

            Assert.Contains("Multiple .slnx files were found", exception.Message, StringComparison.Ordinal);
            Assert.Contains("One.slnx", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Two.slnx", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"updatum-solution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateEmptySln() =>
        """
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Global
        EndGlobal
        """;

    private sealed class TestBuild(AbsolutePath solutionDirectory) : UpdatumBuild
    {
        protected override AbsolutePath SolutionSearchDirectory => solutionDirectory;
    }
}
