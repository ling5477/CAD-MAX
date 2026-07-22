using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CadMax.Bridge.Core;
using CadMax.Contracts;

namespace CadMax.AutoCAD.Plugin;

/// <summary>
/// Process-local opaque identity map for adapter-owned objects. Reverse lookup uses weak
/// references, removal is explicit, and reset invalidates the entire process-session map.
/// </summary>
public sealed class OpaqueSessionIdentityRegistry<T>
    where T : class
{
    private ConditionalWeakTable<T, IdentityHolder> byObject = new();
    private readonly Dictionary<string, WeakReference<T>> byId = new(StringComparer.Ordinal);

    public int EntryCount => byId.Count;

    public string GetOrCreate(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (byObject.TryGetValue(value, out var existing))
        {
            return existing.Identity;
        }

        string identity;
        do
        {
            identity = "doc_" + EncodeBase64Url(RandomNumberGenerator.GetBytes(16));
        }
        while (byId.ContainsKey(identity));

        byObject.Add(value, new IdentityHolder(identity));
        byId.Add(identity, new WeakReference<T>(value));
        return identity;
    }

    public OpaqueIdentityResolution<T> ResolveActive(T activeValue, string? expectedIdentity)
    {
        var activeIdentity = GetOrCreate(activeValue);
        if (expectedIdentity is null)
        {
            return new OpaqueIdentityResolution<T>(activeIdentity, activeValue, null);
        }

        if (!byId.TryGetValue(expectedIdentity, out var weakValue)
            || !weakValue.TryGetTarget(out var expectedValue))
        {
            _ = byId.Remove(expectedIdentity);
            return new OpaqueIdentityResolution<T>(null, null, CadStatus.DocumentNotFound);
        }

        return ReferenceEquals(activeValue, expectedValue)
            ? new OpaqueIdentityResolution<T>(activeIdentity, activeValue, null)
            : new OpaqueIdentityResolution<T>(null, null, CadStatus.DocumentNotActive);
    }

    public void Remove(T value)
    {
        if (byObject.TryGetValue(value, out var holder))
        {
            _ = byId.Remove(holder.Identity);
            _ = byObject.Remove(value);
        }
    }

    public void Prune()
    {
        foreach (var entry in byId.ToArray())
        {
            if (!entry.Value.TryGetTarget(out _))
            {
                _ = byId.Remove(entry.Key);
            }
        }
    }

    public void Reset()
    {
        byId.Clear();
        byObject = new ConditionalWeakTable<T, IdentityHolder>();
    }

    private static string EncodeBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record IdentityHolder(string Identity);
}

public sealed record OpaqueIdentityResolution<T>(
    string? Identity,
    T? Value,
    CadStatus? ErrorStatus)
    where T : class;

/// <summary>Result of inspecting one queue item during a bounded Idle drain cycle.</summary>
public enum DocumentDispatchTakeResult
{
    NoWork,
    Skipped,
    Started,
}

/// <summary>
/// SDK-free boundary over AutoCAD's official document command-context scheduler.
/// Implementations must not synchronously wait for the returned task.
/// </summary>
public interface IDocumentCommandContextScheduler
{
    bool IsOnMainThread { get; }

    Task ExecuteAsync(Func<Task> callback);
}

/// <summary>
/// Starts and observes one official document command-context callback without blocking the
/// application thread. The callback remains fixed by the SDK-bound adapter.
/// </summary>
public sealed class DocumentCommandContextCoordinator
{
    private readonly DocumentContextDispatcher dispatcher;

    public DocumentCommandContextCoordinator(DocumentContextDispatcher dispatcher)
    {
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Start(
        DocumentDispatchWorkItem workItem,
        IDocumentCommandContextScheduler scheduler,
        Func<DocumentDispatchWorkItem, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(callback);

        if (!scheduler.IsOnMainThread)
        {
            _ = dispatcher.Complete(
                workItem,
                DocumentContextDispatcher.CreateFailure(
                    workItem.Request,
                    CadStatus.MainThreadDispatchFailed));
            return;
        }

        Task execution;
        try
        {
            execution = scheduler.ExecuteAsync(() => callback(workItem));
        }
        catch (Exception)
        {
            _ = dispatcher.Complete(
                workItem,
                DocumentContextDispatcher.CreateFailure(
                    workItem.Request,
                    CadStatus.MainThreadDispatchFailed));
            return;
        }

        _ = ObserveExecutionAsync(workItem, execution);
    }

    private async Task ObserveExecutionAsync(
        DocumentDispatchWorkItem workItem,
        Task execution)
    {
        try
        {
            await execution.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The stable failure below deliberately discards the raw scheduler exception.
        }

        _ = dispatcher.Complete(
            workItem,
            DocumentContextDispatcher.CreateFailure(
                workItem.Request,
                CadStatus.CommandContextFailed));
    }
}

/// <summary>Immutable SDK-free work item admitted to the bounded FIFO queue.</summary>
public sealed class DocumentDispatchWorkItem
{
    private const int QueuedState = 0;
    private const int InFlightState = 1;
    private const int CompletedState = 2;
    private int state = QueuedState;
    private int cancellationRequested;

