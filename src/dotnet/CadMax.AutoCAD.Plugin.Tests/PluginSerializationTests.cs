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
        Assert.Equal("0.1.0", document.RootElement.GetProperty("pluginVersion").GetString());
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
    }

    [Fact]
    public void StatusContainsOnlySafeFixedCapabilities()
    {
        var controller = new PluginLifecycleController(
            PluginMetadata.CreateDefault(),
            new NullEvidenceWriter());
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
}
