using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class BridgeTokenTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        AppContext.BaseDirectory,
        "cad-max-token-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SecureTokenFileLoadsAndProducesDigestOnlyIdentifier()
    {
        var path = WriteTokenFile(CreateToken());

        var result = new FileBridgeTokenSource(path).Load();

        Assert.True(result.Success);
        Assert.NotNull(result.Credential);
        Assert.Matches("^[0-9a-f]{12}$", result.Credential.TokenId);
        result.Credential.Dispose();
    }

    [Fact]
    public void MissingTokenFileFailsClosed()
    {
        var result = new FileBridgeTokenSource(Path.Combine(testRoot, "missing.json")).Load();

        Assert.False(result.Success);
        Assert.Equal("TOKEN_NOT_CONFIGURED", result.SafeErrorCode);
    }

    [Fact]
    public void InvalidSchemaFailsClosed()
    {
        var path = WriteRawFile("{\"schemaVersion\":\"2.0\"}");

        var result = new FileBridgeTokenSource(path).Load();

        Assert.Equal("TOKEN_CONFIG_INVALID", result.SafeErrorCode);
    }

    [Fact]
    public void InvalidTokenLengthFailsClosed()
    {
        var path = WriteTokenFile("too-short");

        var result = new FileBridgeTokenSource(path).Load();

        Assert.Equal("TOKEN_INVALID", result.SafeErrorCode);
    }

    [Fact]
    public void BroadReadAclFailsClosed()
    {
        var path = WriteTokenFile(CreateToken());
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.Read,
            AccessControlType.Allow));
        file.SetAccessControl(security);

        var result = new FileBridgeTokenSource(path).Load();

        Assert.Equal("TOKEN_FILE_INSECURE", result.SafeErrorCode);
    }

    [Fact]
    public void NonAllowlistedControlRightsFailClosed()
    {
        var path = WriteTokenFile(CreateToken());
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership,
            AccessControlType.Allow));
        file.SetAccessControl(security);

        var result = new FileBridgeTokenSource(path).Load();

        Assert.Equal("TOKEN_FILE_INSECURE", result.SafeErrorCode);
    }

    [Fact]
    public void BearerValidationRejectsMissingWrongAndMalformedValues()
    {
        var token = CreateToken();
        Assert.True(BridgeTokenCredential.TryCreate(token, out var credential));
        using (var validCredential = credential
            ?? throw new InvalidOperationException("TEST_TOKEN_CREATION_FAILED"))
        {
            Assert.True(validCredential.IsAuthorized($"Bearer {token}"));
            Assert.False(validCredential.IsAuthorized(null));
            Assert.False(validCredential.IsAuthorized("Bearer invalid"));
            Assert.False(validCredential.IsAuthorized($"Bearer {CreateToken()}"));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    internal static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    internal static void WriteSecureFile(string path, string content)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("CURRENT_USER_SID_UNAVAILABLE");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new FileSecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            system,
            FileSystemRights.FullControl,
            AccessControlType.Allow));

        var payload = new UTF8Encoding(false).GetBytes(content);
        try
        {
            using var stream = FileSystemAclExtensions.Create(
                new FileInfo(path),
                FileMode.CreateNew,
                FileSystemRights.FullControl,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough,
                security);
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private string WriteTokenFile(string token) =>
        WriteRawFile(JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            token,
            createdAtUtc = DateTimeOffset.UtcNow,
        }));

    private string WriteRawFile(string content)
    {
        Directory.CreateDirectory(testRoot);
        var path = Path.Combine(testRoot, $"{Guid.NewGuid():N}.json");
        WriteSecureFile(path, content);
        return path;
    }
}
