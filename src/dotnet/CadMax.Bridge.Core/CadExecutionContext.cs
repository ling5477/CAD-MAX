namespace CadMax.Bridge.Core;

/// <summary>
/// Execution metadata passed to handlers without depending on Autodesk types.
/// </summary>
/// <param name="RequestId">Request correlation ID.</param>
/// <param name="TraceId">Trace correlation ID.</param>
/// <param name="StartedAt">UTC start time.</param>
public sealed record CadExecutionContext(
    string RequestId,
    string TraceId,
    DateTimeOffset StartedAt);
