using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace CadMax.AutoCAD.Plugin;

/// <summary>Result of loading the machine-local bridge token without exposing its value.</summary>
public sealed record BridgeTokenLoadResult(
    BridgeTokenCredential? Credential,
    string? SafeErrorCode)
{
    public bool Success => Credential is not null && SafeErrorCode is null;

    public static BridgeTokenLoadResult Failure(string safeErrorCode) =>
        new(null, safeErrorCode);
}

/// <summary>Loads a protected token before any listener is created.</summary>
public interface IBridgeTokenSource
{
    BridgeTokenLoadResult Load();
}

/// <summary>
/// A disposable 256-bit Bearer credential. Authentication always compares fixed-size decoded
/// buffers with <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </summary>
public sealed class BridgeTokenCredential : IDisposable
{
    public const int TokenBytes = 32;
    private byte[] tokenBytes;
    private bool disposed;

    private BridgeTokenCredential(byte[] tokenBytes)
    {
        this.tokenBytes = tokenBytes;
        TokenId = Convert.ToHexString(SHA256.HashData(tokenBytes))[..12].ToLowerInvariant();
    }

    /// <summary>Diagnostic digest prefix; it is not an authentication credential.</summary>
    public string TokenId { get; }

    /// <summary>Parse exactly 32 base64url-encoded random bytes.</summary>
    public static bool TryCreate(string? encodedToken, out BridgeTokenCredential? credential)
    {
        credential = null;
        Span<byte> decoded = stackalloc byte[TokenBytes];
        if (!TryDecodeBase64Url(encodedToken, decoded))
        {
            CryptographicOperations.ZeroMemory(decoded);
            return false;
        }

        credential = new BridgeTokenCredential(decoded.ToArray());
        CryptographicOperations.ZeroMemory(decoded);
        return true;
    }

    /// <summary>Validate an Authorization value without distinguishing missing and invalid.</summary>
    public bool IsAuthorized(string? authorizationHeader)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Span<byte> candidate = stackalloc byte[TokenBytes];
        var validEncoding = false;
        if (authorizationHeader is not null
            && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            validEncoding = TryDecodeBase64Url(authorizationHeader[7..], candidate);
        }

        var equal = CryptographicOperations.FixedTimeEquals(tokenBytes, candidate);
        CryptographicOperations.ZeroMemory(candidate);
        return validEncoding && equal;
    }

    /// <summary>Create the one in-memory header required by the internal startup self-probe.</summary>
    internal string CreateSelfProbeAuthorizationHeader()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return $"Bearer {EncodeBase64Url(tokenBytes)}";
    }

    internal static bool FixedTimeEqualsBase64Url(string? candidate, ReadOnlySpan<byte> expected)
    {
        Span<byte> decoded = stackalloc byte[expected.Length];
        var validEncoding = expected.Length == TokenBytes && TryDecodeBase64Url(candidate, decoded);
        var equal = CryptographicOperations.FixedTimeEquals(expected, decoded);
        CryptographicOperations.ZeroMemory(decoded);
        return validEncoding && equal;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(tokenBytes);
        tokenBytes = [];
        disposed = true;
        GC.SuppressFinalize(this);
    }

    private static bool TryDecodeBase64Url(string? value, Span<byte> destination)
    {
        if (value is null || value.Length != 43 || destination.Length != TokenBytes)
        {
            destination.Clear();
            return false;
        }

        Span<char> base64 = stackalloc char[44];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            base64[index] = character switch
            {
                '-' => '+',
                '_' => '/',
                >= 'A' and <= 'Z' => character,
                >= 'a' and <= 'z' => character,
                >= '0' and <= '9' => character,
                _ => '\0',
            };
            if (base64[index] == '\0')
            {
                destination.Clear();
                return false;
            }
        }

        base64[43] = '=';
        return Convert.TryFromBase64Chars(base64, destination, out var written)
            && written == TokenBytes;
    }

    private static string EncodeBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// Reads and validates the fixed per-user token file, including its protected Windows DACL.
/// Failures return stable codes and never include the file path or token.
/// </summary>
public sealed class FileBridgeTokenSource : IBridgeTokenSource
{
    public const int MaximumTokenFileBytes = 4096;
    private readonly string tokenFilePath;

    public FileBridgeTokenSource(string? tokenFilePath = null)
    {
        this.tokenFilePath = string.IsNullOrWhiteSpace(tokenFilePath)
            ? GetDefaultTokenFilePath()
            : Path.GetFullPath(tokenFilePath);
    }

