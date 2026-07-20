using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CadMax.Bridge.Core;
using CadMax.Contracts;

namespace CadMax.AutoCAD.Plugin;

/// <summary>
/// Represents the complete SDK-free lifecycle used by the AutoCAD adapter.
/// The state machine is process-local and thread-safe; it owns no AutoCAD object.
/// </summary>
public enum PluginLifecycleState
{
    Stopped,
    Starting,
    Listening,
    Ready,
    Degraded,
    Failed,
    Stopping,
}

/// <summary>
/// Enforces the lifecycle transitions that are safe during bootstrap.
/// Invalid transitions fail closed with a stable error instead of silently overwriting state.
/// </summary>
public sealed class PluginLifecycleStateMachine
{
    private readonly object syncRoot = new();
    private PluginLifecycleState state = PluginLifecycleState.Stopped;

    /// <summary>Gets the current lifecycle state under the state-machine lock.</summary>
    public PluginLifecycleState State
    {
        get
        {
            lock (syncRoot)
            {
                return state;
            }
        }
    }

    /// <summary>
    /// Moves to a permitted next state.
    /// </summary>
    /// <param name="nextState">The requested lifecycle state.</param>
    /// <exception cref="InvalidOperationException">The transition is not permitted.</exception>
    public void TransitionTo(PluginLifecycleState nextState)
    {
        lock (syncRoot)
        {
            if (!IsAllowed(state, nextState))
            {
                throw new InvalidOperationException("ILLEGAL_LIFECYCLE_TRANSITION");
            }

            state = nextState;
        }
    }

    private static bool IsAllowed(
        PluginLifecycleState currentState,
        PluginLifecycleState nextState) =>
        (currentState, nextState) switch
        {
            (PluginLifecycleState.Stopped, PluginLifecycleState.Starting) => true,
            (PluginLifecycleState.Starting, PluginLifecycleState.Listening) => true,
            (PluginLifecycleState.Starting, PluginLifecycleState.Degraded) => true,
            (PluginLifecycleState.Starting, PluginLifecycleState.Failed) => true,
            (PluginLifecycleState.Starting, PluginLifecycleState.Stopping) => true,
            (PluginLifecycleState.Listening, PluginLifecycleState.Ready) => true,
            (PluginLifecycleState.Listening, PluginLifecycleState.Degraded) => true,
            (PluginLifecycleState.Listening, PluginLifecycleState.Stopping) => true,
            (PluginLifecycleState.Ready, PluginLifecycleState.Degraded) => true,
            (PluginLifecycleState.Ready, PluginLifecycleState.Stopping) => true,
            (PluginLifecycleState.Degraded, PluginLifecycleState.Ready) => true,
            (PluginLifecycleState.Degraded, PluginLifecycleState.Failed) => true,
            (PluginLifecycleState.Degraded, PluginLifecycleState.Stopping) => true,
            (PluginLifecycleState.Failed, PluginLifecycleState.Stopping) => true,
            (PluginLifecycleState.Stopping, PluginLifecycleState.Stopped) => true,
            _ => false,
        };
}

