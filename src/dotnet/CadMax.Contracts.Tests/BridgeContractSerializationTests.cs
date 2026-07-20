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
