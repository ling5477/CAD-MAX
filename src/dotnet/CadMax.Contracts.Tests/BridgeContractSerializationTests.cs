using System.Text.Json;
using CadMax.Contracts;
using Xunit;

namespace CadMax.Contracts.Tests;

public sealed class BridgeContractSerializationTests
{
    [Fact]
    public void BridgeDtosUseExactSchemaFieldNamesAndEnumValues()
    {
        var health = new BridgeHealthData(
            "CadMax.AutoCAD.Bridge",
            Guid.NewGuid().ToString("D"),
            BridgePluginState.Ready,
            AutoCADConnected: true,
            DevelopmentHost: false,
            DocumentAccess: false,
            DwgRead: false,
            DwgWrite: false,
            ReadOnly: true,
            AllowWrite: false,
            AllowScript: false);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(health, CadJson.Options));
        using var schema = JsonDocument.Parse(File.ReadAllText(FindContract(
            "bridge-endpoints.schema.json")));
        var required = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("health")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()
                ?? throw new InvalidOperationException("CONTRACT_FIELD_INVALID"))
            .ToHashSet(StringComparer.Ordinal);
        var actual = serialized.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(required.SetEquals(actual));
        Assert.Equal("READY", serialized.RootElement.GetProperty("pluginState").GetString());
        Assert.True(serialized.RootElement.GetProperty("autocadConnected").GetBoolean());
        Assert.False(serialized.RootElement.TryGetProperty("autoCADConnected", out _));
    }

    [Fact]
    public void CSharpStatusEnumMatchesLanguageNeutralSchema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(FindContract(
            "cad-result.schema.json")));
        var expected = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("status")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(item => item.GetString()
                ?? throw new InvalidOperationException("CONTRACT_STATUS_INVALID"))
            .ToHashSet(StringComparer.Ordinal);
        var actual = Enum.GetValues<CadStatus>()
            .Select(value => JsonSerializer.Serialize(value, CadJson.Options).Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(expected.SetEquals(actual));
    }

    [Fact]
    public void ContextProbeUsesStrictCamelCaseContract()
    {
        var request = new ContextProbeRequest(
            CadProtocol.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddSeconds(5),
            Guid.NewGuid().ToString("D"),
            ExpectedDocumentId: null);
        var json = JsonSerializer.Serialize(request, CadJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(document.RootElement.TryGetProperty("expectedDocumentId", out var expected));
        Assert.Equal(JsonValueKind.Null, expected.ValueKind);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ContextProbeRequest>(
            json[..^1] + ",\"unexpected\":true}",
            CadJson.Options));
    }

    [Fact]
    public void HeartbeatContextFieldsMatchLanguageNeutralSchema()
    {
        var heartbeat = new BridgeHeartbeatData(
            Guid.NewGuid().ToString("D"),
            HeartbeatSequence: 1,
            DateTimeOffset.UtcNow,
            UptimeMs: 1,
            BridgePluginState.Ready,
            "revision",
            AutoCADConnected: true,
            DevelopmentHost: false,
            ContextDispatcherState: "READY",
            QueueDepth: 0,
            InFlightCount: 0,
            Modal: false,
            HasActiveDocument: true,
            ContextDispatchLastStatus.Ok);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(
            heartbeat,
            CadJson.Options));
        using var schema = JsonDocument.Parse(File.ReadAllText(FindContract(
            "bridge-endpoints.schema.json")));
        var required = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("heartbeat")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()
                ?? throw new InvalidOperationException("CONTRACT_FIELD_INVALID"))
            .ToHashSet(StringComparer.Ordinal);
        var actual = serialized.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(required.SetEquals(actual));
        Assert.Equal("OK", serialized.RootElement.GetProperty("lastDispatchStatus").GetString());
    }

    private static string FindContract(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("CONTRACT_FIXTURE_NOT_FOUND");
    }
}
