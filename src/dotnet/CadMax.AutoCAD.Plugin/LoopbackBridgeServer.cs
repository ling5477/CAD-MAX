using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CadMax.Bridge.Core;
using CadMax.Contracts;

namespace CadMax.AutoCAD.Plugin;

/// <summary>
/// Bounded HTTP limits. Defaults are hard maximums; callers may only tighten them.
/// </summary>
public sealed record LoopbackBridgeLimits(
    int MaxRequestLineBytes = 2048,
    int MaxHeaderBytes = 8192,
    int MaxHeaderCount = 32,
    int MaxRequestBodyBytes = 4096,
    int MaxResponseBytes = 65_536,
    int ReadTimeoutMs = 2000,
    int WriteTimeoutMs = 2000,
    int MaxConcurrentConnections = 40,
    int ListenBacklog = 64)
{
    public void Validate()
    {
        if (MaxRequestLineBytes is < 128 or > 2048
            || MaxHeaderBytes is < 512 or > 8192
            || MaxHeaderBytes <= MaxRequestLineBytes
            || MaxHeaderCount is < 4 or > 32
            || MaxRequestBodyBytes is < 512 or > 4096
            || MaxResponseBytes is < 1024 or > 65_536
            || ReadTimeoutMs is < 100 or > 2000
            || WriteTimeoutMs is < 100 or > 2000
            || MaxConcurrentConnections is < 1 or > 40
            || ListenBacklog is < 1 or > 64)
        {
            throw new InvalidOperationException("BRIDGE_LIMITS_INVALID");
        }
    }
}

/// <summary>Fixed production listener options. The bind address is never configurable.</summary>
public sealed record LoopbackBridgeOptions(
    int Port = 47_770,
    LoopbackBridgeLimits? Limits = null)
{
    public LoopbackBridgeLimits EffectiveLimits => Limits ?? new LoopbackBridgeLimits();

    public void Validate()
    {
        if (Port is < 1 or > 65_535)
        {
            throw new InvalidOperationException("BRIDGE_PORT_INVALID");
        }

        EffectiveLimits.Validate();
    }
}

/// <summary>Abstraction used to test lifecycle ownership without Autodesk dependencies.</summary>
public interface ILoopbackBridgeServer : IAsyncDisposable
{
    int ActiveConnectionCount { get; }

    string? TryStart();

    Task<bool> RunAuthenticatedSelfProbeAsync(CancellationToken cancellationToken);

    Task<bool> StopAsync(TimeSpan deadline);
}

/// <summary>Creates one process-owned loopback server.</summary>
public interface ILoopbackBridgeServerFactory
{
    ILoopbackBridgeServer Create(
        LoopbackBridgeOptions options,
        BridgeTokenCredential credential,
        BridgeInstanceResponseService responseService,
        DocumentContextDispatcher contextDispatcher,
        Action<string> listenerFault);
}

public sealed class LoopbackBridgeServerFactory : ILoopbackBridgeServerFactory
{
    public ILoopbackBridgeServer Create(
        LoopbackBridgeOptions options,
        BridgeTokenCredential credential,
        BridgeInstanceResponseService responseService,
        DocumentContextDispatcher contextDispatcher,
        Action<string> listenerFault) =>
        new LoopbackBridgeServer(
            options,
            credential,
            responseService,
            contextDispatcher,
            listenerFault);
}

