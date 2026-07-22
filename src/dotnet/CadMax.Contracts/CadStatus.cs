using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadMax.Contracts;

/// <summary>
/// Stable status and error-code values shared with the Python MCP envelope.
/// </summary>
[JsonConverter(typeof(CadStatusJsonConverter))]
public enum CadStatus
{
    /// <summary>The requested operation completed successfully.</summary>
    Ok,

    /// <summary>The configured backend could not be reached.</summary>
    BackendUnavailable,

    /// <summary>No backend has been configured.</summary>
    BackendNotConfigured,

    /// <summary>The request failed validation.</summary>
    InvalidArgument,

    /// <summary>A requested path is outside the allowlist.</summary>
    PathNotAllowed,

    /// <summary>The operation is blocked by read-only policy.</summary>
    ReadOnly,

    /// <summary>No real handler is registered for the operation.</summary>
    NotImplemented,

    /// <summary>The operation was cancelled or exceeded its timeout.</summary>
    Timeout,

    /// <summary>The request was cancelled before the fixed probe completed.</summary>
    Cancelled,

    /// <summary>The AutoCAD document-context dispatcher has not completed initialization.</summary>
    DispatcherNotReady,

    /// <summary>The request targets a previous AutoCAD process instance.</summary>
    InstanceMismatch,

    /// <summary>AutoCAD currently has no active document.</summary>
    NoActiveDocument,

    /// <summary>The requested opaque document is not the active document.</summary>
    DocumentNotActive,

    /// <summary>The requested opaque document was destroyed during dispatch.</summary>
    DocumentDestroyed,

    /// <summary>The requested opaque document is not known to this process instance.</summary>
    DocumentNotFound,

    /// <summary>AutoCAD is inside a modal application state.</summary>
    ApplicationModal,

    /// <summary>The active document is not quiescent.</summary>
    DocumentBusy,

    /// <summary>The bounded document dispatch queue is full.</summary>
    QueueFull,

    /// <summary>The application/main-thread dispatch boundary failed.</summary>
    MainThreadDispatchFailed,

    /// <summary>The official document command-context callback failed.</summary>
    CommandContextFailed,

    /// <summary>An internal failure was mapped to a sanitized response.</summary>
    InternalError,

    /// <summary>The Bearer token was missing or invalid.</summary>
    Unauthorized,

    /// <summary>The HTTP method is not permitted.</summary>
    MethodNotAllowed,

    /// <summary>The request path is not registered.</summary>
    RouteNotFound,

    /// <summary>Query strings are not accepted by process-level routes.</summary>
    QueryNotAllowed,

    /// <summary>Request bodies and transfer encodings are not accepted.</summary>
    RequestBodyNotAllowed,

    /// <summary>The bounded header section was exceeded.</summary>
    HeadersTooLarge,

    /// <summary>The bounded request line was exceeded.</summary>
    RequestTargetTooLong,

    /// <summary>The HTTP request could not be read before its deadline.</summary>
    RequestTimeout,

    /// <summary>The bounded connection capacity is exhausted.</summary>
    ServerBusy,

    /// <summary>The listener is running but the lifecycle is not ready.</summary>
    BridgeNotReady,

    /// <summary>The bridge is stopping and rejects new work.</summary>
    BridgeStopping,

    /// <summary>The configured AutoCAD bridge process is not reachable.</summary>
    NotConnected,

    /// <summary>The peer contract is incompatible.</summary>
    SchemaMismatch,

    /// <summary>The machine-local token file is not configured.</summary>
    TokenNotConfigured,

    /// <summary>The machine-local token file schema is invalid.</summary>
    TokenConfigInvalid,

    /// <summary>The machine-local token file ACL is too broad.</summary>
    TokenFileInsecure,

    /// <summary>The decoded token is not exactly 256 bits.</summary>
    TokenInvalid,

    /// <summary>The machine-local token file could not be read safely.</summary>
    TokenUnavailable,
}

