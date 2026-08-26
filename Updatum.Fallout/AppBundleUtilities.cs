using System;

namespace Updatum.Fallout;

/// <summary>
/// Provides utility methods for handling application bundles, including string escaping and quoting for various contexts such as Bash, YAML, and shell commands, as well as validation of required string values.
/// </summary>
public static class AppBundleUtilities
{
    /// <summary>
    /// Escapes a string for use in a Bash double-quoted context, handling special characters such as backslashes, double quotes, dollar signs, and backticks.
    /// </summary>
    /// <param name="value">The string to escape.</param>
    /// <returns>The escaped string.</returns>
    public static string EscapeBashDoubleQuoted(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal);
    }

    /// <summary>
    /// Quotes a string for use in a YAML context, escaping single quotes by doubling them.
    /// </summary>
    /// <param name="value">The string to quote.</param>
    /// <returns>The quoted string.</returns>
    public static string QuoteYaml(string value)
    {
        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    /// <summary>
    /// Quotes a string for use in a shell context, escaping single quotes by closing the quote, adding an escaped single quote, and reopening the quote.
    /// </summary>
    /// <param name="value">The string to quote.</param>
    /// <returns>The quoted string.</returns>
    public static string QuoteShell(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    /// <summary>
    /// Validates that a string value is not null, empty, or whitespace. Throws an ArgumentException if the validation fails.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="parameterName">The name of the parameter being validated.</param>
    /// <exception cref="ArgumentException">Thrown if the value is null, empty, or whitespace.</exception>
    public static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be null, empty, or white space.", parameterName);
        }
    }
}