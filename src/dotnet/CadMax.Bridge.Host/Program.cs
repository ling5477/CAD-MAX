using System.Net;
using CadMax.AutoCAD.Plugin;
using CadMax.Bridge.Core;
using CadMax.Contracts;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
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

var tokenSource = new FileBridgeTokenSource(
    Environment.GetEnvironmentVariable("CAD_MAX_BRIDGE_TOKEN_FILE"));
var tokenResult = tokenSource.Load();
if (!tokenResult.Success || tokenResult.Credential is null)
{
    Console.Error.WriteLine($"ERROR / {tokenResult.SafeErrorCode ?? "TOKEN_UNAVAILABLE"}");
    Environment.ExitCode = 1;
    return;
}

var credential = tokenResult.Credential;
var responseService = new BridgeInstanceResponseService(
    new BridgeInstanceMetadata(
        "CadMax.Bridge.Host",
        typeof(BridgeInstanceResponseService).Assembly.GetName().Version?.ToString(3)
            ?? "UNKNOWN",
        "NOT_APPLICABLE",
        0,
        "NOT_CONNECTED",
        "net8.0-windows",
        IsAutoCADHostProcess: false,
        DevelopmentHost: true),
    BridgePluginState.Ready);

builder.Services.AddSingleton(responseService);
var app = builder.Build();
app.Lifetime.ApplicationStopping.Register(credential.Dispose);

app.Use(async (context, next) =>
{
    if (!credential.IsAuthorized(context.Request.Headers.Authorization.ToString()))
    {
        await BridgeHttpResponses.WriteAsync(
            context,
            401,
            CadResultEnvelope.Failure(
                Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"),
                CadStatus.Unauthorized,
                "CAD-MAX bridge authorization failed"));
        return;
    }

    var route = context.Request.Path.Value ?? string.Empty;
    if (BridgeRoutes.Production.Contains(route))
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.Headers.Allow = "GET";
            await BridgeHttpResponses.WriteAsync(
                context,
                405,
                CadResultEnvelope.Failure(
                    Guid.NewGuid().ToString("D"),
                    Guid.NewGuid().ToString("D"),
                    CadStatus.MethodNotAllowed,
                    "CAD-MAX bridge permits GET only"));
            return;
        }

        if (context.Request.QueryString.HasValue)
        {
            await BridgeHttpResponses.WriteAsync(
                context,
                400,
                CadResultEnvelope.Failure(
                    Guid.NewGuid().ToString("D"),
                    Guid.NewGuid().ToString("D"),
                    CadStatus.QueryNotAllowed,
                    "CAD-MAX bridge query strings are not allowed"));
            return;
        }

        if (context.Request.ContentLength is > 0
            || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            await BridgeHttpResponses.WriteAsync(
                context,
                400,
                CadResultEnvelope.Failure(
                    Guid.NewGuid().ToString("D"),
                    Guid.NewGuid().ToString("D"),
                    CadStatus.RequestBodyNotAllowed,
                    "CAD-MAX bridge request bodies are not allowed"));
            return;
        }
    }

    await next(context);
});

foreach (var route in BridgeRoutes.Production)
{
    var mappedRoute = route;
    app.MapGet(mappedRoute, (BridgeInstanceResponseService service) =>
    {
        var envelope = service.CreateResponse(
            mappedRoute,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        return Results.Json(envelope, CadJson.Options, statusCode: 200);
    });
}

app.MapPost(
    "/v1/commands",
    async (
        CadCommandRequest request,
        CadCommandDispatcher dispatcher,
        HttpContext httpContext) =>
        await dispatcher.DispatchAsync(request, httpContext.RequestAborted));

app.MapFallback(async context =>
{
    await BridgeHttpResponses.WriteAsync(
        context,
        404,
        CadResultEnvelope.Failure(
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            CadStatus.RouteNotFound,
            "CAD-MAX bridge route was not found"));
});

app.Run();

/// <summary>Validates the development-host binding before Kestrel starts.</summary>
internal static class BridgeBinding
{
    internal const string DefaultUrl = "http://127.0.0.1:47779";

    /// <summary>Resolve the development-only URL from its dedicated environment setting.</summary>
    internal static string Resolve()
    {
        var value = Environment.GetEnvironmentVariable("CAD_MAX_DEVELOPMENT_BRIDGE_URL")
            ?? DefaultUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !IPAddress.TryParse(uri.Host, out var address)
            || !address.Equals(IPAddress.Loopback)
            || uri.Port is < 1 or > 65_535
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "CAD_MAX_DEVELOPMENT_BRIDGE_URL must use explicit IPv4 loopback HTTP.");
        }

        return value;
    }
}

internal static class BridgeHttpResponses
{
    internal static async Task WriteAsync(
        HttpContext context,
        int httpStatus,
        CadResultEnvelope envelope)
    {
        context.Response.StatusCode = httpStatus;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (httpStatus == 401)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        await context.Response.WriteAsJsonAsync(envelope, CadJson.Options);
    }
}

/// <summary>Public marker used by ASP.NET integration tests.</summary>
public partial class Program;
