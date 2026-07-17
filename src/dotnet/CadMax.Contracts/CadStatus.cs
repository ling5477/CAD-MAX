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

    /// <summary>An internal failure was mapped to a sanitized response.</summary>
    InternalError,
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
            "INTERNAL_ERROR" => CadStatus.InternalError,
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
            CadStatus.InternalError => "INTERNAL_ERROR",
            _ => throw new JsonException("Unknown CAD-MAX status."),
        });
}
