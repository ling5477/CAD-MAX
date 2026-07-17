using System.Text.Json;
using CadMax.Contracts;
using Xunit;

namespace CadMax.Contracts.Tests;

public sealed class EnvelopeSerializationTests
{
    [Fact]
    public void EnvelopeSerializesWithCamelCaseAndStringEnums()
    {
        var envelope = CadResultEnvelope.Failure(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            CadStatus.NotImplemented,
            "No command handler is registered");

        var json = JsonSerializer.Serialize(envelope, CadJson.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("NOT_IMPLEMENTED", root.GetProperty("status").GetString());
        Assert.Equal("NOT_IMPLEMENTED", root.GetProperty("errorCode").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(root.TryGetProperty("SchemaVersion", out _));
    }
}
