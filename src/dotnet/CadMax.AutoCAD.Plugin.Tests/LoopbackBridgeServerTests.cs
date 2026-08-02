using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CadMax.Bridge.Core;
using CadMax.Contracts;
using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class LoopbackBridgeServerTests
{
    [Fact]
    public void BindingAndPortConfigurationFailClosed()
    {
        Assert.Equal(IPAddress.Loopback, LoopbackBridgeServer.BindingAddress);
        Assert.Throws<InvalidOperationException>(() => new LoopbackBridgeOptions(0).Validate());
        Assert.DoesNotContain(
            typeof(LoopbackBridgeOptions).GetProperties(),
            property => string.Equals(property.Name, "Host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticatedSelfProbeReachesReadyCanonicalEndpoints()
    {
        await using var fixture = await ServerFixture.StartAsync();

        foreach (var route in BridgeRoutes.Production)
        {
            var response = await fixture.SendAsync("GET", route, fixture.Token);
            Assert.Equal(200, response.StatusCode);
            Assert.Equal("OK", response.Status);
            Assert.Equal(fixture.Service.InstanceId, response.InstanceId);
        }
    }

    [Fact]
    public async Task MissingAndWrongTokensUseSameUnauthorizedResponse()
    {
        await using var fixture = await ServerFixture.StartAsync();

        var missing = await fixture.SendAsync("GET", BridgeRoutes.Health, null);
        var wrong = await fixture.SendAsync(
            "GET",
            BridgeRoutes.Health,
            BridgeTokenTests.CreateToken());

        Assert.Equal((401, "UNAUTHORIZED"), (missing.StatusCode, missing.Status));
        Assert.Equal((401, "UNAUTHORIZED"), (wrong.StatusCode, wrong.Status));
        Assert.DoesNotContain(fixture.Token, missing.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Token, wrong.Raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", "/v1/health", "", 405, "METHOD_NOT_ALLOWED")]
    [InlineData("GET", "/v1/health?detail=1", "", 400, "QUERY_NOT_ALLOWED")]
    [InlineData("GET", "/v1/missing", "", 404, "ROUTE_NOT_FOUND")]
    [InlineData("GET", "/v1/commands", "", 404, "ROUTE_NOT_FOUND")]
    [InlineData("GET", "/v1/health", "Content-Length: 1\r\n\r\nx", 400, "REQUEST_BODY_NOT_ALLOWED")]
    [InlineData("GET", "/v1/health", "Transfer-Encoding: chunked\r\n", 400, "REQUEST_BODY_NOT_ALLOWED")]
    public async Task ParserRejectsDisallowedRequestShapes(
        string method,
        string target,
        string extraHeaders,
        int expectedHttp,
        string expectedStatus)
    {
        await using var fixture = await ServerFixture.StartAsync();

        var response = await fixture.SendRawAsync(method, target, fixture.Token, extraHeaders);

        Assert.Equal(expectedHttp, response.StatusCode);
        Assert.Equal(expectedStatus, response.Status);
    }

    [Fact]
    public async Task RequestLineAndHeaderLimitsReturnBoundedErrors()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var longTarget = "/" + new string('a', 2100);
        var requestLine = await fixture.SendRawAsync("GET", longTarget, fixture.Token, string.Empty);
        var largeHeader = await fixture.SendRawAsync(
            "GET",
            BridgeRoutes.Health,
            fixture.Token,
            $"X-Fill: {new string('b', 8300)}\r\n");

        Assert.Equal((414, "REQUEST_TARGET_TOO_LONG"),
            (requestLine.StatusCode, requestLine.Status));
        Assert.Equal((431, "HEADERS_TOO_LARGE"),
            (largeHeader.StatusCode, largeHeader.Status));
    }

    [Fact]
    public async Task ConnectionLimitReturnsServerBusyWithoutUnboundedTaskCreation()
    {
        var limits = new LoopbackBridgeLimits(
            ReadTimeoutMs: 500,
            MaxConcurrentConnections: 1);
        await using var fixture = await ServerFixture.StartAsync(limits);
        using var occupied = new TcpClient(AddressFamily.InterNetwork);
        await occupied.ConnectAsync(IPAddress.Loopback, fixture.Port);

        var response = await fixture.SendRawAsync(
            "GET",
            BridgeRoutes.Health,
            fixture.Token,
            string.Empty);

        Assert.Equal((503, "SERVER_BUSY"), (response.StatusCode, response.Status));
    }

    [Fact]
    public async Task IncompleteRequestTimesOutAndConnectionCloses()
    {
        var limits = new LoopbackBridgeLimits(ReadTimeoutMs: 100);
        await using var fixture = await ServerFixture.StartAsync(limits);
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, fixture.Port);
        await using var stream = client.GetStream();
        await stream.WriteAsync("GET /v1/health"u8.ToArray());

        var raw = await ReadToCloseAsync(stream);
        var response = Parse(raw);

        Assert.Equal((408, "REQUEST_TIMEOUT"), (response.StatusCode, response.Status));
    }

    [Fact]
    public async Task PortConflictReturnsStableErrorAndDoesNotChooseAnotherPort()
    {
        var port = FindFreePort();
        var owner = new TcpListener(IPAddress.Loopback, port);
        owner.Start();
        var token = BridgeTokenTests.CreateToken();
        Assert.True(BridgeTokenCredential.TryCreate(token, out var credential));
        using (var validCredential = credential
            ?? throw new InvalidOperationException("TEST_TOKEN_CREATION_FAILED"))
        await using (var server = CreateServer(port, validCredential, out _, out _))
        {
            Assert.Equal("PORT_IN_USE", server.TryStart());
            Assert.Equal(0, server.ActiveConnectionCount);
        }
        owner.Stop();
    }

    [Fact]
    public async Task ShutdownDrainsConnectionsAndReleasesExactPort()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var active = new TcpClient(AddressFamily.InterNetwork);
        await active.ConnectAsync(IPAddress.Loopback, fixture.Port);

        Assert.True(await fixture.Server.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, fixture.Server.ActiveConnectionCount);
        Assert.Equal(0, fixture.Server.ActiveTaskCount);
        var replacement = new TcpListener(IPAddress.Loopback, fixture.Port);
        replacement.Start();
        replacement.Stop();
    }

    [Fact]
    public async Task RepeatedStartAndStopAreIdempotent()
    {
        await using var fixture = await ServerFixture.StartAsync();

        Assert.Null(fixture.Server.TryStart());
        Assert.True(await fixture.Server.StopAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await fixture.Server.StopAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void SynchronouslyCompletedTaskIsRemovedBeforeShutdownSnapshot()
    {
        var registry = new ActiveConnectionTaskRegistry();

        Assert.True(registry.TryRegister(1, Task.CompletedTask));

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.SnapshotRunning());
        Assert.Equal(0, registry.ObservedFaultCount);
    }

    [Fact]
    public void SynchronouslyFaultedTaskIsRemovedAndItsExceptionIsObserved()
    {
        var registry = new ActiveConnectionTaskRegistry();
        var faulted = Task.FromException(new InvalidOperationException("TEST_CONNECTION_FAILURE"));

        Assert.True(registry.TryRegister(1, faulted));

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.SnapshotRunning());
        Assert.Equal(1, registry.ObservedFaultCount);
    }

    [Fact]
    public void SynchronouslyCanceledTaskIsRemovedBeforeShutdownSnapshot()
    {
        var registry = new ActiveConnectionTaskRegistry();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.True(registry.TryRegister(1, Task.FromCanceled(cancellation.Token)));

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.SnapshotRunning());
        Assert.Equal(0, registry.ObservedFaultCount);
    }

    [Fact]
    public async Task AsynchronouslyCompletedTaskIsRemovedAfterRegistration()
    {
        var registry = new ActiveConnectionTaskRegistry();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(registry.TryRegister(1, completion.Task));
        Assert.Equal(1, registry.Count);
        _ = Assert.Single(registry.SnapshotRunning());

        completion.SetResult();
        await completion.Task;

        Assert.True(SpinWait.SpinUntil(() => registry.Count == 0, TimeSpan.FromSeconds(1)));
        Assert.Empty(registry.SnapshotRunning());
    }

    [Fact]
    public void TenThousandSynchronouslyCompletedTasksAreNotRetained()
    {
        var registry = new ActiveConnectionTaskRegistry();

        for (var connectionId = 1L; connectionId <= 10_000; connectionId++)
        {
            Assert.True(registry.TryRegister(connectionId, Task.CompletedTask));
        }

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.SnapshotRunning());
        Assert.Equal(0, registry.ObservedFaultCount);
    }

    [Fact]
    public async Task ShutdownSnapshotHandlesCompletionBeforeAndAfterSnapshot()
    {
        var registry = new ActiveConnectionTaskRegistry();
        Assert.True(registry.TryRegister(1, Task.CompletedTask));
        Assert.Empty(registry.SnapshotRunning());

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(registry.TryRegister(2, completion.Task));
        var shutdownSnapshot = registry.SnapshotRunning();
        _ = Assert.Single(shutdownSnapshot);

        completion.SetResult();
        await Task.WhenAll(shutdownSnapshot);

        Assert.True(SpinWait.SpinUntil(() => registry.Count == 0, TimeSpan.FromSeconds(1)));
        Assert.Empty(registry.SnapshotRunning());
    }

    [Fact]
    public async Task ConcurrentAndRepeatedStopDrainTasksAndReleasePort()
    {
        await using var fixture = await ServerFixture.StartAsync();
        using var active = new TcpClient(AddressFamily.InterNetwork);
        await active.ConnectAsync(IPAddress.Loopback, fixture.Port);

        var stopResults = await Task.WhenAll(
            Enumerable.Range(0, 4)
                .Select(_ => fixture.Server.StopAsync(TimeSpan.FromSeconds(2))));

        Assert.All(stopResults, Assert.True);
        Assert.Equal(0, fixture.Server.ActiveConnectionCount);
        Assert.Equal(0, fixture.Server.ActiveTaskCount);
        var replacement = new TcpListener(IPAddress.Loopback, fixture.Port);
        replacement.Start();
        replacement.Stop();
    }

    [Fact]
    public void InstanceAndCapabilityRevisionChangeAcrossLifecycleInstances()
    {
        var first = CreateResponseService(BridgePluginState.Listening);
        var initialRevision = first.CapabilityRevision;
        first.SetPluginState(BridgePluginState.Ready);
        var second = CreateResponseService(BridgePluginState.Ready);

        Assert.NotEqual(initialRevision, first.CapabilityRevision);
        Assert.NotEqual(first.InstanceId, second.InstanceId);
        Assert.NotEqual(first.CapabilityRevision, second.CapabilityRevision);
    }

    [Fact]
    public async Task CapabilitiesAreHonestAndHeartbeatIsMonotonic()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var capabilities = await fixture.SendAsync("GET", BridgeRoutes.Capabilities, fixture.Token);
        var heartbeatOne = await fixture.SendAsync("GET", BridgeRoutes.Heartbeat, fixture.Token);
        var heartbeatTwo = await fixture.SendAsync("GET", BridgeRoutes.Heartbeat, fixture.Token);
        using var capabilityJson = JsonDocument.Parse(capabilities.Body);
        var inventory = capabilityJson.RootElement.GetProperty("data").GetProperty("capabilities");

        Assert.True(inventory.GetProperty("bridge.health").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.status").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.list_documents").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.active_document").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.units").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.bounds").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.layouts").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.system_metadata").GetBoolean());
        Assert.False(inventory.GetProperty("drawing.revision").GetBoolean());
        Assert.True(inventory.GetProperty("dwg.read").GetBoolean());
        Assert.False(inventory.GetProperty("dwg.write").GetBoolean());
        Assert.False(inventory.GetProperty("script").GetBoolean());
        Assert.False(inventory.GetProperty("command").GetBoolean());
        Assert.True(heartbeatTwo.HeartbeatSequence > heartbeatOne.HeartbeatSequence);
    }

    [Fact]
    public void NoDocumentKeepsOnlyApplicationDrawingCapabilitiesAvailable()
    {
        using var dispatcher = new DocumentContextDispatcher();
        var service = CreateResponseService(BridgePluginState.Ready, dispatcher);
        dispatcher.ConfigureInstance(service.InstanceId);
        dispatcher.MarkReady(activeDocumentExists: false, documentIsQuiescent: false);

        var capabilities = service.CreateResponse(
            BridgeRoutes.Capabilities,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        var health = service.CreateResponse(
            BridgeRoutes.Health,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"));
        using var capabilitiesJson = JsonDocument.Parse(JsonSerializer.Serialize(
            capabilities,
            CadJson.Options));
        using var healthJson = JsonDocument.Parse(JsonSerializer.Serialize(
            health,
            CadJson.Options));
        var inventory = capabilitiesJson.RootElement
            .GetProperty("data")
            .GetProperty("capabilities");

        Assert.True(inventory.GetProperty("drawing.status").GetBoolean());
        Assert.True(inventory.GetProperty("drawing.list_documents").GetBoolean());
        Assert.False(inventory.GetProperty("drawing.active_document").GetBoolean());
        Assert.False(inventory.GetProperty("drawing.units").GetBoolean());
        Assert.False(inventory.GetProperty("drawing.bounds").GetBoolean());
        Assert.False(inventory.GetProperty("drawing.layouts").GetBoolean());
        Assert.False(inventory.GetProperty("drawing.system_metadata").GetBoolean());
        Assert.True(inventory.GetProperty("dwg.read").GetBoolean());
        Assert.True(healthJson.RootElement.GetProperty("data").GetProperty("dwgRead").GetBoolean());
        Assert.False(
            healthJson.RootElement.GetProperty("data").GetProperty("dwgWrite").GetBoolean());
    }

    [Fact]
    public async Task ContextProbePostUsesBoundedDispatcherAndPreservesCorrelation()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var request = new ContextProbeRequest(
            CadProtocol.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddSeconds(5),
            fixture.Service.InstanceId,
            ExpectedDocumentId: null);
        var responseTask = fixture.SendJsonAsync(BridgeRoutes.ContextProbe, request);
        Assert.True(SpinWait.SpinUntil(
            () => fixture.Dispatcher.GetSnapshot().QueueDepth == 1,
            TimeSpan.FromSeconds(1)));
        Assert.Equal(
            DocumentDispatchTakeResult.Started,
            fixture.Dispatcher.TryTakeNext(out var item));
        Assert.NotNull(item);
        Assert.True(fixture.Dispatcher.Complete(
            item!,
            CadResultEnvelope.Ok(
                request.RequestId,
                request.TraceId,
                "AutoCAD document context dispatch completed",
                new ContextProbeData(
                    fixture.Service.InstanceId,
                    item.DispatchId,
                    MainThreadVerified: true,
                    ExecutionContext: "DOCUMENT_COMMAND_CONTEXT",
                    DocumentState: "ACTIVE",
                    "doc_AAAAAAAAAAAAAAAAAAAAAA",
                    IsQuiescent: true,
                    QueueDelayMs: 0,
                    ExecutionMs: 0))));

        var response = await responseTask;

        Assert.Equal((200, "OK"), (response.StatusCode, response.Status));
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(request.RequestId, document.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(
            "DOCUMENT_COMMAND_CONTEXT",
            document.RootElement.GetProperty("data").GetProperty("executionContext").GetString());
    }

    [Fact]
    public async Task ContextProbeRejectsUnknownFieldsAndWrongMethod()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var invalid = "{" +
            $"\"schemaVersion\":\"1.0\",\"requestId\":\"{Guid.NewGuid():D}\"," +
            $"\"traceId\":\"{Guid.NewGuid():D}\"," +
            $"\"deadlineUtc\":\"{DateTimeOffset.UtcNow.AddSeconds(5):O}\"," +
            $"\"expectedInstanceId\":\"{fixture.Service.InstanceId}\"," +
            "\"expectedDocumentId\":null,\"unexpected\":true}";

        var invalidResponse = await fixture.SendJsonAsync(BridgeRoutes.ContextProbe, invalid);
        var wrongMethod = await fixture.SendAsync("GET", BridgeRoutes.ContextProbe, fixture.Token);

        Assert.Equal((400, "INVALID_ARGUMENT"),
            (invalidResponse.StatusCode, invalidResponse.Status));
        Assert.Equal((405, "METHOD_NOT_ALLOWED"),
            (wrongMethod.StatusCode, wrongMethod.Status));
    }

    [Fact]
    public async Task DrawingInspectionPostUsesSameDispatcherAndStrictContract()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var request = new DrawingInspectRequest(
            CadProtocol.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddSeconds(5),
            fixture.Service.InstanceId,
            DrawingOperation.Status,
            ExpectedDocumentId: null);
        var responseTask = fixture.SendJsonAsync(BridgeRoutes.DrawingInspect, request);
        Assert.True(SpinWait.SpinUntil(
            () => fixture.Dispatcher.GetSnapshot().QueueDepth == 1,
            TimeSpan.FromSeconds(1)));
        Assert.Equal(
            DocumentDispatchTakeResult.Started,
            fixture.Dispatcher.TryTakeNext(out var item));
        Assert.NotNull(item);
        Assert.Equal(DocumentDispatchScope.Application, item.Scope);
        Assert.True(fixture.Dispatcher.Complete(
            item,
            CadResultEnvelope.Ok(
                request.RequestId,
                request.TraceId,
                "AutoCAD drawing status inspected",
                new DrawingStatusData(
                    fixture.Service.InstanceId,
                    item.DispatchId,
                    request.Operation,
                    MainThreadVerified: true,
                    DrawingExecutionContext.ApplicationContext,
                    ActiveDocumentId: null,
                    QueueDelayMs: 0,
                    ExecutionMs: 0,
                    DrawingReadMode.ReadOnly,
                    TransactionUsed: false,
                    RuntimeState: "READY",
                    DocumentState: "NO_ACTIVE_DOCUMENT",
                    DocumentCount: 0))));

        var response = await responseTask;
        using var document = JsonDocument.Parse(response.Body);

        Assert.Equal((200, "OK"), (response.StatusCode, response.Status));
        Assert.Equal(request.RequestId, document.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(
            "status",
            document.RootElement.GetProperty("data").GetProperty("operation").GetString());

        var invalid = JsonSerializer.Serialize(request, CadJson.Options)[..^1]
            + ",\"arguments\":{}}";
        var invalidResponse = await fixture.SendJsonAsync(BridgeRoutes.DrawingInspect, invalid);
        var wrongMethod = await fixture.SendAsync(
            "GET",
            BridgeRoutes.DrawingInspect,
            fixture.Token);
        Assert.Equal("INVALID_ARGUMENT", invalidResponse.Status);
        Assert.Equal("METHOD_NOT_ALLOWED", wrongMethod.Status);
    }

    [Fact]
    public async Task DrawingInspectionPostRejectsMissingDocumentSelector()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var request = new DrawingInspectRequest(
            CadProtocol.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddSeconds(5),
            fixture.Service.InstanceId,
            DrawingOperation.Units,
            ExpectedDocumentId: null);

        var response = await fixture.SendJsonAsync(BridgeRoutes.DrawingInspect, request);

        Assert.Equal((400, "INVALID_ARGUMENT"), (response.StatusCode, response.Status));
        Assert.Equal(0, fixture.Dispatcher.GetSnapshot().QueueDepth);
    }

    private static LoopbackBridgeServer CreateServer(
        int port,
        BridgeTokenCredential credential,
        out BridgeInstanceResponseService service,
        out DocumentContextDispatcher dispatcher,
        LoopbackBridgeLimits? limits = null)
    {
        dispatcher = new DocumentContextDispatcher();
        service = CreateResponseService(BridgePluginState.Listening, dispatcher);
        dispatcher.ConfigureInstance(service.InstanceId);
        dispatcher.MarkReady(activeDocumentExists: true, documentIsQuiescent: true);
        return new LoopbackBridgeServer(
            new LoopbackBridgeOptions(port, limits),
            credential,
            service,
            dispatcher);
    }

    private static BridgeInstanceResponseService CreateResponseService(
        BridgePluginState state,
        IBridgeContextStateProvider? contextStateProvider = null) =>
        new(
            new BridgeInstanceMetadata(
                "CadMax.AutoCAD.Bridge",
                "0.1.0",
                "0.1.0",
                2025,
                "R25.0.58.0.0",
                "net8.0-windows",
                IsAutoCADHostProcess: true,
                DevelopmentHost: false),
            state,
            contextStateProvider);

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<byte[]> ReadToCloseAsync(NetworkStream stream)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read == 0)
                {
                    return memory.ToArray();
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
            }
        }
        catch (IOException) when (memory.Length > 0)
        {
            // Windows may reset after a bounded error when unread attack bytes remain.
            return memory.ToArray();
        }
    }

    private static RawResponse Parse(byte[] raw)
    {
        var text = Encoding.UTF8.GetString(raw);
        var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(headerEnd > 0);
        var statusLine = text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
        var statusCode = int.Parse(statusLine.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        var body = text[(headerEnd + 4)..];
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var instanceId = root.GetProperty("data").TryGetProperty("instanceId", out var instance)
            ? instance.GetString()
            : null;
        var heartbeat = root.GetProperty("data").TryGetProperty(
            "heartbeatSequence",
            out var sequence)
            ? sequence.GetInt64()
            : 0;
        return new RawResponse(
            statusCode,
            root.GetProperty("status").GetString() ?? "UNKNOWN",
            instanceId,
            heartbeat,
            body,
            text);
    }

    private sealed record RawResponse(
        int StatusCode,
        string Status,
        string? InstanceId,
        long HeartbeatSequence,
        string Body,
        string Raw);

    private sealed class ServerFixture : IAsyncDisposable
    {
        private readonly BridgeTokenCredential credential;

        private ServerFixture(
            int port,
            string token,
            BridgeTokenCredential credential,
            LoopbackBridgeServer server,
            BridgeInstanceResponseService service,
            DocumentContextDispatcher dispatcher)
        {
            Port = port;
            Token = token;
            this.credential = credential;
            Server = server;
            Service = service;
            Dispatcher = dispatcher;
        }

        public int Port { get; }
        public string Token { get; }
        public LoopbackBridgeServer Server { get; }
        public BridgeInstanceResponseService Service { get; }
        public DocumentContextDispatcher Dispatcher { get; }

        public static async Task<ServerFixture> StartAsync(LoopbackBridgeLimits? limits = null)
        {
            var port = FindFreePort();
            var token = BridgeTokenTests.CreateToken();
            Assert.True(BridgeTokenCredential.TryCreate(token, out var credential));
            var validCredential = credential
                ?? throw new InvalidOperationException("TEST_TOKEN_CREATION_FAILED");
            var server = CreateServer(
                port,
                validCredential,
                out var service,
                out var dispatcher,
                limits);
            Assert.Null(server.TryStart());
            Assert.True(await server.RunAuthenticatedSelfProbeAsync(CancellationToken.None));
            service.SetPluginState(BridgePluginState.Ready);
            return new ServerFixture(
                port,
                token,
                validCredential,
                server,
                service,
                dispatcher);
        }

        public async Task<RawResponse> SendAsync(string method, string route, string? token) =>
            await SendRawAsync(method, route, token, string.Empty);

        public async Task<RawResponse> SendJsonAsync(string route, object payload)
        {
            var json = payload is string text
                ? text
                : JsonSerializer.Serialize(payload, CadJson.Options);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, Port);
            await using var stream = client.GetStream();
            var body = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes(
                $"POST {route} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{Port}\r\n" +
                $"Authorization: Bearer {Token}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            return Parse(await ReadToCloseAsync(stream));
        }

        public async Task<RawResponse> SendRawAsync(
            string method,
            string route,
            string? token,
            string extraHeaders)
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, Port);
            await using var stream = client.GetStream();
            var authorization = token is null ? string.Empty : $"Authorization: Bearer {token}\r\n";
            var request = Encoding.ASCII.GetBytes(
                $"{method} {route} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{Port}\r\n" +
                authorization +
                extraHeaders +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(request);
            return Parse(await ReadToCloseAsync(stream));
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            Dispatcher.Dispose();
            credential.Dispose();
        }
    }
}
