using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CadMax.Bridge.Core.Tests;

public sealed class HostEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public HostEndpointTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpointReturnsOkEnvelope()
    {
        using var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("OK", document.RootElement.GetProperty("status").GetString());
        Assert.False(
            document.RootElement.GetProperty("data").GetProperty("autocadConnected").GetBoolean());
    }

    [Fact]
    public async Task CapabilitiesEndpointDoesNotClaimDrawingImplementation()
    {
        using var response = await client.GetAsync("/v1/capabilities");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var capabilities = document.RootElement.GetProperty("data").GetProperty("capabilities");

        Assert.Contains(
            capabilities.EnumerateArray(),
            item => item.GetProperty("name").GetString() == "drawing.status"
                && !item.GetProperty("implemented").GetBoolean());
    }
}