/// <summary>
/// Immutable plugin identity shared by the SDK-free core and SDK-bound adapter.
/// Values are validated before the lifecycle can become READY.
/// </summary>
/// <param name="PluginName">Stable human-readable plugin name.</param>
/// <param name="PluginVersion">Semantic plugin version.</param>
/// <param name="SchemaVersion">Lifecycle/status schema version.</param>
public sealed record PluginMetadata(
    string PluginName,
    string PluginVersion,
    string SchemaVersion)
{
    /// <summary>Creates metadata from the SDK-free plugin assembly.</summary>
    public static PluginMetadata CreateDefault()
    {
        var assembly = typeof(PluginMetadata).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var version = informationalVersion?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        return new PluginMetadata("CAD-MAX", version, "1.0");
    }

    /// <summary>
    /// Validates identity fields without returning the rejected value in an exception.
    /// </summary>
    /// <exception cref="InvalidOperationException">Metadata is not safe or supported.</exception>
    public void Validate()
    {
        if (!string.Equals(PluginName, "CAD-MAX", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("PLUGIN_NAME_INVALID");
        }

        if (!Version.TryParse(PluginVersion, out _))
        {
            throw new InvalidOperationException("PLUGIN_VERSION_INVALID");
        }

        if (!string.Equals(SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("SCHEMA_VERSION_INVALID");
        }
    }
}

/// <summary>
/// Safe process-level AutoCAD facts supplied by the adapter without accessing a document.
/// </summary>
/// <param name="AutoCADYear">Target product year compiled into the adapter.</param>
/// <param name="AutoCADProductVersion">Product executable version, never a path.</param>
/// <param name="AdapterVersion">SDK adapter assembly version.</param>
/// <param name="IsAutoCADHostProcess">
/// True only when the SDK-bound adapter attested that it is running inside acad.exe.
/// </param>
public sealed record PluginRuntimeInfo(
    int AutoCADYear,
    string AutoCADProductVersion,
    string AdapterVersion,
    bool IsAutoCADHostProcess)
{
    /// <summary>
    /// Validates the supported target year, safe version fields, and process-level host
    /// attestation. This proves plugin hosting only; it does not claim an MCP connection.
    /// </summary>
    public void Validate()
    {
        if (!IsAutoCADHostProcess)
        {
            throw new InvalidOperationException("AUTOCAD_HOST_ATTESTATION_MISSING");
        }

        if (AutoCADYear is not (2025 or 2026))
        {
            throw new InvalidOperationException("AUTOCAD_YEAR_UNSUPPORTED");
        }

        if (!PluginEvidenceSanitizer.IsSafeVersion(AutoCADProductVersion)
            || !PluginEvidenceSanitizer.IsSafeVersion(AdapterVersion))
        {
            throw new InvalidOperationException("RUNTIME_VERSION_INVALID");
        }
    }
}

/// <summary>
/// Minimal status returned by CADMAXPLUGINSTATUS. It intentionally contains no document,
/// database, path, token, listener, or mutable capability data.
/// </summary>
public sealed record PluginStatus(
    string PluginName,
    string PluginVersion,
    string SchemaVersion,
    string LifecycleState,
    int AutoCADYear,
    string? SafeErrorCode,
    bool ReadOnly,
    bool AllowWrite,
    bool AllowScript);

/// <summary>Lifecycle events permitted in the local append-only evidence file.</summary>
public enum PluginLifecycleEventType
{
    PluginInitializeStarted,
    PluginInitializeSucceeded,
    PluginInitializeFailed,
    BridgeListenerStarted,
    BridgeListenerFaulted,
    BridgeListenerStopped,
    BridgeShutdownDrainTimeout,
    PluginTerminateStarted,
    PluginTerminateSucceeded,
    PluginTerminateFailed,
}

/// <summary>
/// Allowlisted lifecycle evidence DTO. The shape cannot carry drawings, user identity,
/// machine identity, local paths, environment variables, stack traces, or raw exceptions.
/// </summary>
public sealed record PluginLifecycleEvidenceEntry(
    string SchemaVersion,
    string EventId,
    DateTimeOffset TimestampUtc,
    string PluginVersion,
    string AdapterVersion,
    int AutoCADYear,
    string AutoCADProductVersion,
    string EventType,
    string PreviousState,
    string CurrentState,
    bool Success,
    string? SafeErrorCode,
    int? ProcessId)
{
    /// <summary>
    /// Creates a sanitized evidence entry. Unsafe version/error values are replaced by
    /// stable placeholders and are never copied through to the evidence file.
    /// </summary>
    public static PluginLifecycleEvidenceEntry Create(
        PluginMetadata metadata,
        PluginRuntimeInfo runtimeInfo,
        PluginLifecycleEventType eventType,
        PluginLifecycleState previousState,
        PluginLifecycleState currentState,
        bool success,
        string? safeErrorCode = null,
        int? processId = null) =>
        new(
            PluginEvidenceSanitizer.SafeVersion(metadata.SchemaVersion),
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow,
            PluginEvidenceSanitizer.SafeVersion(metadata.PluginVersion),
            PluginEvidenceSanitizer.SafeVersion(runtimeInfo.AdapterVersion),
            runtimeInfo.AutoCADYear is 2025 or 2026 ? runtimeInfo.AutoCADYear : 0,
            PluginEvidenceSanitizer.SafeVersion(runtimeInfo.AutoCADProductVersion),
            ToWireValue(eventType),
            ToWireValue(previousState),
            ToWireValue(currentState),
            success,
            success ? null : PluginEvidenceSanitizer.SafeErrorCode(safeErrorCode),
            processId);

    private static string ToWireValue(PluginLifecycleEventType eventType) =>
        eventType switch
        {
            PluginLifecycleEventType.PluginInitializeStarted => "PLUGIN_INITIALIZE_STARTED",
            PluginLifecycleEventType.PluginInitializeSucceeded => "PLUGIN_INITIALIZE_SUCCEEDED",
            PluginLifecycleEventType.PluginInitializeFailed => "PLUGIN_INITIALIZE_FAILED",
            PluginLifecycleEventType.BridgeListenerStarted => "BRIDGE_LISTENER_STARTED",
            PluginLifecycleEventType.BridgeListenerFaulted => "BRIDGE_LISTENER_FAULTED",
            PluginLifecycleEventType.BridgeListenerStopped => "BRIDGE_LISTENER_STOPPED",
            PluginLifecycleEventType.BridgeShutdownDrainTimeout =>
                "BRIDGE_SHUTDOWN_DRAIN_TIMEOUT",
            PluginLifecycleEventType.PluginTerminateStarted => "PLUGIN_TERMINATE_STARTED",
            PluginLifecycleEventType.PluginTerminateSucceeded => "PLUGIN_TERMINATE_SUCCEEDED",
            PluginLifecycleEventType.PluginTerminateFailed => "PLUGIN_TERMINATE_FAILED",
            _ => "UNKNOWN_EVENT",
        };

    private static string ToWireValue(PluginLifecycleState state) =>
        state.ToString().ToUpperInvariant();
}

/// <summary>
/// Evidence sink abstraction. Implementations return false for bounded-storage or IO failures
/// so the AutoCAD host lifecycle is never taken down by evidence persistence.
/// </summary>
public interface IPluginLifecycleEvidenceWriter
{
    /// <summary>Attempts to append one allowlisted evidence record.</summary>
    bool TryAppend(PluginLifecycleEvidenceEntry entry);
}

/// <summary>
/// Appends UTF-8 JSON Lines evidence to one bounded per-user file. Once the size limit is
/// reached, new evidence is rejected without truncating or overwriting prior records.
/// </summary>
public sealed class BoundedJsonLineEvidenceWriter : IPluginLifecycleEvidenceWriter
{
    public const long DefaultMaximumBytes = 1_048_576;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly object syncRoot = new();
    private readonly string evidenceFilePath;
    private readonly long maximumBytes;

    /// <summary>
    /// Creates a bounded writer.
    /// </summary>
    /// <param name="evidenceFilePath">Explicit test/local path; null uses LocalApplicationData.</param>
    /// <param name="maximumBytes">Maximum total file size.</param>
    public BoundedJsonLineEvidenceWriter(
        string? evidenceFilePath = null,
        long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        this.evidenceFilePath = evidenceFilePath ?? GetDefaultEvidenceFilePath();
        this.maximumBytes = maximumBytes;
    }

    /// <inheritdoc />
    public bool TryAppend(PluginLifecycleEvidenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var jsonLine = PluginJson.SerializeEvidence(entry) + Environment.NewLine;
            var bytes = Utf8WithoutBom.GetBytes(jsonLine);

            lock (syncRoot)
            {
                var directory = Path.GetDirectoryName(evidenceFilePath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return false;
                }

                Directory.CreateDirectory(directory);
                using var stream = new FileStream(
                    evidenceFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.Write,
                    FileShare.Read);

                if (stream.Length + bytes.Length > maximumBytes)
                {
                    return false;
                }

                stream.Seek(0, SeekOrigin.End);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
                return true;
            }
        }
        catch (Exception)
        {
            // Evidence is best-effort and must never escape into the AutoCAD host lifecycle.
            return false;
        }
    }

    private static string GetDefaultEvidenceFilePath()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "CAD-MAX", "evidence", "plugin-lifecycle.jsonl");
    }
}

