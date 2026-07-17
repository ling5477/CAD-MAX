using System.Net;
using CadMax.Bridge.Core;
using CadMax.Contracts;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(BridgeBinding.Resolve());
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = CadJson.Options.PropertyNamingPolicy;
    options.SerializerOptions.DictionaryKeyPolicy = CadJson.Options.DictionaryKeyPolicy;
    foreach (var converter in CadJson.Options.Converters)
    {
        options.SerializerOptions.Converters.Add(converter);
    }
});
builder.Services.AddCadMaxBridgeCore();

var app = builder.Build();

app.MapGet("/health", () =>
{
    var requestId = Guid.NewGuid().ToString();
    var traceId = Guid.NewGuid().ToString();
    return CadResultEnvelope.Ok(
        requestId,
        traceId,
        "CAD-MAX bridge host is healthy",
        new
        {
            service = "CadMax.Bridge.Host",
            schemaVersion = CadProtocol.SchemaVersion,
            autocadConnected = false,
        });
});

app.MapGet("/v1/capabilities", (CadCommandRegistry registry) =>
{
    var requestId = Guid.NewGuid().ToString();
    var traceId = Guid.NewGuid().ToString();
    var capabilities = new[]
    {
        new CadCapabilityDescription("bridge.health", true, true),
        new CadCapabilityDescription("bridge.capabilities", true, true),
        new CadCapabilityDescription("bridge.commands", true, true),
        new CadCapabilityDescription("drawing.status", false, true),
        new CadCapabilityDescription("drawing.write", false, false),
    };
    return CadResultEnvelope.Ok(
        requestId,
        traceId,
        "CAD-MAX bridge capability inventory",
        new
        {
            capabilities,
            registeredCommands = registry.ListCommandNames(),
        });
});

app.MapPost(
    "/v1/commands",
    async (
        CadCommandRequest request,
        CadCommandDispatcher dispatcher,
        HttpContext httpContext) =>
        await dispatcher.DispatchAsync(request, httpContext.RequestAborted));

app.Run();

/// <summary>
/// Validates the development-host binding before Kestrel starts.
/// </summary>
internal static class BridgeBinding
{
    /// <summary>Resolve the local-only URL from environment or its safe default.</summary>
    internal static string Resolve()
    {
        var value = Environment.GetEnvironmentVariable("CAD_MAX_BRIDGE_URL")
            ?? "http://127.0.0.1:47770";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !IsLoopback(uri.Host))
        {
            throw new InvalidOperationException(
                "CAD_MAX_BRIDGE_URL must use an explicit loopback host.");
        }

        return value;
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}

/// <summary>Public marker used by ASP.NET integration tests.</summary>
public partial class Program;