    internal DocumentDispatchWorkItem(ContextProbeRequest request)
    {
        Request = request;
        DispatchId = Guid.NewGuid().ToString("D");
        EnqueuedTimestamp = Stopwatch.GetTimestamp();
        Completion = new TaskCompletionSource<CadResultEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public ContextProbeRequest Request { get; }

    public string DispatchId { get; }

    public long EnqueuedTimestamp { get; }

    public bool IsCancellationRequested => Volatile.Read(ref cancellationRequested) != 0;

    internal TaskCompletionSource<CadResultEnvelope> Completion { get; }

    internal LinkedListNode<DocumentDispatchWorkItem>? QueueNode { get; set; }

    internal CancellationTokenSource DeadlineMonitorCancellation { get; } = new();

    internal bool TryMarkInFlight() =>
        Interlocked.CompareExchange(ref state, InFlightState, QueuedState) == QueuedState;

    internal bool TryMarkCompleted() =>
        Interlocked.Exchange(ref state, CompletedState) != CompletedState;

    internal bool IsQueued => Volatile.Read(ref state) == QueuedState;

    internal bool IsInFlight => Volatile.Read(ref state) == InFlightState;

    internal void RequestCancellation() =>
        Interlocked.Exchange(ref cancellationRequested, 1);
}

/// <summary>
/// Owns the bounded Phase 1.3 queue and its completion state. It contains no Autodesk types and
/// never executes caller-provided code. The SDK adapter is the sole consumer.
/// </summary>
public sealed class DocumentContextDispatcher : IBridgeContextStateProvider, IDisposable
{
    public const int DefaultMaxQueueDepth = 32;
    public static readonly TimeSpan MaximumDeadline = TimeSpan.FromSeconds(10);
    private readonly object syncRoot = new();
    private readonly LinkedList<DocumentDispatchWorkItem> queue = new();
    private readonly int maxQueueDepth;
    private readonly TimeProvider timeProvider;
    private string? instanceId;
    private bool ready;
    private bool stopping;
    private int modalDepth;
    private bool hasActiveDocument;
    private bool isQuiescent;
    private DocumentDispatchWorkItem? inFlight;
    private ContextDispatchLastStatus lastDispatchStatus = ContextDispatchLastStatus.None;
    private long contextRevision = 1;
    private bool disposed;

    public DocumentContextDispatcher(
        int maxQueueDepth = DefaultMaxQueueDepth,
        TimeProvider? timeProvider = null)
    {
        if (maxQueueDepth is < 1 or > DefaultMaxQueueDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxQueueDepth));
        }

        this.maxQueueDepth = maxQueueDepth;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Sets the current process identity without opening admission.</summary>
    public void ConfigureInstance(string expectedInstanceId)
    {
        if (!Guid.TryParse(expectedInstanceId, out _))
        {
            throw new ArgumentException("INSTANCE_ID_INVALID", nameof(expectedInstanceId));
        }

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (queue.Count != 0 || inFlight is not null)
            {
                throw new InvalidOperationException("DISPATCHER_NOT_DRAINED");
            }

            instanceId = expectedInstanceId;
            stopping = false;
            ready = false;
            modalDepth = 0;
            hasActiveDocument = false;
            isQuiescent = false;
            lastDispatchStatus = ContextDispatchLastStatus.None;
            contextRevision++;
        }
    }

    /// <summary>Opens admission after all SDK-bound event subscriptions are installed.</summary>
    public void MarkReady(bool activeDocumentExists, bool documentIsQuiescent)
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (instanceId is null || stopping)
            {
                throw new InvalidOperationException("DISPATCHER_INSTANCE_UNAVAILABLE");
            }