/// <summary>
/// Coordinates idempotent initialize/terminate operations and exposes the safe status model.
/// All work is synchronous, bounded, and free of AutoCAD Document/Database/Editor access.
/// </summary>
public sealed class PluginLifecycleController
{
    public static readonly TimeSpan ShutdownDeadline = TimeSpan.FromMilliseconds(2000);
    private readonly object syncRoot = new();
    private readonly PluginMetadata metadata;
    private readonly IPluginLifecycleEvidenceWriter evidenceWriter;
    private readonly IBridgeTokenSource tokenSource;
    private readonly ILoopbackBridgeServerFactory serverFactory;
    private readonly LoopbackBridgeOptions bridgeOptions;
    private readonly PluginLifecycleStateMachine stateMachine = new();
    private PluginMetadata? validatedMetadata;
    private PluginRuntimeInfo? runtimeInfo;
    private BridgeTokenCredential? tokenCredential;
    private BridgeInstanceResponseService? responseService;
    private ILoopbackBridgeServer? bridgeServer;
    private string? safeErrorCode;
    private bool terminationEvidenceComplete = true;

    /// <summary>Creates a controller for one AutoCAD process lifetime.</summary>
    public PluginLifecycleController(
        PluginMetadata metadata,
        IPluginLifecycleEvidenceWriter evidenceWriter,
        IBridgeTokenSource? tokenSource = null,
        ILoopbackBridgeServerFactory? serverFactory = null,
        LoopbackBridgeOptions? bridgeOptions = null)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.evidenceWriter = evidenceWriter
            ?? throw new ArgumentNullException(nameof(evidenceWriter));
        this.tokenSource = tokenSource ?? new FileBridgeTokenSource();
        this.serverFactory = serverFactory ?? new LoopbackBridgeServerFactory();
        this.bridgeOptions = bridgeOptions ?? new LoopbackBridgeOptions();
        this.bridgeOptions.Validate();
    }

    /// <summary>Gets the current lifecycle state.</summary>
    public PluginLifecycleState State
    {
        get => stateMachine.State;
    }

    /// <summary>
    /// Starts the plugin lifecycle. A second call after READY is an idempotent success; calls
    /// in any other non-stopped state are rejected. Metadata/runtime failure ends in FAILED.
    /// </summary>
    public bool Initialize(PluginRuntimeInfo requestedRuntimeInfo, int? processId = null)
    {
        ArgumentNullException.ThrowIfNull(requestedRuntimeInfo);

        lock (syncRoot)
        {
            if (stateMachine.State == PluginLifecycleState.Ready)
            {
                return true;
            }

            if (stateMachine.State != PluginLifecycleState.Stopped)
            {
                return false;
            }

            validatedMetadata = null;
            runtimeInfo = null;
            tokenCredential = null;
            responseService = null;
            bridgeServer = null;
            safeErrorCode = null;
            terminationEvidenceComplete = false;
            stateMachine.TransitionTo(PluginLifecycleState.Starting);
            var initializeStartedRecorded = TryAppend(
                PluginLifecycleEventType.PluginInitializeStarted,
                PluginLifecycleState.Stopped,
                PluginLifecycleState.Starting,
                success: true,
                processId: processId,
                evidenceRuntimeInfo: requestedRuntimeInfo);

            if (!initializeStartedRecorded)
            {
                stateMachine.TransitionTo(PluginLifecycleState.Degraded);
                _ = TryAppend(
                    PluginLifecycleEventType.PluginInitializeFailed,
                    PluginLifecycleState.Starting,
                    PluginLifecycleState.Degraded,
                    success: false,
                    safeErrorCode: "LIFECYCLE_EVIDENCE_UNAVAILABLE",
                    processId: processId,
                    evidenceRuntimeInfo: requestedRuntimeInfo);
                return false;
            }

            try
            {
                metadata.Validate();
                requestedRuntimeInfo.Validate();
                validatedMetadata = metadata;
                runtimeInfo = requestedRuntimeInfo;

                var tokenResult = tokenSource.Load();
                if (!tokenResult.Success || tokenResult.Credential is null)
                {
                    return DegradeInitialization(
                        tokenResult.SafeErrorCode ?? "TOKEN_UNAVAILABLE",
                        processId,
                        requestedRuntimeInfo);
                }

                tokenCredential = tokenResult.Credential;
                responseService = new BridgeInstanceResponseService(
                    new BridgeInstanceMetadata(
                        "CadMax.AutoCAD.Bridge",
                        metadata.PluginVersion,
                        requestedRuntimeInfo.AdapterVersion,
                        requestedRuntimeInfo.AutoCADYear,
                        requestedRuntimeInfo.AutoCADProductVersion,
                        "net8.0-windows",
                        requestedRuntimeInfo.IsAutoCADHostProcess,
                        DevelopmentHost: false),
                    BridgePluginState.Starting);
                bridgeServer = serverFactory.Create(
                    bridgeOptions,
                    tokenCredential,
                    responseService,
                    OnListenerFault);
                var startError = bridgeServer.TryStart();
                if (startError is not null)
                {
                    return DegradeInitialization(startError, processId, requestedRuntimeInfo);
                }

                stateMachine.TransitionTo(PluginLifecycleState.Listening);
                responseService.SetPluginState(BridgePluginState.Listening);
                if (!TryAppend(
                        PluginLifecycleEventType.BridgeListenerStarted,
                        PluginLifecycleState.Starting,
                        PluginLifecycleState.Listening,
                        success: true,
                        processId: processId,
                        evidenceRuntimeInfo: requestedRuntimeInfo))
                {
                    return DegradeInitialization(
                        "LIFECYCLE_EVIDENCE_UNAVAILABLE",
                        processId,
                        requestedRuntimeInfo);
                }

                using var selfProbeDeadline = new CancellationTokenSource(
                    TimeSpan.FromMilliseconds(2000));
                var selfProbeSucceeded = bridgeServer
                    .RunAuthenticatedSelfProbeAsync(selfProbeDeadline.Token)
                    .GetAwaiter()
                    .GetResult();
                if (!selfProbeSucceeded)
                {
                    return DegradeInitialization(
                        "SELF_PROBE_FAILED",
                        processId,
                        requestedRuntimeInfo);
                }

                var initializeSucceededRecorded = TryAppend(
                    PluginLifecycleEventType.PluginInitializeSucceeded,
                    PluginLifecycleState.Listening,
                    PluginLifecycleState.Ready,
                    success: true,
                    processId: processId,
                    evidenceRuntimeInfo: requestedRuntimeInfo);

                if (!initializeSucceededRecorded)
                {
                    return DegradeInitialization(
                        "LIFECYCLE_EVIDENCE_UNAVAILABLE",
                        processId,
                        requestedRuntimeInfo);
                }

                stateMachine.TransitionTo(PluginLifecycleState.Ready);
                responseService.SetPluginState(BridgePluginState.Ready);
                return true;
            }
            catch (Exception)
            {
                StopAndReleaseBridge();
                var previousState = stateMachine.State;
                if (previousState == PluginLifecycleState.Starting)
                {
                    stateMachine.TransitionTo(PluginLifecycleState.Failed);
                }
                else if (previousState is PluginLifecycleState.Listening)
                {
                    stateMachine.TransitionTo(PluginLifecycleState.Degraded);
                }
                safeErrorCode = "PLUGIN_INITIALIZATION_FAILED";
                _ = TryAppend(
                    PluginLifecycleEventType.PluginInitializeFailed,
                    previousState,
                    stateMachine.State,
                    success: false,
                    safeErrorCode: safeErrorCode,
                    processId: processId,
                    evidenceRuntimeInfo: requestedRuntimeInfo);
                return false;
            }
        }
    }

    /// <summary>
    /// Terminates the lifecycle without throwing into AutoCAD. A second call after STOPPED is
    /// an idempotent success. Listener drain is bounded by the fixed two-second deadline.
    /// </summary>
    public bool Terminate(int? processId = null)
    {
        lock (syncRoot)
        {
            if (stateMachine.State == PluginLifecycleState.Stopped)
            {
                return terminationEvidenceComplete;
            }

            var previousState = stateMachine.State;
            try
            {
                stateMachine.TransitionTo(PluginLifecycleState.Stopping);
                var terminateStartedRecorded = TryAppend(
                    PluginLifecycleEventType.PluginTerminateStarted,
                    previousState,
                    PluginLifecycleState.Stopping,
                    success: true,
                    processId: processId);

                responseService?.SetPluginState(BridgePluginState.Stopping);
                var drained = bridgeServer?.StopAsync(ShutdownDeadline)
                    .GetAwaiter()
                    .GetResult() ?? true;
                if (!drained)
                {
                    safeErrorCode = "SHUTDOWN_DRAIN_TIMEOUT";
                    _ = TryAppend(
                        PluginLifecycleEventType.BridgeShutdownDrainTimeout,
                        PluginLifecycleState.Stopping,
                        PluginLifecycleState.Stopping,
                        success: false,
                        safeErrorCode: safeErrorCode,
                        processId: processId);
                }

                var listenerStoppedRecorded = TryAppend(
                    PluginLifecycleEventType.BridgeListenerStopped,
                    PluginLifecycleState.Stopping,
                    PluginLifecycleState.Stopping,
                    success: drained,
                    safeErrorCode: drained ? null : safeErrorCode,
                    processId: processId);
                ReleaseBridgeObjects();
                stateMachine.TransitionTo(PluginLifecycleState.Stopped);
                var terminateSucceededRecorded = TryAppend(
                    drained
                        ? PluginLifecycleEventType.PluginTerminateSucceeded
                        : PluginLifecycleEventType.PluginTerminateFailed,
                    PluginLifecycleState.Stopping,
                    PluginLifecycleState.Stopped,
                    success: drained,
                    safeErrorCode: drained ? null : safeErrorCode,
                    processId: processId);
                terminationEvidenceComplete =
                    drained
                    && terminateStartedRecorded
                    && listenerStoppedRecorded
                    && terminateSucceededRecorded;
                return terminationEvidenceComplete;
            }
            catch (Exception)
            {
                terminationEvidenceComplete = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Returns the fixed read-only status contract. Failed or incomplete initialization uses
    /// fixed placeholders rather than unvalidated caller-provided metadata.
    /// </summary>
    public PluginStatus GetStatus()
    {
        lock (syncRoot)
        {
            return new PluginStatus(
                validatedMetadata?.PluginName ?? "CAD-MAX",
                validatedMetadata?.PluginVersion ?? "UNKNOWN",
                validatedMetadata?.SchemaVersion ?? "1.0",
                stateMachine.State.ToString().ToUpperInvariant(),
                runtimeInfo?.AutoCADYear ?? 0,
                PluginEvidenceSanitizer.SafeOptionalErrorCode(safeErrorCode),
                ReadOnly: true,
                AllowWrite: false,
                AllowScript: false);
        }
    }

    private bool DegradeInitialization(
        string errorCode,
        int? processId,
        PluginRuntimeInfo requestedRuntimeInfo)
    {
        var previousState = stateMachine.State;
        safeErrorCode = PluginEvidenceSanitizer.SafeErrorCode(errorCode);
        responseService?.SetPluginState(BridgePluginState.Degraded);
        StopAndReleaseBridge();
        if (stateMachine.State is PluginLifecycleState.Starting or PluginLifecycleState.Listening)
        {
            stateMachine.TransitionTo(PluginLifecycleState.Degraded);
        }

        _ = TryAppend(
            PluginLifecycleEventType.PluginInitializeFailed,
            previousState,
            PluginLifecycleState.Degraded,
            success: false,
            safeErrorCode: safeErrorCode,
            processId: processId,
            evidenceRuntimeInfo: requestedRuntimeInfo);
        return false;
    }

    private void OnListenerFault(string errorCode)
    {
        lock (syncRoot)
        {
            var previousState = stateMachine.State;
            if (previousState is not (PluginLifecycleState.Listening or PluginLifecycleState.Ready))
            {
                return;
            }

            safeErrorCode = PluginEvidenceSanitizer.SafeErrorCode(errorCode);
            responseService?.SetPluginState(BridgePluginState.Degraded);
            stateMachine.TransitionTo(PluginLifecycleState.Degraded);
            _ = TryAppend(
                PluginLifecycleEventType.BridgeListenerFaulted,
                previousState,
                PluginLifecycleState.Degraded,
                success: false,
                safeErrorCode: safeErrorCode);
        }
    }

    private void StopAndReleaseBridge()
    {
        try
        {
            _ = bridgeServer?.StopAsync(ShutdownDeadline).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            safeErrorCode ??= "LISTENER_START_FAILED";
        }

        ReleaseBridgeObjects();
    }

    private void ReleaseBridgeObjects()
    {
        try
        {
            bridgeServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Dispose failure is contained; listener Stop already force-closes the socket.
        }

        bridgeServer = null;
        tokenCredential?.Dispose();
        tokenCredential = null;
        responseService = null;
    }

    private bool TryAppend(
        PluginLifecycleEventType eventType,
        PluginLifecycleState previousState,
        PluginLifecycleState currentState,
        bool success,
        string? safeErrorCode = null,
        int? processId = null,
        PluginRuntimeInfo? evidenceRuntimeInfo = null)
    {
        var currentRuntimeInfo = evidenceRuntimeInfo
            ?? runtimeInfo
            ?? new PluginRuntimeInfo(0, "UNKNOWN", "UNKNOWN", false);
        var entry = PluginLifecycleEvidenceEntry.Create(
            metadata,
            currentRuntimeInfo,
            eventType,
            previousState,
            currentState,
            success,
            safeErrorCode,
            processId);

        try
        {
            return evidenceWriter.TryAppend(entry);
        }
        catch (Exception)
        {
            // A custom evidence sink must not be able to destabilize the AutoCAD host.
            return false;
        }
    }
}

/// <summary>Central JSON settings for metadata, status, and lifecycle evidence.</summary>
public static class PluginJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Serializes plugin metadata with stable camelCase fields.</summary>
    public static string SerializeMetadata(PluginMetadata metadata) =>
        JsonSerializer.Serialize(metadata, Options);

    /// <summary>Serializes safe status with stable camelCase fields.</summary>
    public static string SerializeStatus(PluginStatus status) =>
        JsonSerializer.Serialize(status, Options);

    /// <summary>Serializes one allowlisted lifecycle evidence record.</summary>
    public static string SerializeEvidence(PluginLifecycleEvidenceEntry entry) =>
        JsonSerializer.Serialize(entry, Options);
}

internal static partial class PluginEvidenceSanitizer
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeVersionRegex();

    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeErrorCodeRegex();

    public static bool IsSafeVersion(string? value) =>
        value is not null && SafeVersionRegex().IsMatch(value);

    public static string SafeVersion(string? value) =>
        IsSafeVersion(value) ? value! : "UNKNOWN";

    public static string SafeErrorCode(string? value) =>
        value is not null && SafeErrorCodeRegex().IsMatch(value)
            ? value
            : "INTERNAL_ERROR";

    public static string? SafeOptionalErrorCode(string? value) =>
        value is null ? null : SafeErrorCode(value);
}
