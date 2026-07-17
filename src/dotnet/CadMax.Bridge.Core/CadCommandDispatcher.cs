using System.Diagnostics;
using CadMax.Contracts;
using Microsoft.Extensions.Logging;

namespace CadMax.Bridge.Core;

/// <summary>
/// Validates, resolves, times, cancels, and safely maps bridge command execution.
/// Thread safety comes from an immutable registry and per-request cancellation sources.
/// </summary>
public sealed class CadCommandDispatcher
{
    private readonly CadCommandRegistry registry;
    private readonly CadCommandValidator validator;
    private readonly ILogger<CadCommandDispatcher> logger;

    /// <summary>Create a dispatcher from immutable collaborators.</summary>
    public CadCommandDispatcher(
        CadCommandRegistry registry,
        CadCommandValidator validator,
        ILogger<CadCommandDispatcher> logger)
    {
        this.registry = registry;
        this.validator = validator;
        this.logger = logger;
    }

    /// <summary>
    /// Dispatch one command with a bounded timeout. No handler means NOT_IMPLEMENTED;
    /// exceptions are logged by type only and mapped to INTERNAL_ERROR.
    /// </summary>
    public async Task<CadResultEnvelope> DispatchAsync(
        CadCommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var validationErrors = validator.Validate(request);
        if (validationErrors.Count > 0)
        {
            return CadResultEnvelope.Failure(
                request.RequestId,
                request.TraceId,
                CadStatus.InvalidArgument,
                "Command request is invalid",
                new { errors = validationErrors },
                stopwatch.ElapsedMilliseconds);
        }

        if (!registry.TryGet(request.Command, out var handler) || handler is null)
        {
            return CadResultEnvelope.Failure(
                request.RequestId,
                request.TraceId,
                CadStatus.NotImplemented,
                "No command handler is registered",
                durationMs: stopwatch.ElapsedMilliseconds);
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(request.TimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var context = new CadExecutionContext(
            request.RequestId,
            request.TraceId,
            DateTimeOffset.UtcNow);

        try
        {
            var result = await handler.ExecuteAsync(request, context, linked.Token);
            return new CadResultEnvelope
            {
                SchemaVersion = CadProtocol.SchemaVersion,
                RequestId = request.RequestId,
                TraceId = request.TraceId,
                Success = true,
                Status = CadStatus.Ok,
                ErrorCode = null,
                Message = result.Message,
                Data = result.Data,
                Warnings = result.Warnings ?? Array.Empty<string>(),
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CadResultEnvelope.Failure(
                request.RequestId,
                request.TraceId,
                CadStatus.Timeout,
                "Command was cancelled",
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return CadResultEnvelope.Failure(
                request.RequestId,
                request.TraceId,
                CadStatus.Timeout,
                "Command timed out",
                durationMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Command failed, requestId={RequestId}, traceId={TraceId}, command={Command}, exceptionType={ExceptionType}",
                request.RequestId,
                request.TraceId,
                request.Command,
                exception.GetType().Name);
            return CadResultEnvelope.Failure(
                request.RequestId,
                request.TraceId,
                CadStatus.InternalError,
                "Command execution failed",
                durationMs: stopwatch.ElapsedMilliseconds);
        }
    }
}
