namespace CadMax.Contracts;

/// <summary>
/// Common structured response returned by all bridge operations.
/// </summary>
public sealed record CadResultEnvelope
{
    /// <summary>Current protocol schema version.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>Request correlation UUID.</summary>
    public required string RequestId { get; init; }

    /// <summary>Trace correlation UUID.</summary>
    public required string TraceId { get; init; }

    /// <summary>Whether the requested operation completed.</summary>
    public required bool Success { get; init; }

    /// <summary>Stable status.</summary>
    public required CadStatus Status { get; init; }

    /// <summary>Stable error code, null for success.</summary>
    public CadStatus? ErrorCode { get; init; }

    /// <summary>Client-safe message without paths, environment values, or stack traces.</summary>
    public required string Message { get; init; }

    /// <summary>Command-specific JSON-serializable data.</summary>
    public required object Data { get; init; }

    /// <summary>Non-fatal warning messages.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>Monotonic elapsed execution time in milliseconds.</summary>
    public required long DurationMs { get; init; }

    /// <summary>Create a successful envelope.</summary>
    public static CadResultEnvelope Ok(
        string requestId,
        string traceId,
        string message,
        object? data = null,
        long durationMs = 0) =>
        new()
        {
            SchemaVersion = CadProtocol.SchemaVersion,
            RequestId = requestId,
            TraceId = traceId,
            Success = true,
            Status = CadStatus.Ok,
            ErrorCode = null,
            Message = message,
            Data = data ?? new { },
            Warnings = Array.Empty<string>(),
            DurationMs = Math.Max(0, durationMs),
        };

    /// <summary>Create a fail-closed envelope.</summary>
    public static CadResultEnvelope Failure(
        string requestId,
        string traceId,
        CadStatus status,
        string message,
        object? data = null,
        long durationMs = 0) =>
        new()
        {
            SchemaVersion = CadProtocol.SchemaVersion,
            RequestId = requestId,
            TraceId = traceId,
            Success = false,
            Status = status,
            ErrorCode = status,
            Message = message,
            Data = data ?? new { },
            Warnings = Array.Empty<string>(),
            DurationMs = Math.Max(0, durationMs),
        };
}