/// <summary>
/// Preserves the exact cross-language uppercase status strings while allowing
/// idiomatic C# enum member names.
/// </summary>
public sealed class CadStatusJsonConverter : JsonConverter<CadStatus>
{
    /// <inheritdoc />
    public override CadStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "OK" => CadStatus.Ok,
            "BACKEND_UNAVAILABLE" => CadStatus.BackendUnavailable,
            "BACKEND_NOT_CONFIGURED" => CadStatus.BackendNotConfigured,
            "INVALID_ARGUMENT" => CadStatus.InvalidArgument,
            "PATH_NOT_ALLOWED" => CadStatus.PathNotAllowed,
            "READ_ONLY" => CadStatus.ReadOnly,
            "NOT_IMPLEMENTED" => CadStatus.NotImplemented,
            "TIMEOUT" => CadStatus.Timeout,
            "CANCELLED" => CadStatus.Cancelled,
            "DISPATCHER_NOT_READY" => CadStatus.DispatcherNotReady,
            "INSTANCE_MISMATCH" => CadStatus.InstanceMismatch,
            "NO_ACTIVE_DOCUMENT" => CadStatus.NoActiveDocument,
            "DOCUMENT_NOT_ACTIVE" => CadStatus.DocumentNotActive,
            "DOCUMENT_DESTROYED" => CadStatus.DocumentDestroyed,
            "DOCUMENT_NOT_FOUND" => CadStatus.DocumentNotFound,
            "APPLICATION_MODAL" => CadStatus.ApplicationModal,
            "DOCUMENT_BUSY" => CadStatus.DocumentBusy,
            "QUEUE_FULL" => CadStatus.QueueFull,
            "MAIN_THREAD_DISPATCH_FAILED" => CadStatus.MainThreadDispatchFailed,
            "COMMAND_CONTEXT_FAILED" => CadStatus.CommandContextFailed,
            "INTERNAL_ERROR" => CadStatus.InternalError,
            "UNAUTHORIZED" => CadStatus.Unauthorized,
            "METHOD_NOT_ALLOWED" => CadStatus.MethodNotAllowed,
            "ROUTE_NOT_FOUND" => CadStatus.RouteNotFound,
            "QUERY_NOT_ALLOWED" => CadStatus.QueryNotAllowed,
            "REQUEST_BODY_NOT_ALLOWED" => CadStatus.RequestBodyNotAllowed,
            "HEADERS_TOO_LARGE" => CadStatus.HeadersTooLarge,
            "REQUEST_TARGET_TOO_LONG" => CadStatus.RequestTargetTooLong,
            "REQUEST_TIMEOUT" => CadStatus.RequestTimeout,
            "SERVER_BUSY" => CadStatus.ServerBusy,
            "BRIDGE_NOT_READY" => CadStatus.BridgeNotReady,
            "BRIDGE_STOPPING" => CadStatus.BridgeStopping,
            "NOT_CONNECTED" => CadStatus.NotConnected,
            "SCHEMA_MISMATCH" => CadStatus.SchemaMismatch,
            "TOKEN_NOT_CONFIGURED" => CadStatus.TokenNotConfigured,
            "TOKEN_CONFIG_INVALID" => CadStatus.TokenConfigInvalid,
            "TOKEN_FILE_INSECURE" => CadStatus.TokenFileInsecure,
            "TOKEN_INVALID" => CadStatus.TokenInvalid,
            "TOKEN_UNAVAILABLE" => CadStatus.TokenUnavailable,
            _ => throw new JsonException("Unknown CAD-MAX status."),
        };

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        CadStatus value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            CadStatus.Ok => "OK",
            CadStatus.BackendUnavailable => "BACKEND_UNAVAILABLE",
            CadStatus.BackendNotConfigured => "BACKEND_NOT_CONFIGURED",
            CadStatus.InvalidArgument => "INVALID_ARGUMENT",
            CadStatus.PathNotAllowed => "PATH_NOT_ALLOWED",
            CadStatus.ReadOnly => "READ_ONLY",
            CadStatus.NotImplemented => "NOT_IMPLEMENTED",
            CadStatus.Timeout => "TIMEOUT",
            CadStatus.Cancelled => "CANCELLED",
            CadStatus.DispatcherNotReady => "DISPATCHER_NOT_READY",
            CadStatus.InstanceMismatch => "INSTANCE_MISMATCH",
            CadStatus.NoActiveDocument => "NO_ACTIVE_DOCUMENT",
            CadStatus.DocumentNotActive => "DOCUMENT_NOT_ACTIVE",
            CadStatus.DocumentDestroyed => "DOCUMENT_DESTROYED",
            CadStatus.DocumentNotFound => "DOCUMENT_NOT_FOUND",
            CadStatus.ApplicationModal => "APPLICATION_MODAL",
            CadStatus.DocumentBusy => "DOCUMENT_BUSY",
            CadStatus.QueueFull => "QUEUE_FULL",
            CadStatus.MainThreadDispatchFailed => "MAIN_THREAD_DISPATCH_FAILED",
            CadStatus.CommandContextFailed => "COMMAND_CONTEXT_FAILED",
            CadStatus.InternalError => "INTERNAL_ERROR",
            CadStatus.Unauthorized => "UNAUTHORIZED",
            CadStatus.MethodNotAllowed => "METHOD_NOT_ALLOWED",
            CadStatus.RouteNotFound => "ROUTE_NOT_FOUND",
            CadStatus.QueryNotAllowed => "QUERY_NOT_ALLOWED",
            CadStatus.RequestBodyNotAllowed => "REQUEST_BODY_NOT_ALLOWED",
            CadStatus.HeadersTooLarge => "HEADERS_TOO_LARGE",
            CadStatus.RequestTargetTooLong => "REQUEST_TARGET_TOO_LONG",
            CadStatus.RequestTimeout => "REQUEST_TIMEOUT",
            CadStatus.ServerBusy => "SERVER_BUSY",
            CadStatus.BridgeNotReady => "BRIDGE_NOT_READY",
            CadStatus.BridgeStopping => "BRIDGE_STOPPING",
            CadStatus.NotConnected => "NOT_CONNECTED",
            CadStatus.SchemaMismatch => "SCHEMA_MISMATCH",
            CadStatus.TokenNotConfigured => "TOKEN_NOT_CONFIGURED",
            CadStatus.TokenConfigInvalid => "TOKEN_CONFIG_INVALID",
            CadStatus.TokenFileInsecure => "TOKEN_FILE_INSECURE",
            CadStatus.TokenInvalid => "TOKEN_INVALID",
            CadStatus.TokenUnavailable => "TOKEN_UNAVAILABLE",
            _ => throw new JsonException("Unknown CAD-MAX status."),
        });
}