            hasActiveDocument = activeDocumentExists;
            isQuiescent = activeDocumentExists && documentIsQuiescent;
            ready = true;
            contextRevision++;
        }
    }

    /// <summary>Updates only safe application-context facts observed on the main thread.</summary>
    public void UpdateApplicationSnapshot(bool activeDocumentExists, bool documentIsQuiescent)
    {
        lock (syncRoot)
        {
            var nextQuiescent = activeDocumentExists && documentIsQuiescent;
            if (hasActiveDocument != activeDocumentExists || isQuiescent != nextQuiescent)
            {
                hasActiveDocument = activeDocumentExists;
                isQuiescent = nextQuiescent;
                contextRevision++;
            }
        }
    }

    /// <summary>Rejects queued and new requests immediately while AutoCAD is modal.</summary>
    public void EnterModal()
    {
        List<DocumentDispatchWorkItem> rejected;
        lock (syncRoot)
        {
            modalDepth++;
            contextRevision++;
            rejected = DrainQueuedUnsafe();
            lastDispatchStatus = ContextDispatchLastStatus.ApplicationModal;
        }

        CompleteAll(rejected, CadStatus.ApplicationModal);
    }

    public void LeaveModal()
    {
        lock (syncRoot)
        {
            var previousDepth = modalDepth;
            modalDepth = Math.Max(0, modalDepth - 1);
            if (previousDepth != modalDepth)
            {
                contextRevision++;
            }
        }
    }

    /// <summary>Validate, admit, and asynchronously await one bounded request.</summary>
    public Task<CadResultEnvelope> EnqueueAsync(
        ContextProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var validationFailure = ContextProbeRequestValidator.Validate(request, now);
        if (validationFailure is not null)
        {
            return Task.FromResult(CreateFailure(request, validationFailure.Value));
        }

        DocumentDispatchWorkItem item;
        CadStatus? admissionFailure = null;
        lock (syncRoot)
        {
            if (disposed || stopping)
            {
                admissionFailure = CadStatus.BridgeStopping;
            }
            else if (!ready || instanceId is null)
            {
                admissionFailure = CadStatus.DispatcherNotReady;
            }
            else if (!string.Equals(
                    request.ExpectedInstanceId,
                    instanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                admissionFailure = CadStatus.InstanceMismatch;
            }
            else if (modalDepth > 0)
            {
                admissionFailure = CadStatus.ApplicationModal;
            }
            else if (!hasActiveDocument)
            {
                admissionFailure = CadStatus.NoActiveDocument;
            }
            else if (!isQuiescent)
            {
                admissionFailure = CadStatus.DocumentBusy;
            }
            else if (queue.Count >= maxQueueDepth)
            {
                admissionFailure = CadStatus.QueueFull;
            }

            if (admissionFailure is not null)
            {
                lastDispatchStatus = Classify(admissionFailure.Value);
                contextRevision++;
                return Task.FromResult(CreateFailure(request, admissionFailure.Value));
            }

            item = new DocumentDispatchWorkItem(request);
            item.QueueNode = queue.AddLast(item);
            contextRevision++;
        }

        _ = MonitorAsync(item, cancellationToken);
        return item.Completion.Task;
    }

    /// <summary>
    /// Inspects exactly one FIFO item and establishes the single in-flight guard for a live item.
    /// Expired and cancelled items are completed without entering AutoCAD, allowing the SDK-bound
    /// Idle handler to count every inspected item against its fixed per-cycle budget.
    /// </summary>
    public DocumentDispatchTakeResult TryTakeNext(out DocumentDispatchWorkItem? workItem)
    {
        workItem = null;
        DocumentDispatchWorkItem? skipped = null;
        CadStatus? skippedStatus = null;
        lock (syncRoot)
        {
            if (!ready || stopping || modalDepth > 0 || inFlight is not null)
            {
                return DocumentDispatchTakeResult.NoWork;
            }

            if (queue.First is not { } node)
            {
                return DocumentDispatchTakeResult.NoWork;
            }

            queue.RemoveFirst();
            var candidate = node.Value;
            candidate.QueueNode = null;
            if (candidate.IsCancellationRequested)
            {
                skipped = candidate;
                skippedStatus = CadStatus.Cancelled;
            }
            else if (candidate.Request.DeadlineUtc <= timeProvider.GetUtcNow())
            {
                skipped = candidate;
                skippedStatus = CadStatus.Timeout;
            }
            else if (!candidate.TryMarkInFlight())
            {
                contextRevision++;
                return DocumentDispatchTakeResult.Skipped;
            }
            else
            {
                inFlight = candidate;
                workItem = candidate;
                contextRevision++;
                return DocumentDispatchTakeResult.Started;
            }

            lastDispatchStatus = Classify(skippedStatus.Value);
            contextRevision++;
        }

        _ = CompleteItem(skipped!, CreateFailure(skipped!.Request, skippedStatus!.Value));
        return DocumentDispatchTakeResult.Skipped;
    }

    /// <summary>Completes the current item exactly once and releases the in-flight guard.</summary>
    public bool Complete(DocumentDispatchWorkItem workItem, CadResultEnvelope result)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(result);
        lock (syncRoot)
        {
            if (!ReferenceEquals(inFlight, workItem))
            {
                return false;
            }

            inFlight = null;
            lastDispatchStatus = Classify(result.Status);
            contextRevision++;
        }

        return CompleteItem(workItem, result);
    }

    /// <summary>Fail closed during shutdown and cancel every queued request.</summary>
    public void BeginStopping()
    {
        List<DocumentDispatchWorkItem> pending;
        DocumentDispatchWorkItem? active;
        lock (syncRoot)
        {
            if (stopping)
            {
                return;
            }

            stopping = true;
            ready = false;
            contextRevision++;
            pending = DrainQueuedUnsafe();
            active = inFlight;
            active?.RequestCancellation();
        }

        CompleteAll(pending, CadStatus.BridgeStopping);
        if (active is not null)
        {
            _ = CompleteItem(active, CreateFailure(active.Request, CadStatus.BridgeStopping));
        }
    }

    /// <summary>
    /// Releases a command-context item that the host can no longer execute. This is used only
    /// after event unsubscription during final host termination.
    /// </summary>
    public void AbandonInFlight()
    {
        DocumentDispatchWorkItem? active;
        lock (syncRoot)
        {
            active = inFlight;
            inFlight = null;
        }

        if (active is not null)
        {
            _ = CompleteItem(active, CreateFailure(active.Request, CadStatus.BridgeStopping));
        }
    }

    public BridgeContextSnapshot GetSnapshot()
    {
        lock (syncRoot)
        {
            return new BridgeContextSnapshot(
                Revision: contextRevision,
                DispatcherReady: ready && !stopping,
                DispatcherState: stopping ? "STOPPING" : ready ? "READY" : "NOT_READY",
                QueueDepth: queue.Count,
                InFlightCount: inFlight is null ? 0 : 1,
                Modal: modalDepth > 0,
                HasActiveDocument: hasActiveDocument,
                IsQuiescent: isQuiescent,
                LastDispatchStatus: lastDispatchStatus);
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        BeginStopping();
        AbandonInFlight();
        GC.SuppressFinalize(this);
    }

    internal static CadResultEnvelope CreateFailure(
        ContextProbeRequest request,
        CadStatus status,
        long durationMs = 0) =>
        CadResultEnvelope.Failure(
            SafeRequestId(request.RequestId),
            SafeTraceId(request.TraceId),
            status,
            SafeMessage(status),
            durationMs: durationMs);

    private async Task MonitorAsync(
        DocumentDispatchWorkItem item,
        CancellationToken callerCancellation)
    {
        using var callerRegistration = callerCancellation.Register(
            () => Cancel(item, CadStatus.Cancelled));
        var delay = item.Request.DeadlineUtc - timeProvider.GetUtcNow();
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(
                    delay,
                    timeProvider,
                    item.DeadlineMonitorCancellation.Token).ConfigureAwait(false);
            }

            Cancel(item, CadStatus.Timeout);
        }
        catch (OperationCanceledException)
        {
            // Normal completion cancels the per-item deadline monitor.
        }
        finally
        {
            item.DeadlineMonitorCancellation.Dispose();
        }
    }

    private void Cancel(DocumentDispatchWorkItem item, CadStatus status)
    {
        var completeNow = false;
        lock (syncRoot)
        {
            item.RequestCancellation();
            if (item.IsQueued && item.QueueNode is { List: not null } node)
            {
                queue.Remove(node);
                item.QueueNode = null;
                completeNow = true;
            }
            else if (item.IsInFlight)
            {
                completeNow = true;
            }

            if (completeNow)
            {
                lastDispatchStatus = Classify(status);
                contextRevision++;
            }
        }

        if (completeNow)
        {
            _ = CompleteItem(item, CreateFailure(item.Request, status));
        }
    }

    private List<DocumentDispatchWorkItem> DrainQueuedUnsafe()
    {
        var items = queue.ToList();
        queue.Clear();
        foreach (var item in items)
        {
            item.QueueNode = null;
        }

        return items;
    }

    private static bool CompleteItem(
        DocumentDispatchWorkItem item,
        CadResultEnvelope result)
    {
        if (!item.TryMarkCompleted())
        {
            return false;
        }

        item.DeadlineMonitorCancellation.Cancel();
        return item.Completion.TrySetResult(result);
    }

    private static void CompleteAll(
        IEnumerable<DocumentDispatchWorkItem> items,
        CadStatus status)
    {
        foreach (var item in items)
        {
            _ = CompleteItem(item, CreateFailure(item.Request, status));
        }
    }

    private static ContextDispatchLastStatus Classify(CadStatus status) => status switch
    {
        CadStatus.Ok => ContextDispatchLastStatus.Ok,
        CadStatus.NoActiveDocument => ContextDispatchLastStatus.NoActiveDocument,
        CadStatus.DocumentBusy => ContextDispatchLastStatus.DocumentBusy,
        CadStatus.ApplicationModal => ContextDispatchLastStatus.ApplicationModal,
        CadStatus.Timeout => ContextDispatchLastStatus.Timeout,
        CadStatus.Cancelled => ContextDispatchLastStatus.Cancelled,
        _ => ContextDispatchLastStatus.Failed,
    };

    private static string SafeRequestId(string value) =>
        Guid.TryParse(value, out var requestId)
            ? requestId.ToString("D")
            : Guid.NewGuid().ToString("D");

    private static string SafeTraceId(string value) =>
        ContextProbeRequestValidator.IsSafeTraceId(value)
            ? value
            : Guid.NewGuid().ToString("D");

    private static string SafeMessage(CadStatus status) => status switch
    {
        CadStatus.Cancelled => "CAD-MAX context probe was cancelled",
        CadStatus.DispatcherNotReady => "CAD-MAX context dispatcher is not ready",
        CadStatus.InstanceMismatch => "CAD-MAX AutoCAD instance does not match",
        CadStatus.NoActiveDocument => "AutoCAD has no active document",
        CadStatus.DocumentNotActive => "The requested document is not active",
        CadStatus.DocumentDestroyed => "The requested document was destroyed",
        CadStatus.DocumentNotFound => "The requested document is not available",
        CadStatus.ApplicationModal => "AutoCAD is in a modal state",
        CadStatus.DocumentBusy => "The active AutoCAD document is busy",
        CadStatus.QueueFull => "CAD-MAX context dispatch queue is full",
        CadStatus.Timeout => "CAD-MAX context probe timed out",
        CadStatus.MainThreadDispatchFailed => "AutoCAD main-thread dispatch failed",
        CadStatus.CommandContextFailed => "AutoCAD document command-context dispatch failed",
        CadStatus.BridgeStopping => "CAD-MAX AutoCAD bridge is stopping",
        CadStatus.InvalidArgument => "CAD-MAX context probe request is invalid",
        CadStatus.SchemaMismatch => "CAD-MAX context probe schema is incompatible",
        _ => "CAD-MAX context probe failed",
    };
}

internal static partial class ContextProbeRequestValidator
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTraceRegex();

    [GeneratedRegex("^doc_[A-Za-z0-9_-]{22}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentIdRegex();

    internal static CadStatus? Validate(ContextProbeRequest request, DateTimeOffset now)
    {
        if (!string.Equals(request.SchemaVersion, CadProtocol.SchemaVersion, StringComparison.Ordinal))
        {
            return CadStatus.SchemaMismatch;
        }

        if (!Guid.TryParse(request.RequestId, out _)
            || !IsSafeTraceId(request.TraceId)
            || !Guid.TryParse(request.ExpectedInstanceId, out _)
            || (request.ExpectedDocumentId is not null
                && !DocumentIdRegex().IsMatch(request.ExpectedDocumentId)))
        {
            return CadStatus.InvalidArgument;
        }

        var remaining = request.DeadlineUtc - now;
        if (remaining <= TimeSpan.Zero)
        {
            return CadStatus.Timeout;
        }

        return remaining > DocumentContextDispatcher.MaximumDeadline
            ? CadStatus.InvalidArgument
            : null;
    }

    internal static bool IsSafeTraceId(string? value) =>
        value is not null && SafeTraceRegex().IsMatch(value);
}
