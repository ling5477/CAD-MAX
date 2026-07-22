using System.Text.Json.Serialization;

namespace CadMax.Contracts;

/// <summary>
/// Immutable request for the sole Phase 1.3 document-context operation. It carries only
/// correlation, deadline, process identity, and an optional opaque document identity.
/// </summary>
public sealed record ContextProbeRequest(
    string SchemaVersion,
    string RequestId,
    string TraceId,
    DateTimeOffset DeadlineUtc,
    string ExpectedInstanceId,
    string? ExpectedDocumentId);

/// <summary>Safe execution data returned by the fixed context probe.</summary>
public sealed record ContextProbeData(
    string InstanceId,
    string DispatchId,
    bool MainThreadVerified,
    string ExecutionContext,
    string DocumentState,
    string ActiveDocumentId,
    bool IsQuiescent,
    long QueueDelayMs,
    long ExecutionMs);

/// <summary>Stable heartbeat classification for the latest document dispatch.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContextDispatchLastStatus>))]
public enum ContextDispatchLastStatus
{
    None,
    Ok,
    NoActiveDocument,
    DocumentBusy,
    ApplicationModal,
    Timeout,
    Cancelled,
    Failed,
}

/// <summary>
/// Atomic SDK-free snapshot consumed by process-level capabilities and heartbeat responses.
/// It never contains an Autodesk object or an opaque document identifier.
/// </summary>
public sealed record BridgeContextSnapshot(
    long Revision,
    bool DispatcherReady,
    string DispatcherState,
    int QueueDepth,
    int InFlightCount,
    bool Modal,
    bool HasActiveDocument,
    bool IsQuiescent,
    ContextDispatchLastStatus LastDispatchStatus)
{
    public static BridgeContextSnapshot Unavailable { get; } = new(
        Revision: 0,
        DispatcherReady: false,
        DispatcherState: "NOT_READY",
        QueueDepth: 0,
        InFlightCount: 0,
        Modal: false,
        HasActiveDocument: false,
        IsQuiescent: false,
        LastDispatchStatus: ContextDispatchLastStatus.None);

    public bool DocumentContextAvailable =>
        DispatcherReady && HasActiveDocument && !Modal && IsQuiescent;
}
