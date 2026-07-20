using System.Net;
using System.Net.Http.Headers;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CadMax.Bridge.Core.Tests;

public sealed class HostEndpointTests : IClassFixture<BridgeHostFactory>
{
    private readonly BridgeHostFactory factory;

    public HostEndpointTests(BridgeHostFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/v1/health")]
    [InlineData("/v1/version")]
    [InlineData("/v1/capabilities")]
    [InlineData("/v1/heartbeat")]
    public async Task CanonicalEndpointsUseSharedDevelopmentEnvelope(string route)
    {
        using var client = factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(route);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var data = root.GetProperty("data");

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("OK", root.GetProperty("status").GetString());
        Assert.True(data.GetProperty("developmentHost").GetBoolean());
        Assert.False(data.GetProperty("autocadConnected").GetBoolean());
    }

    [Fact]
    public async Task MissingAndWrongTokensReturnSameUnauthorizedEnvelope()
    {
        using var missingClient = factory.CreateClient();
        using var wrongClient = factory.CreateClient();
        wrongClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BridgeHostFactory.CreateToken());

        using var missing = await missingClient.GetAsync("/v1/health");
        using var wrong = await wrongClient.GetAsync("/v1/health");
        var missingBody = await missing.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("UNAUTHORIZED", missingBody, StringComparison.Ordinal);
        Assert.Contains("UNAUTHORIZED", wrongBody, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Token, missingBody, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Token, wrongBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentCommandEndpointRemainsFailClosed()
    {
        using var client = factory.CreateAuthenticatedClient();
        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1.0",
                requestId = Guid.NewGuid(),
                traceId = Guid.NewGuid(),
                command = "drawing.status",
                parameters = new { },
                timeoutMs = 1000,
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/v1/commands", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("NOT_IMPLEMENTED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRouteUsesVersionedEnvelope()
    {
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.GetAsync("/v1/missing");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("ROUTE_NOT_FOUND", body, StringComparison.Ordinal);
    }
}

public sealed class BridgeHostFactory : WebApplicationFactory<Program>
{
    private readonly string? previousTokenFile;
    private readonly string testRoot;

    public BridgeHostFactory()
    {
        testRoot = Path.Combine(
            AppContext.BaseDirectory,
            "cad-max-host-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        Token = CreateToken();
        var tokenPath = Path.Combine(testRoot, "bridge-token.json");
        WriteSecureFile(
            tokenPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = "1.0",
                token = Token,
                createdAtUtc = DateTimeOffset.UtcNow,
            }));
        previousTokenFile = Environment.GetEnvironmentVariable("CAD_MAX_BRIDGE_TOKEN_FILE");
        Environment.SetEnvironmentVariable("CAD_MAX_BRIDGE_TOKEN_FILE", tokenPath);
    }

    public string Token { get; }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    public static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        Environment.SetEnvironmentVariable("CAD_MAX_BRIDGE_TOKEN_FILE", previousTokenFile);
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void WriteSecureFile(string path, string content)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("CURRENT_USER_SID_UNAVAILABLE");
        var security = new FileSecurity();
        security.SetOwner(currentUser);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
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
}