    public static string GetDefaultTokenFilePath()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "CAD-MAX", "config", "bridge-token.json");
    }

    public BridgeTokenLoadResult Load()
    {
        if (!OperatingSystem.IsWindows())
        {
            return BridgeTokenLoadResult.Failure("TOKEN_FILE_INSECURE");
        }

        try
        {
            // Open one reparse-resistant handle before inspecting ownership, DACL, identity, or
            // content. Never re-resolve the pathname after this point: otherwise an attacker able
            // to replace the path between validation and read could select the listener credential.
            using var stream = OpenReadHandle();
            if (!TryGetSafeOpenedFileInformation(stream.SafeFileHandle, out var before)
                || !HasSecureWindowsAcl(stream))
            {
                return BridgeTokenLoadResult.Failure("TOKEN_FILE_INSECURE");
            }

            var byteCount = GetFileSize(before);
            if (byteCount is 0 or > MaximumTokenFileBytes)
            {
                return BridgeTokenLoadResult.Failure("TOKEN_CONFIG_INVALID");
            }

            var bytes = GC.AllocateUninitializedArray<byte>((int)byteCount);
            try
            {
                stream.ReadExactly(bytes);
                if (!TryGetSafeOpenedFileInformation(stream.SafeFileHandle, out var after)
                    || !SameOpenedFile(before, after))
                {
                    return BridgeTokenLoadResult.Failure("TOKEN_FILE_INSECURE");
                }

                return Parse(bytes);
            }
            catch (EndOfStreamException)
            {
                return BridgeTokenLoadResult.Failure("TOKEN_FILE_INSECURE");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (FileNotFoundException)
        {
            return BridgeTokenLoadResult.Failure("TOKEN_NOT_CONFIGURED");
        }
        catch (UnauthorizedAccessException)
        {
            return BridgeTokenLoadResult.Failure("TOKEN_UNAVAILABLE");
        }
        catch (IOException)
        {
            return BridgeTokenLoadResult.Failure("TOKEN_UNAVAILABLE");
        }
        catch (SystemException)
        {
            return BridgeTokenLoadResult.Failure("TOKEN_UNAVAILABLE");
        }
    }

    private FileStream OpenReadHandle()
    {
        var handle = CreateFile(
            tokenFilePath,
            GenericRead | ReadControl,
            FileShare.Read,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var errorCode = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (errorCode is FileNotFoundError or PathNotFoundError)
            {
                throw new FileNotFoundException();
            }

            throw new IOException();
        }

        try
        {
            return new FileStream(handle, FileAccess.Read, MaximumTokenFileBytes, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static BridgeTokenLoadResult Parse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return BridgeTokenLoadResult.Failure("TOKEN_CONFIG_INVALID");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    return BridgeTokenLoadResult.Failure("TOKEN_CONFIG_INVALID");
                }
            }

            if (!names.SetEquals(["schemaVersion", "token", "createdAtUtc"])
                || !root.TryGetProperty("schemaVersion", out var schema)
                || schema.ValueKind != JsonValueKind.String
                || schema.GetString() != "1.0"
                || !root.TryGetProperty("createdAtUtc", out var createdAt)
                || createdAt.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(
                    createdAt.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _)
                || !root.TryGetProperty("token", out var token)
                || token.ValueKind != JsonValueKind.String)
            {
                return BridgeTokenLoadResult.Failure("TOKEN_CONFIG_INVALID");
            }

            if (!BridgeTokenCredential.TryCreate(token.GetString(), out var credential))
            {
                return BridgeTokenLoadResult.Failure("TOKEN_INVALID");
            }

            return new BridgeTokenLoadResult(credential, null);
        }
        catch (JsonException)
        {
            return BridgeTokenLoadResult.Failure("TOKEN_CONFIG_INVALID");
        }
    }

    private static bool HasSecureWindowsAcl(FileStream stream)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is null)
        {
            return false;
        }

        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = FileSystemAclExtensions.GetAccessControl(stream);
        if (!security.AreAccessRulesProtected
            || security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner
            || !owner.Equals(currentUser))
        {
            return false;
        }

        var currentUserCanRead = false;
        var currentUserCanWrite = false;
        var systemAllowed = false;
        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IsInherited || rule.IdentityReference is not SecurityIdentifier identity)
            {
                return false;
            }

            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            var grantsRead = (rule.FileSystemRights
                & (FileSystemRights.ReadData | FileSystemRights.Read)) != 0;
            var grantsWrite = (rule.FileSystemRights
                & (FileSystemRights.WriteData | FileSystemRights.Write)) != 0;
            if (identity.Equals(currentUser))
            {
                currentUserCanRead |= grantsRead;
                currentUserCanWrite |= grantsWrite;
            }
            else if (identity.Equals(system))
            {
                systemAllowed |= grantsRead;
            }
            else if (rule.FileSystemRights != 0)
            {
                return false;
            }
        }

        return currentUserCanRead && currentUserCanWrite && systemAllowed;
    }

    private static bool TryGetSafeOpenedFileInformation(
        SafeFileHandle handle,
        out ByHandleFileInformation information)
    {
        if (!GetFileInformationByHandle(handle, out information))
        {
            return false;
        }

        return information.NumberOfLinks == 1
            && (information.FileAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) == 0;
    }

    private static ulong GetFileSize(ByHandleFileInformation information) =>
        ((ulong)information.FileSizeHigh << 32) | information.FileSizeLow;

    private static bool SameOpenedFile(
        ByHandleFileInformation before,
        ByHandleFileInformation after) =>
        before.FileAttributes == after.FileAttributes
        && before.VolumeSerialNumber == after.VolumeSerialNumber
        && before.FileIndexHigh == after.FileIndexHigh
        && before.FileIndexLow == after.FileIndexLow
        && before.LastWriteTime.LowDateTime == after.LastWriteTime.LowDateTime
        && before.LastWriteTime.HighDateTime == after.LastWriteTime.HighDateTime
        && before.FileSizeHigh == after.FileSizeHigh
        && before.FileSizeLow == after.FileSizeLow
        && before.NumberOfLinks == after.NumberOfLinks;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private const uint GenericRead = 0x80000000;
    private const uint ReadControl = 0x00020000;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int FileNotFoundError = 2;
    private const int PathNotFoundError = 3;
}