/// <summary>
/// Minimal HTTP/1.1 GET server for the four fixed process-level routes. It binds directly to
/// <see cref="IPAddress.Loopback"/>, creates one accept loop, and never touches Autodesk APIs.
/// </summary>
public sealed class LoopbackBridgeServer : ILoopbackBridgeServer
{
    public const string SelfProbeHeaderName = "x-cad-max-self-probe";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private readonly LoopbackBridgeOptions options;
    private readonly LoopbackBridgeLimits limits;
    private readonly BridgeTokenCredential credential;
    private readonly BridgeInstanceResponseService responseService;
    private readonly DocumentContextDispatcher contextDispatcher;
    private readonly Action<string> listenerFault;
    private readonly SemaphoreSlim connectionSlots;
    private readonly ConcurrentDictionary<long, TcpClient> activeClients = new();
    private readonly ActiveConnectionTaskRegistry activeTasks = new();
    private readonly byte[] selfProbeNonce = RandomNumberGenerator.GetBytes(32);
    private readonly object lifecycleLock = new();
    private TcpListener? listener;
    private CancellationTokenSource? shutdown;
    private Task? acceptLoop;
    private long nextConnectionId;
    private bool disposed;

    /// <summary>The only address the production listener can bind.</summary>
    public static IPAddress BindingAddress => IPAddress.Loopback;

    public LoopbackBridgeServer(
        LoopbackBridgeOptions options,
        BridgeTokenCredential credential,
        BridgeInstanceResponseService responseService,
        DocumentContextDispatcher? contextDispatcher = null,
        Action<string>? listenerFault = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        limits = options.EffectiveLimits;
        this.credential = credential ?? throw new ArgumentNullException(nameof(credential));
        this.responseService = responseService
            ?? throw new ArgumentNullException(nameof(responseService));
        this.contextDispatcher = contextDispatcher ?? new DocumentContextDispatcher();
        this.listenerFault = listenerFault ?? (_ => { });
        connectionSlots = new SemaphoreSlim(limits.MaxConcurrentConnections);
    }

    public int ActiveConnectionCount => activeClients.Count;

    internal int ActiveTaskCount => activeTasks.Count;

