using System.Text.Json.Serialization;

namespace CadMax.Contracts;

/// <summary>Process-level plugin lifecycle states exposed by the loopback bridge.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BridgePluginState>))]
public enum BridgePluginState
{
    Stopped,
    Starting,
    Listening,
    Ready,
    Degraded,
    Failed,
    Stopping,
}

/// <summary>Sanitized Python-to-plugin connection states.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BridgeConnectionState>))]
public enum BridgeConnectionState
{
    NotConfigured,
    NotConnected,
    Unauthorized,
    Connected,
    Incompatible,
    DevelopmentHost,
}

/// <summary>Canonical paths implemented by the process-level bridge.</summary>
public static class BridgeRoutes
{
    public const string Health = "/v1/health";
    public const string Version = "/v1/version";
    public const string Capabilities = "/v1/capabilities";
    public const string Heartbeat = "/v1/heartbeat";
    public const string ContextProbe = "/v1/context/probe";

    /// <summary>The immutable production route allowlist.</summary>
    public static IReadOnlySet<string> Production { get; } = new HashSet<string>(
        [Health, Version, Capabilities, Heartbeat],
        StringComparer.Ordinal);
}

/// <summary>Health data returned without touching an AutoCAD document.</summary>
public sealed record BridgeHealthData(
    string Service,
    string InstanceId,
    BridgePluginState PluginState,
    [property: JsonPropertyName("autocadConnected")]
    bool AutoCADConnected,
    bool DevelopmentHost,
    bool DocumentAccess,
    bool DwgRead,
    bool DwgWrite,
    bool ReadOnly,
    bool AllowWrite,
    bool AllowScript);

/// <summary>Version data limited to safe process and protocol facts.</summary>
public sealed record BridgeVersionData(
    string ProtocolVersion,
    string SchemaVersion,
    string PluginVersion,
    string AdapterVersion,
    [property: JsonPropertyName("autocadYear")]
    int AutoCADYear,
    [property: JsonPropertyName("autocadProductVersion")]
    string AutoCADProductVersion,
    string RuntimeTarget,
    string InstanceId,
    string CapabilityRevision,
    [property: JsonPropertyName("autocadConnected")]
    bool AutoCADConnected,
    bool DevelopmentHost);

/// <summary>Truthful process-level capability inventory.</summary>
public sealed record BridgeCapabilitiesData(
    string InstanceId,
    string CapabilityRevision,
    BridgePluginState PluginState,
    [property: JsonPropertyName("autocadConnected")]
    bool AutoCADConnected,
    bool DevelopmentHost,
    IReadOnlyDictionary<string, bool> Capabilities);

/// <summary>On-demand heartbeat data; no timer or background heartbeat is used.</summary>
public sealed record BridgeHeartbeatData(
    string InstanceId,
    long HeartbeatSequence,
    DateTimeOffset TimestampUtc,
    long UptimeMs,
    BridgePluginState PluginState,
    string CapabilityRevision,
    [property: JsonPropertyName("autocadConnected")]
    bool AutoCADConnected,
    bool DevelopmentHost,
    string ContextDispatcherState,
    int QueueDepth,
    int InFlightCount,
    bool Modal,
    bool HasActiveDocument,
    ContextDispatchLastStatus LastDispatchStatus);
