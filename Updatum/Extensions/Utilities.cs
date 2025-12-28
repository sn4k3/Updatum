using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Updatum.Extensions;

/// <summary>
/// Provides utility methods and constants.
/// </summary>
internal static class Utilities
{
    /// <summary>
    /// Gets the unix file mode for 775 permissions.
    /// </summary>
    public const UnixFileMode Unix755FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute  |
                                                UnixFileMode.GroupRead                         | UnixFileMode.GroupExecute |
                                                UnixFileMode.OtherRead                         | UnixFileMode.OtherExecute;
    /// <summary>
    /// Gets the default applications directory for linux.
    /// </summary>
    public static string LinuxDefaultApplicationDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");

    /// <summary>
    /// Gets the default applications directory for macOS.
    /// </summary>
    public const string MacOSDefaultApplicationDirectory = "/Applications";

    /// <summary>
    /// Gets the default applications directory that is common to all systems if the others are not available.
    /// </summary>
    public static string CommonDefaultApplicationDirectory => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// Starts a process with the given name and arguments.
    /// </summary>
    /// <param name="name">The name of the process to start.</param>
    /// <param name="arguments">The arguments to pass to the process.</param>
    /// <param name="waitForCompletion">True to wait for the process to complete.</param>
    /// <param name="waitTimeout">The timeout in milliseconds to wait for the process to complete.</param>
    /// <returns>The exit code of the process.</returns>
    public static int StartProcess(string name, string? arguments, bool waitForCompletion = false, int waitTimeout = Timeout.Infinite)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(name, arguments ?? string.Empty) { UseShellExecute = true });
            if (process is null) return -1;
            if (waitForCompletion)
            {
                process.WaitForExit(waitTimeout);
                return process.ExitCode;
            }
            return 0;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            return -1;
        }
    }

    /// <summary>
    /// Gets a temporary folder with the given name.
    /// </summary>
    /// <param name="name">Child folder name</param>
    /// <param name="init">True to delete existence directory and create a new.</param>
    /// <returns>The path to the temporary folder.</returns>
    public static string GetTemporaryFolder(string name, bool init = false)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), name);

        // Delete if it was a file
        if (File.Exists(name))
            File.Delete(name);

        if (init)
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, true); // Delete existing directory

            Directory.CreateDirectory(tmpDir); // Create new directory
        }

        return tmpDir;
    }

    /// <summary>
    /// Determines whether the specified file is a Windows setup or installer executable based on common installer
    /// signatures.
    /// </summary>
    /// <param name="filePath">The full path to the file to examine. The file must exist and have a ".exe" extension. This parameter cannot be
    /// null or empty.</param>
    /// <remarks>This method checks for known installer signatures such as Inno Setup, NSIS, InstallShield,
    /// WiX, and others within the specified executable file. Only Windows PE executables are considered. The method
    /// returns false if the file does not exist, is not a valid Windows executable, or does not contain recognizable
    /// installer signatures. The check is case-insensitive and scans both the beginning and end of large files for
    /// efficiency.</remarks>
    /// <returns>true if the file is recognized as a setup or installer executable; otherwise, false.</returns>
    public static bool IsWindowsInstallerFile(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!File.Exists(filePath)) return false;
        if (filePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return true;
        if (!filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            // First, check version info comments and description for installer keywords
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            if (versionInfo.Comments is not null)
            {
                ReadOnlySpan<string> searchWords =
                [
                    "setup",
                    "install",
                    "inno",
                    "nullsoft",
                    "nsis",
                    "wix",
                ];


#if NET10_0_OR_GREATER
                var searchValues = SearchValues.Create(searchWords, StringComparison.OrdinalIgnoreCase);
              //  if (!string.IsNullOrWhiteSpace(versionInfo.Comments) && versionInfo.Comments.ContainsAny(searchValues)) return true;
              //  if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription) && versionInfo.FileDescription.ContainsAny(searchValues)) return true;
#elif NET9_0_OR_GREATER
                // .NET 9 supports SearchValues<string>, but the "string.ContainsAny(SearchValues<string>)"
                // convenience is .NET 10+. Use the Span-based API instead.
                // We could use this branch for .NET 10+ as well, but keeping separate for clarity.
                var searchValues = SearchValues.Create(searchWords, StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(versionInfo.Comments) && versionInfo.Comments.AsSpan().ContainsAny(searchValues)) return true;
                if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription) && versionInfo.FileDescription.AsSpan().ContainsAny(searchValues)) return true;
#elif NET8_0_OR_GREATER
                // .NET 8 doesn't have SearchValues<string> substring search. Fallback to a loop.
                if (!string.IsNullOrWhiteSpace(versionInfo.Comments) && ContainsAnyOrdinalIgnoreCase(versionInfo.Comments, searchWords)) return true;
                if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription) && ContainsAnyOrdinalIgnoreCase(versionInfo.FileDescription, searchWords)) return true;

                static bool ContainsAnyOrdinalIgnoreCase(string haystack, ReadOnlySpan<string> needles)
                {
                    foreach (var needle in needles)
                    {
                        if (!string.IsNullOrWhiteSpace(needle) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
                    }

                    return false;
                }
#endif
            }

            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                options: FileOptions.SequentialScan);

            using var reader = new BinaryReader(stream, Encoding.UTF8, true);

            // Check for valid DOS header (MZ)
            if (stream.Length < 64) return false;
            var dosSignature = reader.ReadUInt16();
            if (dosSignature != 0x5A4D) return false; // "MZ"

            // Read PE offset from DOS header at offset 0x3C
            stream.Seek(0x3C, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();

            if (peOffset < 0 || peOffset + 4 > stream.Length) return false;

            // Verify PE signature
            stream.Seek(peOffset, SeekOrigin.Begin);
            var peSignature = reader.ReadUInt32();
            if (peSignature != 0x00004550) return false; // "PE\0\0"

            // Read the entire file to search for installer signatures
            stream.Seek(0, SeekOrigin.Begin);

            // For large files, only scan first 2MB and last 1MB where signatures typically reside
            const int headSize = 2 * 1024 * 1024;
            const int tailSize = 1 * 1024 * 1024;
            var bufferSize = (int)Math.Min(stream.Length, headSize + tailSize);

            var rented = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                var span = rented.AsSpan(0, bufferSize);

                if (stream.Length <= headSize + tailSize)
                {
                    stream.ReadExactly(span);
                }
                else
                {
                    stream.ReadExactly(span[..headSize]);
                    stream.Seek(-tailSize, SeekOrigin.End);
                    stream.ReadExactly(span.Slice(headSize, tailSize));
                }

                // Check installer signatures (ordered by likelihood)
                // Inno Setup - most common
                if (span.IndexOf("Inno Setup"u8) >= 0 ||
                    //span.IndexOf("inno"u8) >= 0 ||
                    span.IndexOf("JR.Inno.Setup"u8) >= 0)
                {
                    return true;
                }

                // NSIS
                if (span.IndexOf("Nullsoft"u8) >= 0 ||
                    span.IndexOf("NSIS"u8) >= 0 ||
                    span.IndexOf("NullsoftInst"u8) >= 0)
                {
                    return true;
                }

                // InstallShield
                if (span.IndexOf("InstallShield"u8) >= 0) return true;

                // WiX / Windows Installer
                if (span.IndexOf("WiX"u8) >= 0 ||
                    span.IndexOf("Windows Installer"u8) >= 0)
                {
                    return true;
                }

                // Advanced Installer
                if (span.IndexOf("Advanced Installer"u8) >= 0) return true;

                // Wise Installation
                if (span.IndexOf("Wise Installation"u8) >= 0) return true;

                // Setup Factory
                if (span.IndexOf("Setup Factory"u8) >= 0) return true;

                // Generic setup indicators
                //if (span.IndexOf("Setup"u8) >= 0 &&
                //    (span.IndexOf("Installer"u8) >= 0 || span.IndexOf("Installation"u8) >= 0))
                //{
                //    return true;
                //}
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch
        {
            // ignored
        }

        return false;
    }

    /// <summary>
    /// Bash-safe one-line string literal that preserves special characters (including newlines)
    /// using ANSI-C quoting: $'...'
    /// </summary>
    public static string BashAnsiCString(object? value)
    {
        if (value is null) return "$''";

        if (value is not string strValue)
        {
            strValue = value.ToString() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(strValue)) return "$''";


        if (strValue.Contains('\0'))
            throw new ArgumentException("Bash strings cannot contain NUL (\\0).", nameof(strValue));

        // Pre-calculate approximate capacity (most chars don't need escaping)
        var sb = new StringBuilder(strValue.Length + strValue.Length / 4 + 3);
        sb.Append("$'");

        foreach (var ch in strValue)
        {
            switch (ch)
            {
                case '\\': sb.Append(@"\\"); break;
                case '\'': sb.Append(@"\'"); break;
                case '\n': sb.Append(@"\n"); break;
                case '\r': sb.Append(@"\r"); break;
                case '\t': sb.Append(@"\t"); break;
                case '\b': sb.Append(@"\b"); break;
                case '\f': sb.Append(@"\f"); break;
                case var _ when ch < 0x20 || ch == 0x7F:
                    // Escape control chars and DEL as \xHH
                    sb.Append(@"\x");
                    sb.Append(HexDigit(ch >> 4));
                    sb.Append(HexDigit(ch & 0xF));
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        sb.Append('\'');
        return sb.ToString();
    }

    /// <summary>
    /// Batch-safe value for: set "VAR=value"
    /// Notes:
    /// - Encodes CR/LF as "\n" (two characters) to keep the set command single-line and safe.
    /// - Escapes: ^, %, !, "
    ///   - ! is escaped to survive when Delayed Expansion is ON.
    /// </summary>
    public static string BatchSetValue(object? value)
    {
        if (value is null) return string.Empty;

        if (value is not string strValue)
        {
            strValue = value.ToString() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(strValue)) return strValue;

        // Pre-calculate capacity accounting for worst-case escaping
        var sb = new StringBuilder(strValue.Length + strValue.Length / 2);

        foreach (var ch in strValue)
        {
            switch (ch)
            {
                case '\r':
                    // Skip standalone CR or CR in CRLF (next iteration handles \n)
                    break;
                case '\n':
                    sb.Append(@"\n");
                    break;
                case '^':
                    sb.Append("^^");
                    break;
                case '%':
                    sb.Append("%%");
                    break;
                case '!':
                    sb.Append("^!");
                    break;
                case '"':
                    sb.Append("^\"");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char HexDigit(int value)
        => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}