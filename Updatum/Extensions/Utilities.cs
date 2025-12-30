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
#if DEBUG
public
#else
internal
#endif
    static class Utilities
{
    /// <summary>
    /// Gets the unix file mode for 775 permissions.
    /// </summary>
    public const UnixFileMode Unix755FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
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

    private const byte ObfuscationKey = 0xA5;

    // Version Info Keywords (lower case)
    private static readonly byte[][] FileInfoSignatureBytes =
    [
        [0xD6, 0xC0, 0xD1, 0xD0, 0xD5], // setup
        [0xCC, 0xCB, 0xD6, 0xD1, 0xC4, 0xC9, 0xC9], // install
        [0xCC, 0xCB, 0xCB, 0xCA], // inno
        [0xCB, 0xD0, 0xC9, 0xC9, 0xD6, 0xCA, 0xC3, 0xD1], // nullsoft
        [0xCB, 0xD6, 0xCC, 0xD6], // nsis
        [0xD2, 0xCC, 0xDD] // wix
    ];

    // File Signatures
    private static readonly byte[][] FileSignatureBytes =
    [
        [0xEC, 0xCB, 0xCB, 0xCA, 0x85, 0xF6, 0xC0, 0xD1, 0xD0, 0xD5], // Inno Setup
        [0xEF, 0xF7, 0x8B, 0xEC, 0xCB, 0xCB, 0xCA, 0x8B, 0xF6, 0xC0, 0xD1, 0xD0, 0xD5], // JR.Inno.Setup
        [0xEB, 0xD0, 0xC9, 0xC9, 0xD6, 0xCA, 0xC3, 0xD1, 0xEC, 0xCB, 0xD6, 0xD1], // NullsoftInst
        [0xEB, 0xD0, 0xC9, 0xC9, 0xD6, 0xCA, 0xC3, 0xD1], // Nullsoft
        [0xF2, 0xCC, 0xCB, 0xC1, 0xCA, 0xD2, 0xD6, 0x85, 0xEC, 0xCB, 0xD6, 0xD1, 0xC4, 0xC9, 0xC9, 0xC0, 0xD7], // Windows Installer
        [0xEC, 0xCB, 0xD6, 0xD1, 0xC4, 0xC9, 0xC9, 0xF6, 0xCD, 0xCC, 0xC0, 0xC9, 0xC1], // InstallShield
        [0xE4, 0xC1, 0xD3, 0xC4, 0xCB, 0xC6, 0xC0, 0xC1, 0x85, 0xEC, 0xCB, 0xD6, 0xD1, 0xC4, 0xC9, 0xC9, 0xC0, 0xD7], // Advanced Installer
        [0xF2, 0xCC, 0xD6, 0xC0, 0x85, 0xEC, 0xCB, 0xD6, 0xD1, 0xC4, 0xC9, 0xC9, 0xC4, 0xD1, 0xCC, 0xCA, 0xCB], // Wise Installation
        [0xF6, 0xC0, 0xD1, 0xD0, 0xD5, 0x85, 0xE3, 0xC4, 0xC6, 0xD1, 0xCA, 0xD7, 0xDC], // Setup Factory
        [0xEB, 0xF6, 0xEC, 0xF6], // NSIS
        //[0xF2, 0xCC, 0xFD] // WiX
    ];

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
    public static bool IsWindowsInstallerFile(string? filePath)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!File.Exists(filePath)) return false;
        if (filePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return true;
        if (!filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            // First, check version info comments and description for installer keywords
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            var fileCommentsDescription = $"{versionInfo.Comments}{versionInfo.FileDescription}";
            if (!string.IsNullOrWhiteSpace(fileCommentsDescription))
            {
                var span = fileCommentsDescription.AsSpan();
                foreach (var bytes in FileInfoSignatureBytes)
                {
                    if (HasEncodedSignature(span, bytes)) return true;
                }
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
                foreach (var signatureByte in FileSignatureBytes)
                {
                    if (HasEncodedSignature(span, signatureByte)) return true;
                }
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
    /// Determines whether the specified data contains the given encoded signature after decoding.
    /// </summary>
    /// <param name="data">The data to search for the decoded signature within.</param>
    /// <param name="encodedSignature">The encoded signature to decode and search for in the data. Each byte is expected to be obfuscated and will be
    /// decoded before comparison.</param>
    /// <returns>true if the decoded signature is found within the data; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasEncodedSignature(ReadOnlySpan<byte> data, ReadOnlySpan<byte> encodedSignature)
    {
        if (data.Length < encodedSignature.Length) return false;

        Span<byte> decoded = stackalloc byte[encodedSignature.Length];
        for (var i = 0; i < encodedSignature.Length; i++)
        {
            decoded[i] = (byte)(encodedSignature[i] ^ ObfuscationKey);
        }

        return data.IndexOf(decoded) >= 0;
    }

    /// <summary>
    /// Determines whether the specified character span contains the given encoded signature after decoding.
    /// </summary>
    /// <remarks>The encoded signature is decoded using an internal obfuscation key before performing the
    /// search. The method returns false if the data span is shorter than the encoded signature.</remarks>
    /// <param name="data">The span of characters to search for the decoded signature.</param>
    /// <param name="encodedSignature">The encoded signature as a span of bytes. Each byte will be decoded before searching.</param>
    /// <returns>true if the decoded signature is found within the data span; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool HasEncodedSignature(ReadOnlySpan<char> data, ReadOnlySpan<byte> encodedSignature)
    {
        if (data.Length < encodedSignature.Length) return false;

        Span<char> decoded = stackalloc char[encodedSignature.Length];
        for (var i = 0; i < encodedSignature.Length; i++)
        {
            decoded[i] = (char)(encodedSignature[i] ^ ObfuscationKey);
        }

        return data.IndexOf(decoded) >= 0;
    }

    /// <summary>
    /// Decodes a UTF-8 encoded string from a span of bytes that have been obfuscated with a predefined key.
    /// </summary>
    /// <remarks>Each byte in the input span is de-obfuscated using a bitwise XOR with a predefined key before
    /// decoding. The method assumes the original data was UTF-8 encoded and obfuscated using the same key.</remarks>
    /// <param name="encoded">A read-only span of bytes representing the obfuscated UTF-8 encoded string to decode.</param>
    /// <returns>A string containing the decoded text represented by the input span.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string GetDecodedString(ReadOnlySpan<byte> encoded)
    {
        Span<byte> decoded = stackalloc byte[encoded.Length];
        for (var i = 0; i < encoded.Length; i++)
        {
            decoded[i] = (byte)(encoded[i] ^ ObfuscationKey);
        }
        return Encoding.UTF8.GetString(decoded);
    }

    /// <summary>
    /// Encodes a string using the obfuscation key.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte[] GetEncodedBytes(string? value)
    {
        if (value is null) return [];
        var bytes = Encoding.UTF8.GetBytes(value);
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(bytes[i] ^ ObfuscationKey);
        }
        return bytes;
    }

    /// <summary>
    /// Bash-safe one-line string literal that preserves special characters (including newlines)
    /// using ANSI-C quoting: $'...'
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    internal static string BashAnsiCString(object? value)
    {
        if (value is null) return "$''";

        if (value is not string strValue)
        {
            strValue = value.ToString() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(strValue)) return "$''";


        if (strValue.Contains('\0'))
            throw new ArgumentException("Bash strings cannot contain NUL (\\0).");

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
    internal static string BatchSetValue(object? value)
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
    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}