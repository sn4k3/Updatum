using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Persistence.Solution.Serializer;
using Fallout.Solutions;
using Serilog;

namespace Updatum.Fallout;

public partial class UpdatumBuild
{
    /// <summary>
    /// The time budget for one <see cref="DownloadFile"/> call, covering the response body and not just the
    /// response headers.
    /// </summary>
    protected static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    private static readonly HttpClient Client = new()
    {
        Timeout = DownloadTimeout
    };

    /// <summary>
    /// Deletes a file-system entry regardless of whether it is a regular file or directory.
    /// </summary>
    /// <param name="path">The file-system entry path.</param>
    protected virtual void DeleteFileSystemEntry(AbsolutePath path)
    {
        path.DeleteFile();
        path.DeleteDirectory();
    }

    /// <summary>
    /// Downloads a file to a protected local path.
    /// </summary>
    /// <param name="url">The source URL.</param>
    /// <param name="destination">The destination file path.</param>
    protected virtual void DownloadFile(string url, AbsolutePath destination)
    {
        using var response = Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var source = response.Content.ReadAsStream();
        using var target = File.Create(destination);
        source.CopyTo(target);
    }

    /// <summary>
    /// Executes a shell command in the specified working directory.
    /// </summary>
    /// <param name="command">The shell command.</param>
    /// <param name="workingDirectory">The command working directory.</param>
    protected virtual void ExecuteShell(string command, AbsolutePath workingDirectory)
    {
        using var process = ProcessTasks.StartShell(command, workingDirectory);
        process.AssertWaitForExit();
    }

    /// <summary>
    /// Determines whether the specified project is a runnable project based on its output type.
    /// </summary>
    /// <param name="project">The project to check.</param>
    /// <returns><c>true</c> if the project is runnable; otherwise, <c>false</c>.</returns>
    protected bool IsRunnableProject(Project project)
    {
        return GetProjectProperty(project, "OutputType") is "Exe" or "WinExe";
    }

    private static AbsolutePath FindSolutionFile(AbsolutePath directory)
    {
        if (!directory.DirectoryExists())
            throw new InvalidOperationException($"Solution search directory '{directory}' does not exist.");

        var slnxFiles = FindSolutionFiles(directory, "*.slnx");
        if (slnxFiles.Length > 0)
            return GetSingleSolutionFile(directory, ".slnx", slnxFiles);

        var slnFiles = FindSolutionFiles(directory, "*.sln");
        if (slnFiles.Length > 0)
            return GetSingleSolutionFile(directory, ".sln", slnFiles);

        throw new InvalidOperationException($"No .slnx or .sln file was found in '{directory}'.");
    }

