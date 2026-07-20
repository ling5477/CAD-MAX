using System.Collections.ObjectModel;
using System.Diagnostics;
using CadMax.Contracts;

namespace CadMax.Bridge.Core;

/// <summary>
/// Immutable process facts used by both the production plugin and SDK-free development Host.
/// Values must not contain a PID, path, user name, machine name, token, or AutoCAD document data.
/// </summary>
public sealed record BridgeInstanceMetadata(
    string Service,
    string PluginVersion,
    string AdapterVersion,
    int AutoCADYear,
    string AutoCADProductVersion,
    string RuntimeTarget,
    bool IsAutoCADHostProcess,
    bool DevelopmentHost);

/// <summary>
/// Produces the four canonical process-level endpoint envelopes for one process instance.
/// The service is thread-safe and owns only immutable metadata and atomic counters.
/// </summary>
public sealed class BridgeInstanceResponseService
{
    private static readonly IReadOnlyDictionary<string, bool> CapabilityInventory =
        new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["bridge.health"] = true,
            ["bridge.version"] = true,
            ["bridge.capabilities"] = true,
            ["bridge.heartbeat"] = true,
            ["drawing.active_document"] = false,
            ["drawing.list_documents"] = false,
            ["drawing.units"] = false,
            ["drawing.bounds"] = false,
            ["drawing.layouts"] = false,
            ["query.entity_count"] = false,
            ["query.count_by_type"] = false,
            ["query.list_entities"] = false,
            ["query.entity_summary"] = false,
            ["query.resolve_handle"] = false,
            ["query.measure_geometry"] = false,
            ["layer.list_layers"] = false,
            ["block.list_definitions"] = false,
            ["block.list_references"] = false,
            ["block.list_attributes"] = false,
            ["style.list_text_styles"] = false,
            ["style.list_dimension_styles"] = false,
            ["style.list_linetypes"] = false,
            ["selection.get_pickfirst"] = false,
            ["preview.render_pdf"] = false,
            ["preview.render_png"] = false,
            ["dwg.read"] = false,
            ["dwg.write"] = false,
            ["script"] = false,
            ["command"] = false,
        });

    private readonly BridgeInstanceMetadata metadata;
    private readonly string instanceId = Guid.NewGuid().ToString("D");
    private readonly string capabilityRevisionSeed = Guid.NewGuid().ToString("N");
    private readonly long startedTimestamp = Stopwatch.GetTimestamp();
    private int pluginState;
    private long capabilityRevisionSequence = 1;
    private long heartbeatSequence;

    /// <summary>Create one response service whose instance identity is never persisted.</summary>
    public BridgeInstanceResponseService(
        BridgeInstanceMetadata metadata,
        BridgePluginState initialState = BridgePluginState.Starting)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        pluginState = (int)initialState;
    }

    public string InstanceId => instanceId;

    public string CapabilityRevision =>
        $"{capabilityRevisionSeed}-{Interlocked.Read(ref capabilityRevisionSequence):x16}";

    public BridgePluginState PluginState =>
        (BridgePluginState)Volatile.Read(ref pluginState);

    /// <summary>Update lifecycle state and revision only when the state actually changes.</summary>
    public void SetPluginState(BridgePluginState nextState)
    {
        var previous = Interlocked.Exchange(ref pluginState, (int)nextState);
        if (previous != (int)nextState)
        {
            _ = Interlocked.Increment(ref capabilityRevisionSequence);
        }
    }

    /// <summary>
    /// Create one canonical response. Only the internal authenticated startup self-probe may
    /// request process data before READY; external callers fail closed.
    /// </summary>
    public CadResultEnvelope CreateResponse(
        string route,
        string requestId,
        string traceId,
        bool authenticatedStartupSelfProbe = false)
    {
        var state = PluginState;
        if (state == BridgePluginState.Stopping || state == BridgePluginState.Stopped)
        {
            return CadResultEnvelope.Failure(
                requestId,
                traceId,
                CadStatus.BridgeStopping,
                "CAD-MAX AutoCAD bridge is stopping");
        }

        if (state != BridgePluginState.Ready && !authenticatedStartupSelfProbe)
        {
            return CadResultEnvelope.Failure(
                requestId,
                traceId,
                CadStatus.BridgeNotReady,
                "CAD-MAX AutoCAD bridge is not ready");
        }

        return route switch
        {
            BridgeRoutes.Health => CreateHealth(requestId, traceId, state),
            BridgeRoutes.Version => CreateVersion(requestId, traceId, state),
            BridgeRoutes.Capabilities => CreateCapabilities(requestId, traceId, state),
            BridgeRoutes.Heartbeat => CreateHeartbeat(requestId, traceId, state),
            _ => CadResultEnvelope.Failure(
                requestId,
                traceId,
                CadStatus.RouteNotFound,
                "CAD-MAX bridge route was not found"),
        };
    }

    private CadResultEnvelope CreateHealth(
        string requestId,
        string traceId,
        BridgePluginState state) =>
        CadResultEnvelope.Ok(
            requestId,
            traceId,
            "CAD-MAX AutoCAD bridge is healthy",
            new BridgeHealthData(
                metadata.Service,
                instanceId,
                state,
                IsAutoCADConnected(state),
                metadata.DevelopmentHost,
                DocumentAccess: false,
                DwgRead: false,
                DwgWrite: false,
                ReadOnly: true,
                AllowWrite: false,
                AllowScript: false));

    private CadResultEnvelope CreateVersion(
        string requestId,
        string traceId,
        BridgePluginState state) =>
        CadResultEnvelope.Ok(
            requestId,
            traceId,
            "CAD-MAX AutoCAD bridge version information",
            new BridgeVersionData(
                CadProtocol.SchemaVersion,
                CadProtocol.SchemaVersion,
                metadata.PluginVersion,
                metadata.AdapterVersion,
                metadata.AutoCADYear,
                metadata.AutoCADProductVersion,
                metadata.RuntimeTarget,
                instanceId,
                CapabilityRevision,
                IsAutoCADConnected(state),
                metadata.DevelopmentHost));

    private CadResultEnvelope CreateCapabilities(
        string requestId,
        string traceId,
        BridgePluginState state) =>
        CadResultEnvelope.Ok(
            requestId,
            traceId,
            "CAD-MAX AutoCAD bridge capability inventory",
            new BridgeCapabilitiesData(
                instanceId,
                CapabilityRevision,
                state,
                IsAutoCADConnected(state),
                metadata.DevelopmentHost,
                CapabilityInventory));

    private CadResultEnvelope CreateHeartbeat(
        string requestId,
        string traceId,
        BridgePluginState state) =>
        CadResultEnvelope.Ok(
            requestId,
            traceId,
            "CAD-MAX AutoCAD bridge heartbeat",
            new BridgeHeartbeatData(
                instanceId,
                Interlocked.Increment(ref heartbeatSequence),
                DateTimeOffset.UtcNow,
                Math.Max(0, (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds),
                state,
                CapabilityRevision,
                IsAutoCADConnected(state),
                metadata.DevelopmentHost));

    private bool IsAutoCADConnected(BridgePluginState state) =>
        !metadata.DevelopmentHost
        && metadata.IsAutoCADHostProcess
        && state == BridgePluginState.Ready;
}
