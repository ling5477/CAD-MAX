using System.Text.Json;
using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class PluginSerializationTests
{
    [Fact]
    public void MetadataSerializesWithCamelCaseFields()
    {
        var json = PluginJson.SerializeMetadata(PluginMetadata.CreateDefault());
        using var document = JsonDocument.Parse(json);

        Assert.Equal("CAD-MAX", document.RootElement.GetProperty("pluginName").GetString());
        Assert.Equal("0.1.1", document.RootElement.GetProperty("pluginVersion").GetString());
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void StatusContainsOnlySafeFixedCapabilities()
    {
        var controller = new PluginLifecycleController(
            PluginMetadata.CreateDefault(),
            new NullEvidenceWriter(),
            new TestTokenSource(),
            new TestServerFactory());
        Assert.True(controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        var json = PluginJson.SerializeStatus(controller.GetStatus());
        var lowerJson = json.ToLowerInvariant();
        using var document = JsonDocument.Parse(json);

        Assert.Equal("READY", document.RootElement.GetProperty("lifecycleState").GetString());
        Assert.True(document.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.False(document.RootElement.GetProperty("allowWrite").GetBoolean());
        Assert.False(document.RootElement.GetProperty("allowScript").GetBoolean());
        Assert.DoesNotContain("path", lowerJson, StringComparison.Ordinal);
        Assert.DoesNotContain("document", lowerJson, StringComparison.Ordinal);
        Assert.DoesNotContain("database", lowerJson, StringComparison.Ordinal);
        Assert.DoesNotContain("token", lowerJson, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsafeEvidenceValuesAreReplacedInsteadOfCopied()
    {
        var entry = PluginLifecycleEvidenceEntry.Create(
            new PluginMetadata("CAD-MAX", "bad/path", "1.0"),
            new PluginRuntimeInfo(2025, "C:/private/install", "0.1.0", true),
            PluginLifecycleEventType.PluginInitializeFailed,
            PluginLifecycleState.Starting,
            PluginLifecycleState.Failed,
            success: false,
            safeErrorCode: "C:/secret/token");

        var json = PluginJson.SerializeEvidence(entry);

        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bad/path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTERNAL_ERROR", json, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", json, StringComparison.Ordinal);
    }

    private sealed class NullEvidenceWriter : IPluginLifecycleEvidenceWriter
    {
        public bool TryAppend(PluginLifecycleEvidenceEntry entry) => true;
    }

    private sealed class TestTokenSource : IBridgeTokenSource
    {
        public BridgeTokenLoadResult Load()
        {
            var token = Convert.ToBase64String(
                    System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            _ = BridgeTokenCredential.TryCreate(token, out var credential);
            return new BridgeTokenLoadResult(credential, null);
        }
    }

    private sealed class TestServerFactory : ILoopbackBridgeServerFactory
    {
        public ILoopbackBridgeServer Create(
            LoopbackBridgeOptions options,
            BridgeTokenCredential credential,
            CadMax.Bridge.Core.BridgeInstanceResponseService responseService,
            DocumentContextDispatcher contextDispatcher,
            Action<string> listenerFault) => new TestServer();
    }

    private sealed class TestServer : ILoopbackBridgeServer
    {
        public int ActiveConnectionCount => 0;
        public string? TryStart() => null;
        public Task<bool> RunAuthenticatedSelfProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);
        public Task<bool> StopAsync(TimeSpan deadline) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