    private static AbsolutePath[] FindSolutionFiles(AbsolutePath directory, string searchPattern)
    {
        return directory.GetFiles(searchPattern)
            .OrderBy(path => path.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AbsolutePath GetSingleSolutionFile(
        AbsolutePath directory,
        string extension,
        AbsolutePath[] files)
    {
        if (files.Length == 1)
            return files[0];

        var fileNames = string.Join(", ", files.Select(path => path.Name));
        throw new InvalidOperationException(
            $"Multiple {extension} files were found in '{directory}': {fileNames}.");
    }

    private static Solution LoadSolution(AbsolutePath solutionFile)
    {
        var serializer = SolutionSerializers.Serializers.FirstOrDefault(candidate =>
            candidate.IsSupported(solutionFile));
        if (serializer is null)
            throw new InvalidOperationException($"No Fallout serializer supports solution file '{solutionFile}'.");

        var model = serializer.OpenAsync(solutionFile, CancellationToken.None).GetAwaiter().GetResult();
        return new Solution(model, solutionFile);
    }

    /// <summary>
    /// Gets the configured runtime-cache file name from the main project.
    /// </summary>
    /// <returns>The configured file name, or <see langword="null"/> when it is not defined.</returns>
    protected virtual string? GetBuildRuntimeCacheFileName()
    {
        return GetMainProjectProperty(nameof(BuildRuntimeCacheFileName));
    }

    /// <summary>
    /// Gets an evaluated property from the main project.
    /// </summary>
    /// <param name="propertyName">The MSBuild property name.</param>
    /// <returns>The evaluated property value, or <see langword="null"/> when it is not defined.</returns>
    protected virtual string? GetMainProjectProperty(string propertyName)
    {
        return GetProjectProperty(MainProject, propertyName);
    }

    /// <summary>
    /// Gets an evaluated MSBuild property from a solution project.
    /// </summary>
    /// <param name="project">The project to evaluate.</param>
    /// <param name="propertyName">The MSBuild property name.</param>
    /// <returns>The evaluated value, or <see langword="null"/> when the property is not defined.</returns>
    protected string? GetProjectProperty(Project project, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        try
        {
            return GetProjectPropertyInProcess(project, propertyName);
        }
        catch (Exception exception) when (IsNuGetFrameworksLoadFailure(exception))
        {
            try
            {
                return GetProjectPropertyFromMSBuild(project, propertyName);
            }
            catch (Exception msBuildException) when (IsNuGetFrameworksLoadFailure(msBuildException))
            {
                if (!_loggedProjectEvaluationFallback)
                {
                    Log.Warning(
                        "Fallout could not evaluate MSBuild properties in process because NuGet.Frameworks conflicts with the active SDK. Falling back to dotnet msbuild.");
                    _loggedProjectEvaluationFallback = true;
                }

                return GetExternallyEvaluatedProjectProperties(project, propertyName)
                    .GetValueOrDefault(propertyName);
            }
        }
    }

    /// <summary>
    /// Gets an MSBuild property through Fallout's in-process project evaluator.
    /// </summary>
    /// <param name="project">The project to evaluate.</param>
    /// <param name="propertyName">The MSBuild property name.</param>
    /// <returns>The evaluated value, or <see langword="null"/> when the property is not defined.</returns>
    protected virtual string? GetProjectPropertyInProcess(Project project, string propertyName)
    {
        return project.GetProperty(propertyName);
    }

    /// <summary>
    /// Gets an MSBuild property through Fallout's MSBuild project evaluator.
    /// </summary>
    /// <param name="project">The project to evaluate.</param>
    /// <param name="propertyName">The MSBuild property name.</param>
    /// <returns>The evaluated value, or <see langword="null"/> when the property is not defined.</returns>
    protected virtual string? GetProjectPropertyFromMSBuild(Project project, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        return project.GetMSBuildProject().GetProperty(propertyName)?.EvaluatedValue;
    }

    private IReadOnlyDictionary<string, string?> GetExternallyEvaluatedProjectProperties(
        Project project,
        string requestedPropertyName)
    {
        var projectPath = project.Path;
        if (_evaluatedProjectProperties.TryGetValue(projectPath, out var cachedProperties) &&
            cachedProperties.ContainsKey(requestedPropertyName))
            return cachedProperties;

        var propertyNames = MainProjectPropertyNames
            .Append("OutputType")
            .Append(requestedPropertyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evaluatedProperties = EvaluateProjectProperties(project, propertyNames);
        var mergedProperties = cachedProperties is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(cachedProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var property in evaluatedProperties)
            mergedProperties[property.Key] = property.Value;

        _evaluatedProjectProperties[projectPath] = mergedProperties;
        return mergedProperties;
    }

    /// <summary>
    /// Evaluates MSBuild properties in an isolated <c>dotnet msbuild</c> process.
    /// </summary>
    /// <param name="project">The project to evaluate.</param>
    /// <param name="propertyNames">The property names to retrieve.</param>
    /// <returns>The evaluated property values.</returns>
    protected virtual IReadOnlyDictionary<string, string?> EvaluateProjectProperties(
        Project project,
        IReadOnlyCollection<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(propertyNames);
        if (propertyNames.Count == 0)
            throw new ArgumentException("At least one property name is required.", nameof(propertyNames));

        var startInfo = new ProcessStartInfo("dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = project.Path.Parent
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(project.Path);
        startInfo.ArgumentList.Add($"-getProperty:{string.Join(',', propertyNames)}");
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start dotnet msbuild.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet msbuild could not evaluate '{project.Path}'.{Environment.NewLine}{error.Trim()}");

        using var document = JsonDocument.Parse(output);
        var properties = document.RootElement.GetProperty("Properties");
        return propertyNames.ToDictionary(
            propertyName => propertyName,
            propertyName => properties.TryGetProperty(propertyName, out var value) ? value.GetString() : null,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsNuGetFrameworksLoadFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("NuGet.Frameworks", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("manifest definition does not match", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the specified project is excluded based on the list of excluded project name tokens.
    /// </summary>
    /// <param name="project">The project to check.</param>
    /// <returns><c>true</c> if the project is excluded; otherwise, <c>false</c>.</returns>
    protected bool IsExcludedByName(Project project)
    {
        var tokens = project.Name
            .Split(['.', '-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);

        return ExcludedProjectNameTokens.Any(excluded =>
            tokens.Any(token =>
                string.Equals(token, excluded, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Throws an exception if the specified property is missing from the main project or solution properties.
    /// </summary>
    /// <param name="value">The value of the property to check.</param>
    /// <param name="propertyName">The name of the property to check.</param>
    /// <exception cref="InvalidOperationException">The property could not be determined.</exception>
    protected void ThrowIfMissingProperty([NotNull] string? value,
        [CallerMemberName] string propertyName = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Property '{propertyName}' could not be determined from the '{MainProject.Name}' main project or solution properties. Please define it in the project file or implement the property.");
    }
}
