using System;
using System.Text;

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
    /// Quotes a string for use as a single argument in a process argument line, using the escaping rules the
    /// .NET process launcher applies (double quotes, with backslashes doubled before a quote).
    /// </summary>
    /// <param name="value">The argument to quote.</param>
    /// <returns>The quoted argument.</returns>
    public static string QuoteProcessArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    backslashes++;
                    break;
                case '"':
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    break;
                default:
                    builder.Append('\\', backslashes).Append(character);
                    backslashes = 0;
                    break;
            }
        }

        return builder.Append('\\', backslashes * 2).Append('"').ToString();
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