    /// <summary>Bind the exact loopback endpoint and start the single accept loop.</summary>
    public string? TryStart()
    {
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (listener is not null)
            {
                return null;
            }

            var candidate = new TcpListener(IPAddress.Loopback, options.Port);
            try
            {
                candidate.Start(limits.ListenBacklog);
                var cancellation = new CancellationTokenSource();
                listener = candidate;
                shutdown = cancellation;
                acceptLoop = AcceptLoopAsync(candidate, cancellation.Token);
                return null;
            }
            catch (SocketException exception)
                when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                candidate.Stop();
                return "PORT_IN_USE";
            }
            catch (Exception)
            {
                candidate.Stop();
                return "LISTENER_START_FAILED";
            }
        }
    }

    /// <summary>
    /// Probe every canonical route through the bound socket with both the Bearer token and an
    /// ephemeral in-memory nonce. The nonce allows only the startup probe to read LISTENING data.
    /// </summary>
    public async Task<bool> RunAuthenticatedSelfProbeAsync(
        CancellationToken cancellationToken)
    {
        string? observedInstanceId = null;
        foreach (var route in BridgeRoutes.Production)
        {
            var instanceId = await ProbeRouteAsync(route, cancellationToken).ConfigureAwait(false);
            if (instanceId is null)
            {
                return false;
            }

            observedInstanceId ??= instanceId;
            if (!string.Equals(observedInstanceId, instanceId, StringComparison.Ordinal)
                || !string.Equals(instanceId, responseService.InstanceId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Stop accepting, close sockets, and wait only until the fixed caller deadline.</summary>
    public async Task<bool> StopAsync(TimeSpan deadline)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);

        TcpListener? listenerToStop;
        CancellationTokenSource? shutdownToCancel;
        Task? acceptLoopToWait;
        lock (lifecycleLock)
        {
            listenerToStop = listener;
            shutdownToCancel = shutdown;
            acceptLoopToWait = acceptLoop;
            listener = null;
            shutdown = null;
            acceptLoop = null;
        }

        var stopwatch = Stopwatch.StartNew();
        shutdownToCancel?.Cancel();
        listenerToStop?.Stop();

        // The accept loop owns registration. Drain it first so the connection-task snapshot cannot
        // race a late registration after shutdown has already selected the tasks to await.
        var acceptLoopDrained = await DrainTasksAsync(
            acceptLoopToWait is null ? [] : [acceptLoopToWait],
            Remaining(deadline, stopwatch)).ConfigureAwait(false);

        CloseActiveClients();
        var connectionTasksDrained = await DrainTasksAsync(
            activeTasks.SnapshotRunning(),
            Remaining(deadline, stopwatch)).ConfigureAwait(false);
        if (!connectionTasksDrained)
        {
            CloseActiveClients();
        }

        _ = activeTasks.SnapshotRunning();
        shutdownToCancel?.Dispose();
        return acceptLoopDrained
            && connectionTasksDrained
            && ActiveConnectionCount == 0
            && ActiveTaskCount == 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        _ = await StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        disposed = true;
        connectionSlots.Dispose();
        CryptographicOperations.ZeroMemory(selfProbeNonce);
        GC.SuppressFinalize(this);
    }

    private async Task AcceptLoopAsync(TcpListener activeListener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await activeListener.AcceptTcpClientAsync(cancellationToken)
                    .ConfigureAwait(false);
                ConfigureClient(client);
                if (!connectionSlots.Wait(0, CancellationToken.None))
                {
                    RejectBusyAndClose(client);
                    continue;
                }

                var connectionId = Interlocked.Increment(ref nextConnectionId);
                if (connectionId <= 0 || !activeClients.TryAdd(connectionId, client))
                {
                    RejectRegistrationAndClose(client, releaseConnectionSlot: true);
                    listenerFault("CONNECTION_REGISTRATION_FAILED");
                    continue;
                }

                var connectionTask = HandleTrackedConnectionAsync(
                    connectionId,
                    client,
                    cancellationToken);
                if (!activeTasks.TryRegister(connectionId, connectionTask))
                {
                    RejectRegistrationAndClose(client, releaseConnectionSlot: false);
                    listenerFault("CONNECTION_REGISTRATION_FAILED");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected shutdown.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected shutdown.
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected shutdown.
        }
        catch (Exception)
        {
            listenerFault("ACCEPT_LOOP_FAILED");
        }
    }

    private async Task HandleTrackedConnectionAsync(
        long connectionId,
        TcpClient client,
        CancellationToken serverCancellation)
    {
        try
        {
            await HandleConnectionAsync(client, serverCancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Per-connection failures are contained; raw exceptions are never returned or logged.
        }
        finally
        {
            client.Dispose();
            _ = activeClients.TryRemove(connectionId, out _);
            connectionSlots.Release();
        }
    }

    private static async Task<bool> DrainTasksAsync(
        IReadOnlyCollection<Task> tasks,
        TimeSpan remaining)
    {
        if (tasks.Count == 0)
        {
            return true;
        }

        if (remaining <= TimeSpan.Zero)
        {
            return false;
        }

        var drainTask = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(
            drainTask,
            Task.Delay(remaining, CancellationToken.None)).ConfigureAwait(false);
        if (!ReferenceEquals(completed, drainTask))
        {
            return false;
        }

        try
        {
            await drainTask.ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static TimeSpan Remaining(TimeSpan deadline, Stopwatch stopwatch)
    {
        var remaining = deadline - stopwatch.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private async Task HandleConnectionAsync(
        TcpClient client,
        CancellationToken serverCancellation)
    {
        using var stream = client.GetStream();
        var parsed = await ReadRequestAsync(stream, serverCancellation).ConfigureAwait(false);
        if (parsed.ErrorStatus is not null)
        {
            await WriteFailureAsync(
                stream,
                parsed.HttpStatus,
                parsed.ErrorStatus.Value,
                parsed.SafeMessage,
                serverCancellation).ConfigureAwait(false);
            return;
        }

        var request = parsed.Request!;
        if (!credential.IsAuthorized(request.Authorization))
        {
            await WriteFailureAsync(
                stream,
                401,
                CadStatus.Unauthorized,
                "CAD-MAX bridge authorization failed",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(request.HttpVersion, "HTTP/1.1", StringComparison.Ordinal))
        {
            await WriteFailureAsync(
                stream,
                400,
                CadStatus.InvalidArgument,
                "CAD-MAX bridge requires HTTP/1.1",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (request.Target.Contains('?'))
        {
            await WriteFailureAsync(
                stream,
                400,
                CadStatus.QueryNotAllowed,
                "CAD-MAX bridge query strings are not allowed",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (string.Equals(request.Target, BridgeRoutes.ContextProbe, StringComparison.Ordinal))
        {
            await HandleContextProbeAsync(
                stream,
                request,
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (!BridgeRoutes.Production.Contains(request.Target))
        {
            await WriteFailureAsync(
                stream,
                404,
                CadStatus.RouteNotFound,
                "CAD-MAX bridge route was not found",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(request.Method, "GET", StringComparison.Ordinal))
        {
            await WriteFailureAsync(
                stream,
                405,
                CadStatus.MethodNotAllowed,
                "CAD-MAX bridge permits GET for this route",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (request.HasTransferEncoding || request.ContentLength != 0 || request.Body.Length != 0)
        {
            await WriteFailureAsync(
                stream,
                400,
                CadStatus.RequestBodyNotAllowed,
                "CAD-MAX bridge request bodies are not allowed",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        var isStartupSelfProbe = responseService.PluginState == BridgePluginState.Listening
            && BridgeTokenCredential.FixedTimeEqualsBase64Url(
                request.SelfProbeNonce,
                selfProbeNonce);
        var requestId = Guid.NewGuid().ToString("D");
        var traceId = Guid.NewGuid().ToString("D");
        var envelope = responseService.CreateResponse(
            request.Target,
            requestId,
            traceId,
            isStartupSelfProbe);
        await WriteEnvelopeAsync(
            stream,
            envelope.Success ? 200 : HttpStatusFor(envelope.Status),
            envelope,
            serverCancellation).ConfigureAwait(false);
    }

    private async Task HandleContextProbeAsync(
        NetworkStream stream,
        ParsedHttpRequest request,
        CancellationToken serverCancellation)
    {
        if (!string.Equals(request.Method, "POST", StringComparison.Ordinal))
        {
            await WriteFailureAsync(
                stream,
                405,
                CadStatus.MethodNotAllowed,
                "CAD-MAX context probe requires POST",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        if (request.HasTransferEncoding
            || request.ContentLength <= 0
            || request.ContentLength > limits.MaxRequestBodyBytes
            || request.Body.Length != request.ContentLength
            || request.ContentType is null
            || !request.ContentType.StartsWith(
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            await WriteFailureAsync(
                stream,
                400,
                CadStatus.InvalidArgument,
                "CAD-MAX context probe request body is invalid",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        ContextProbeRequest probeRequest;
        try
        {
            probeRequest = JsonSerializer.Deserialize<ContextProbeRequest>(
                    request.Body,
                    CadJson.Options)
                ?? throw new JsonException("CONTEXT_REQUEST_MISSING");
        }
        catch (JsonException)
        {
            await WriteFailureAsync(
                stream,
                400,
                CadStatus.InvalidArgument,
                "CAD-MAX context probe request body is invalid",
                serverCancellation).ConfigureAwait(false);
            return;
        }

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            serverCancellation);
        var dispatchTask = contextDispatcher.EnqueueAsync(
            probeRequest,
            requestCancellation.Token);
        var disconnectTask = WaitForDisconnectAsync(stream, requestCancellation.Token);
        var completed = await Task.WhenAny(dispatchTask, disconnectTask).ConfigureAwait(false);
        if (ReferenceEquals(completed, disconnectTask))
        {
            requestCancellation.Cancel();
            _ = await dispatchTask.ConfigureAwait(false);
            return;
        }

        requestCancellation.Cancel();
        var envelope = await dispatchTask.ConfigureAwait(false);
        await WriteEnvelopeAsync(
            stream,
            envelope.Success ? 200 : HttpStatusFor(envelope.Status),
            envelope,
            serverCancellation).ConfigureAwait(false);
    }

    private static async Task WaitForDisconnectAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        try
        {
            _ = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Response completion cancels the disconnect monitor.
        }
        catch (IOException)
        {
            // A reset peer is equivalent to cancellation.
        }
    }

    private async Task<ParsedRequestResult> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken serverCancellation)
    {
        var buffer = new byte[limits.MaxHeaderBytes + limits.MaxRequestBodyBytes + 1];
        var total = 0;
        var headerEnd = -1;
        ParsedRequestResult? parsedHeaders = null;
        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
        readDeadline.CancelAfter(limits.ReadTimeoutMs);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), readDeadline.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return ParsedRequestResult.Invalid(
                        400,
                        CadStatus.InvalidArgument,
                        "CAD-MAX bridge received an invalid request");
                }

                total += read;
                var requestLineEnd = FindSequence(buffer.AsSpan(0, total), "\r\n"u8);
                if (requestLineEnd < 0 && total > limits.MaxRequestLineBytes)
                {
                    return ParsedRequestResult.Invalid(
                        414,
                        CadStatus.RequestTargetTooLong,
                        "CAD-MAX bridge request target is too long");
                }

                if (requestLineEnd > limits.MaxRequestLineBytes)
                {
                    return ParsedRequestResult.Invalid(
                        414,
                        CadStatus.RequestTargetTooLong,
                        "CAD-MAX bridge request target is too long");
                }

                if (headerEnd < 0)
                {
                    headerEnd = FindSequence(buffer.AsSpan(0, total), "\r\n\r\n"u8);
                    if (headerEnd >= 0)
                    {
                        if (headerEnd + 4 > limits.MaxHeaderBytes)
                        {
                            return ParsedRequestResult.Invalid(
                                431,
                                CadStatus.HeadersTooLarge,
                                "CAD-MAX bridge headers are too large");
                        }

                        parsedHeaders = ParseRequestHeaders(
                            buffer.AsSpan(0, headerEnd),
                            headerEnd);
                        if (parsedHeaders.ErrorStatus is not null)
                        {
                            return parsedHeaders;
                        }

                        var contentLength = parsedHeaders.Request!.ContentLength;
                        if (BridgeRoutes.Production.Contains(parsedHeaders.Request.Target)
                            && (contentLength != 0
                                || parsedHeaders.Request.HasTransferEncoding))
                        {
                            return ParsedRequestResult.Invalid(
                                400,
                                CadStatus.RequestBodyNotAllowed,
                                "CAD-MAX bridge request bodies are not allowed");
                        }

                        if (contentLength > limits.MaxRequestBodyBytes)
                        {
                            return ParsedRequestResult.Invalid(
                                400,
                                CadStatus.InvalidArgument,
                                "CAD-MAX bridge request body is invalid");
                        }
                    }
                }

                if (headerEnd < 0 && total > limits.MaxHeaderBytes)
                {
                    return ParsedRequestResult.Invalid(
                        431,
                        CadStatus.HeadersTooLarge,
                        "CAD-MAX bridge headers are too large");
                }

                if (headerEnd >= 0 && parsedHeaders is not null)
                {
                    var bodyStart = headerEnd + 4;
                    var expectedTotal = bodyStart + checked((int)parsedHeaders.Request!.ContentLength);
                    if (total > expectedTotal)
                    {
                        return ParsedRequestResult.Invalid(
                            400,
                            CadStatus.InvalidArgument,
                            "CAD-MAX bridge request body is invalid");
                    }

                    if (total == expectedTotal)
                    {
                        return parsedHeaders.WithBody(buffer.AsSpan(bodyStart, total - bodyStart));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!serverCancellation.IsCancellationRequested)
        {
            return ParsedRequestResult.Invalid(
                408,
                CadStatus.RequestTimeout,
                "CAD-MAX bridge request timed out");
        }
    }

    private ParsedRequestResult ParseRequestHeaders(ReadOnlySpan<byte> headerBytes, int headerEnd)
    {
        foreach (var value in headerBytes)
        {
            if (value >= 128 || value == 0 || (value < 32 && value is not (9 or 10 or 13)))
            {
                return ParsedRequestResult.Invalid(
                    400,
                    CadStatus.InvalidArgument,
                    "CAD-MAX bridge received an invalid request");
            }
        }

        var text = Encoding.ASCII.GetString(headerBytes);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        if (lines.Length < 2 || lines.Length - 1 > limits.MaxHeaderCount)
        {
            return ParsedRequestResult.Invalid(
                431,
                CadStatus.HeadersTooLarge,
                "CAD-MAX bridge headers are too large");
        }

        var requestParts = lines[0].Split(' ', StringSplitOptions.None);
        if (requestParts.Length != 3
            || requestParts.Any(string.IsNullOrWhiteSpace)
            || !requestParts[1].StartsWith('/'))
        {
            return ParsedRequestResult.Invalid(
                400,
                CadStatus.InvalidArgument,
                "CAD-MAX bridge received an invalid request");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                return ParsedRequestResult.Invalid(
                    400,
                    CadStatus.InvalidArgument,
                    "CAD-MAX bridge received an invalid request");
            }

            var colon = line.IndexOf(':');
            if (colon <= 0
                || !IsHeaderName(line.AsSpan(0, colon))
                || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim()))
            {
                return ParsedRequestResult.Invalid(
                    400,
                    CadStatus.InvalidArgument,
                    "CAD-MAX bridge received an invalid request");
            }
        }

        if (!headers.ContainsKey("Host"))
        {
            return ParsedRequestResult.Invalid(
                400,
                CadStatus.InvalidArgument,
                "CAD-MAX bridge received an invalid request");
        }

        long contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var contentLengthValue)
            && (!long.TryParse(contentLengthValue, out contentLength) || contentLength < 0))
        {
            return ParsedRequestResult.Invalid(
                400,
                CadStatus.RequestBodyNotAllowed,
                "CAD-MAX bridge request bodies are not allowed");
        }

        return ParsedRequestResult.Valid(new ParsedHttpRequest(
            requestParts[0],
            requestParts[1],
            requestParts[2],
            headers.GetValueOrDefault("Authorization"),
            headers.GetValueOrDefault(SelfProbeHeaderName),
            headers.GetValueOrDefault("Content-Type"),
            headers.ContainsKey("Transfer-Encoding"),
            contentLength,
            []));
    }

    private async Task<string?> ProbeRouteAsync(
        string route,
        CancellationToken cancellationToken)
    {
        try
        {
            using var probeDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeDeadline.CancelAfter(limits.ReadTimeoutMs);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, options.Port, probeDeadline.Token)
                .ConfigureAwait(false);
            ConfigureClient(client);
            await using var stream = client.GetStream();
            var nonce = EncodeBase64Url(selfProbeNonce);
            var request = Encoding.ASCII.GetBytes(
                $"GET {route} HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{options.Port}\r\n" +
                $"Authorization: {credential.CreateSelfProbeAuthorizationHeader()}\r\n" +
                $"X-CAD-Max-Self-Probe: {nonce}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(request, probeDeadline.Token).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(request);

            var response = new byte[limits.MaxResponseBytes + 2049];
            var total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(response.AsMemory(total), probeDeadline.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total == response.Length)
                {
                    return null;
                }
            }

            var headerEnd = FindSequence(response.AsSpan(0, total), "\r\n\r\n"u8);
            if (headerEnd < 0
                || !response.AsSpan(0, headerEnd).StartsWith("HTTP/1.1 200 "u8))
            {
                return null;
            }

            using var document = JsonDocument.Parse(
                response.AsMemory(headerEnd + 4, total - headerEnd - 4));
            var root = document.RootElement;
            if (!root.GetProperty("success").GetBoolean()
                || root.GetProperty("status").GetString() != "OK")
            {
                return null;
            }

            return root.GetProperty("data").GetProperty("instanceId").GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RejectBusyAndClose(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var envelope = CadResultEnvelope.Failure(
                    Guid.NewGuid().ToString("D"),
                    Guid.NewGuid().ToString("D"),
                    CadStatus.ServerBusy,
                    "CAD-MAX bridge connection capacity is exhausted");
                var bytes = BuildHttpResponse(503, envelope);
                stream.Write(bytes);
            }
        }
        catch (Exception)
        {
            // Busy connections are closed even if the peer does not read the response.
        }
    }

    private void RejectRegistrationAndClose(TcpClient client, bool releaseConnectionSlot)
    {
        try
        {
            client.Dispose();
        }
        catch (Exception)
        {
            // Registration failures are fail-closed even when socket disposal also fails.
        }
        finally
        {
            if (releaseConnectionSlot)
            {
                connectionSlots.Release();
            }
        }
    }

    private Task WriteFailureAsync(
        NetworkStream stream,
        int httpStatus,
        CadStatus status,
        string message,
        CancellationToken cancellationToken) =>
        WriteEnvelopeAsync(
            stream,
            httpStatus,
            CadResultEnvelope.Failure(
                Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"),
                status,
                message),
            cancellationToken);

    private async Task WriteEnvelopeAsync(
        NetworkStream stream,
        int httpStatus,
        CadResultEnvelope envelope,
        CancellationToken serverCancellation)
    {
        var response = BuildHttpResponse(httpStatus, envelope);
        using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
        writeDeadline.CancelAfter(limits.WriteTimeoutMs);
        await stream.WriteAsync(response, writeDeadline.Token).ConfigureAwait(false);
    }

    private byte[] BuildHttpResponse(int httpStatus, CadResultEnvelope envelope)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, CadJson.Options);
        if (json.Length > limits.MaxResponseBytes)
        {
            httpStatus = 500;
            json = JsonSerializer.SerializeToUtf8Bytes(
                CadResultEnvelope.Failure(
                    Guid.NewGuid().ToString("D"),
                    Guid.NewGuid().ToString("D"),
                    CadStatus.InternalError,
                    "CAD-MAX bridge response limit was exceeded"),
                CadJson.Options);
        }

        var extraHeader = httpStatus switch
        {
            401 => "WWW-Authenticate: Bearer\r\n",
            405 => "Allow: GET\r\n",
            _ => string.Empty,
        };
        var header = Utf8WithoutBom.GetBytes(
            $"HTTP/1.1 {httpStatus} {ReasonPhrase(httpStatus)}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {json.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            extraHeader +
            "Connection: close\r\n\r\n");
        var response = new byte[header.Length + json.Length];
        Buffer.BlockCopy(header, 0, response, 0, header.Length);
        Buffer.BlockCopy(json, 0, response, header.Length, json.Length);
        return response;
    }

    private static void ConfigureClient(TcpClient client)
    {
        client.NoDelay = true;
        client.ReceiveBufferSize = 8192;
        client.SendBufferSize = 65_536;
    }

    private void CloseActiveClients()
    {
        foreach (var client in activeClients.Values)
        {
            try
            {
                client.Client.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
                // The socket may already be closed.
            }

            client.Dispose();
        }
    }

    private static int FindSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> sequence) =>
        source.IndexOf(sequence);

    private static bool IsHeaderName(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    private static int HttpStatusFor(CadStatus status) => status switch
    {
        CadStatus.Unauthorized => 401,
        CadStatus.MethodNotAllowed => 405,
        CadStatus.RouteNotFound => 404,
        CadStatus.HeadersTooLarge => 431,
        CadStatus.RequestTargetTooLong => 414,
        CadStatus.RequestTimeout => 408,
        CadStatus.Timeout or CadStatus.Cancelled => 408,
        CadStatus.NoActiveDocument
            or CadStatus.DocumentNotActive
            or CadStatus.DocumentDestroyed
            or CadStatus.DocumentNotFound
            or CadStatus.ApplicationModal
            or CadStatus.DocumentBusy => 409,
        CadStatus.InstanceMismatch => 409,
        CadStatus.ServerBusy
            or CadStatus.QueueFull
            or CadStatus.DispatcherNotReady
            or CadStatus.BridgeNotReady
            or CadStatus.BridgeStopping => 503,
        CadStatus.MainThreadDispatchFailed
            or CadStatus.CommandContextFailed
            or CadStatus.InternalError => 500,
        _ => 400,
    };

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        408 => "Request Timeout",
        409 => "Conflict",
        414 => "URI Too Long",
        431 => "Request Header Fields Too Large",
        500 => "Internal Server Error",
        503 => "Service Unavailable",
        _ => "Error",
    };

    private static string EncodeBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record ParsedHttpRequest(
        string Method,
        string Target,
        string HttpVersion,
        string? Authorization,
        string? SelfProbeNonce,
        string? ContentType,
        bool HasTransferEncoding,
        long ContentLength,
        byte[] Body);

    private sealed record ParsedRequestResult(
        ParsedHttpRequest? Request,
        int HttpStatus,
        CadStatus? ErrorStatus,
        string SafeMessage)
    {
        public static ParsedRequestResult Valid(ParsedHttpRequest request) =>
            new(request, 200, null, string.Empty);

        public static ParsedRequestResult Invalid(
            int httpStatus,
            CadStatus status,
            string safeMessage) =>
            new(null, httpStatus, status, safeMessage);

        public ParsedRequestResult WithBody(ReadOnlySpan<byte> body)
        {
            if (Request is null || ErrorStatus is not null)
            {
                return this;
            }

            return this with { Request = Request with { Body = body.ToArray() } };
        }
    }
}

/// <summary>
/// Tracks only running connection tasks. Registration precedes cleanup installation so a task
/// that completes synchronously cannot be retained for the listener lifetime.
/// </summary>
internal sealed class ActiveConnectionTaskRegistry
{
    private readonly ConcurrentDictionary<long, Task> tasks = new();
    private long observedFaultCount;

    internal int Count => tasks.Count;

    internal long ObservedFaultCount => Interlocked.Read(ref observedFaultCount);

    internal bool TryRegister(long connectionId, Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (connectionId <= 0 || !tasks.TryAdd(connectionId, task))
        {
            return false;
        }

        _ = task.ContinueWith(
            completedTask => RemoveCompleted(connectionId, completedTask),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        if (task.IsCompleted)
        {
            RemoveCompleted(connectionId, task);
        }

        return true;
    }

    internal IReadOnlyCollection<Task> SnapshotRunning()
    {
        var running = new List<Task>(tasks.Count);
        foreach (var entry in tasks)
        {
            if (entry.Value.IsCompleted)
            {
                RemoveCompleted(entry.Key, entry.Value);
            }
            else
            {
                running.Add(entry.Value);
            }
        }

        return running;
    }

    private void RemoveCompleted(long connectionId, Task task)
    {
        if (!task.IsCompleted
            || !tasks.TryGetValue(connectionId, out var registeredTask)
            || !ReferenceEquals(registeredTask, task)
            || !tasks.TryRemove(connectionId, out _))
        {
            return;
        }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            Interlocked.Increment(ref observedFaultCount);
        }
    }
}
