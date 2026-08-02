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
/// Supplies one lock-free, Autodesk-free context snapshot for capabilities and heartbeat.
/// </summary>
public interface IBridgeContextStateProvider
{
    BridgeContextSnapshot GetSnapshot();
}

/// <summary>
/// Produces the four canonical process-level endpoint envelopes for one process instance.
/// The service is thread-safe and owns only immutable metadata and atomic counters.
/// </summary>
public sealed class BridgeInstanceResponseService
{
    private static readonly IReadOnlyDictionary<string, bool> BaseCapabilityInventory =
        new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["bridge.health"] = true,
            ["bridge.version"] = true,
            ["bridge.capabilities"] = true,
            ["bridge.heartbeat"] = true,
            ["bridge.contextDispatch"] = false,
            ["bridge.contextProbe"] = false,
            ["documentContext.available"] = false,
            ["drawing.status"] = false,
            ["drawing.active_document"] = false,
            ["drawing.list_documents"] = false,
            ["drawing.units"] = false,
            ["drawing.bounds"] = false,
            ["drawing.layouts"] = false,
            ["drawing.system_metadata"] = false,
            ["drawing.revision"] = false,
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
    private readonly IBridgeContextStateProvider? contextStateProvider;
    private readonly string instanceId = Guid.NewGuid().ToString("D");
    private readonly string capabilityRevisionSeed = Guid.NewGuid().ToString("N");
    private readonly long startedTimestamp = Stopwatch.GetTimestamp();
    private int pluginState;
    private long capabilityRevisionSequence = 1;
    private long heartbeatSequence;

    /// <summary>Create one response service whose instance identity is never persisted.</summary>
    public BridgeInstanceResponseService(
        BridgeInstanceMetadata metadata,
        BridgePluginState initialState = BridgePluginState.Starting,
        IBridgeContextStateProvider? contextStateProvider = null)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.contextStateProvider = contextStateProvider;
        pluginState = (int)initialState;
    }

    public string InstanceId => instanceId;

    public string CapabilityRevision
    {
        get
        {
            var contextRevision = GetContextSnapshot().Revision;
            return $"{capabilityRevisionSeed}-" +
                $"{Interlocked.Read(ref capabilityRevisionSequence):x16}-" +
                $"{contextRevision:x16}";
        }
    }

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
        BridgePluginState state)
    {
        var context = GetContextSnapshot();
        var drawingHandlersAvailable = IsAutoCADConnected(state)
            && context.DispatcherReady;
        return CadResultEnvelope.Ok(
            requestId,
            traceId,
            "CAD-MAX AutoCAD bridge is healthy",
            new BridgeHealthData(
                metadata.Service,
                instanceId,
                state,
                IsAutoCADConnected(state),
                metadata.DevelopmentHost,
                DocumentAccess: drawingHandlersAvailable,
                DwgRead: drawingHandlersAvailable,
                DwgWrite: false,
                ReadOnly: true,
                AllowWrite: false,
                AllowScript: false));
    }

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
        BridgePluginState state)
    {
        var context = GetContextSnapshot();
        var capabilities = new Dictionary<string, bool>(
            BaseCapabilityInventory,
            StringComparer.Ordinal)
        {
            ["bridge.contextDispatch"] = context.DispatcherReady,
            ["bridge.contextProbe"] = context.DispatcherReady,
            ["documentContext.available"] = context.DocumentContextAvailable,
            ["drawing.status"] = IsAutoCADConnected(state)
                && context.DispatcherReady
                && !context.Modal,
            ["drawing.list_documents"] = IsAutoCADConnected(state)
                && context.DispatcherReady
                && !context.Modal,
            ["drawing.active_document"] = IsAutoCADConnected(state)
                && context.DocumentContextAvailable,
            ["drawing.units"] = IsAutoCADConnected(state)
                && context.DocumentContextAvailable,
            ["drawing.bounds"] = IsAutoCADConnected(state)
                && context.DocumentContextAvailable,
            ["drawing.layouts"] = IsAutoCADConnected(state)
                && context.DocumentContextAvailable,
            ["drawing.system_metadata"] = IsAutoCADConnected(state)
                && context.DocumentContextAvailable,
            ["dwg.read"] = IsAutoCADConnected(state)
                && context.DispatcherReady
                && !context.Modal,
        };
        return CadResultEnvelope.Ok(
            requestId,
            traceId,
            "CAD-MAX AutoCAD bridge capability inventory",
            new BridgeCapabilitiesData(
                instanceId,
                CapabilityRevision,
                state,
                IsAutoCADConnected(state),
                metadata.DevelopmentHost,
                new ReadOnlyDictionary<string, bool>(capabilities)));
    }

    private CadResultEnvelope CreateHeartbeat(
        string requestId,
        string traceId,
        BridgePluginState state)
    {
        var context = GetContextSnapshot();
        return CadResultEnvelope.Ok(
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
                metadata.DevelopmentHost,
                context.DispatcherState,
                context.QueueDepth,
                context.InFlightCount,
                context.Modal,
                context.HasActiveDocument,
                context.LastDispatchStatus));
    }

    private BridgeContextSnapshot GetContextSnapshot()
    {
        try
        {
            return contextStateProvider?.GetSnapshot() ?? BridgeContextSnapshot.Unavailable;
        }
        catch (Exception)
        {
            return BridgeContextSnapshot.Unavailable;
        }
    }

    private bool IsAutoCADConnected(BridgePluginState state) =>
        !metadata.DevelopmentHost
        && metadata.IsAutoCADHostProcess
        && state == BridgePluginState.Ready;
}
