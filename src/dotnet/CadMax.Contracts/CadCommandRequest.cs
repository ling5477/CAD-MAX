using System.Text.Json;

namespace CadMax.Contracts;

/// <summary>
/// Versioned command request accepted by the localhost bridge.
/// </summary>
/// <param name="SchemaVersion">Protocol schema version; currently 1.0.</param>
/// <param name="RequestId">Per-command UUID used for idempotency and diagnostics.</param>
/// <param name="TraceId">Cross-layer UUID used for correlation.</param>
/// <param name="Command">Registered command name.</param>
/// <param name="Parameters">Command-specific JSON object.</param>
/// <param name="TimeoutMs">Bounded execution timeout in milliseconds.</param>
public sealed record CadCommandRequest(
    string SchemaVersion,
    string RequestId,
    string TraceId,
    string Command,
    JsonElement Parameters,
    int TimeoutMs = 30_000);
