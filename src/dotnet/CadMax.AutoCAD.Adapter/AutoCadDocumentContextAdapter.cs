using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using CadMax.AutoCAD.Plugin;
using CadMax.Contracts;
using AutoCADApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadMax.AutoCAD.Adapter;

/// <summary>
/// SDK-bound application/document-context adapter. Autodesk objects never leave this type and
/// are never stored in queue items, HTTP models, static SDK-free state, or Python.
/// </summary>
internal sealed class AutoCadDocumentContextAdapter
{
    private const int MaxItemsPerIdle = 4;
    private static readonly TimeSpan MaxDrainDuration = TimeSpan.FromMilliseconds(25);
    private readonly object lifecycleLock = new();
    private readonly DocumentContextDispatcher queue;
    private readonly DocumentCommandContextCoordinator commandContextCoordinator;
    private readonly OpaqueSessionIdentityRegistry<Document> identities = new();
    private DocumentCollection? documents;
    private int mainThreadId;
    private bool initialized;

    internal AutoCadDocumentContextAdapter(DocumentContextDispatcher queue)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        commandContextCoordinator = new DocumentCommandContextCoordinator(queue);
    }

    internal bool Initialize()
    {
        lock (lifecycleLock)
        {
            if (initialized)
            {
                return true;
            }

            try
            {
                mainThreadId = Environment.CurrentManagedThreadId;
                documents = AutoCADApplication.DocumentManager;
                AutoCADApplication.Idle += OnIdle;
                AutoCADApplication.EnterModal += OnEnterModal;
                AutoCADApplication.LeaveModal += OnLeaveModal;
                AutoCADApplication.BeginQuit += OnBeginQuit;
                documents.DocumentCreated += OnDocumentCreated;
                documents.DocumentActivated += OnDocumentActivated;
                documents.DocumentBecameCurrent += OnDocumentBecameCurrent;
                documents.DocumentActivationChanged += OnDocumentActivationChanged;
                documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
                documents.DocumentDestroyed += OnDocumentDestroyed;
                initialized = true;
                var activeDocument = documents.MdiActiveDocument;
                var isQuiescent = activeDocument?.Editor.IsQuiescent ?? false;
                if (activeDocument is not null)
                {
                    _ = identities.GetOrCreate(activeDocument);
                }

                queue.MarkReady(activeDocument is not null, isQuiescent);
                return true;
            }
            catch (Exception)
            {
                UnsubscribeSafely();
                queue.BeginStopping();
                return false;
            }
        }
    }

    internal bool Terminate()
    {
        lock (lifecycleLock)
        {
            if (!initialized)
            {
                return true;
            }

            queue.BeginStopping();
            UnsubscribeSafely();
            queue.AbandonInFlight();
            identities.Reset();
            documents = null;
            mainThreadId = 0;
            initialized = false;
            return true;
        }
    }

    private void OnIdle(object? sender, EventArgs eventArgs)
    {
        try
        {
            var activeDocuments = documents;
            if (!initialized || activeDocuments is null)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId != mainThreadId)
            {
                if (queue.TryTakeNext(out var wrongThreadItem)
                    == DocumentDispatchTakeResult.Started
                    && wrongThreadItem is not null)
                {
                    _ = queue.Complete(
                        wrongThreadItem,
                        Failure(wrongThreadItem, CadStatus.MainThreadDispatchFailed));
                }

                return;
            }

            var cycleStarted = Stopwatch.GetTimestamp();
            for (var count = 0; count < MaxItemsPerIdle; count++)
            {
                if (Stopwatch.GetElapsedTime(cycleStarted) >= MaxDrainDuration)
                {
                    return;
                }

                var activeDocument = activeDocuments.MdiActiveDocument;
                var isQuiescent = activeDocument?.Editor.IsQuiescent ?? false;
                queue.UpdateApplicationSnapshot(activeDocument is not null, isQuiescent);
                var takeResult = queue.TryTakeNext(out var item);
                if (takeResult == DocumentDispatchTakeResult.NoWork)
                {
                    return;
                }

                if (takeResult == DocumentDispatchTakeResult.Skipped)
                {
                    continue;
                }

                if (item is null)
                {
                    return;
                }

                if (activeDocument is null)
                {
                    _ = queue.Complete(item, Failure(item, CadStatus.NoActiveDocument));
                    continue;
                }

                if (!isQuiescent)
                {
                    _ = queue.Complete(item, Failure(item, CadStatus.DocumentBusy));
                    continue;
                }

                commandContextCoordinator.Start(
                    item,
                    new AutoCadCommandContextScheduler(activeDocuments, mainThreadId),
                    ExecuteProbeCallbackAsync);
                return;
            }
        }
        catch (Exception)
        {
            // AutoCAD event handlers never allow exceptions to escape the host.
        }
    }

    private Task ExecuteProbeCallbackAsync(DocumentDispatchWorkItem item)
    {
        var executionStarted = Stopwatch.GetTimestamp();
        try
        {
            if (item.IsCancellationRequested || item.Request.DeadlineUtc <= DateTimeOffset.UtcNow)
            {
                _ = queue.Complete(
                    item,
                    Failure(
                        item,
                        item.IsCancellationRequested ? CadStatus.Cancelled : CadStatus.Timeout));
                return Task.CompletedTask;
            }

            if (Environment.CurrentManagedThreadId != mainThreadId)
            {
                _ = queue.Complete(item, Failure(item, CadStatus.MainThreadDispatchFailed));
                return Task.CompletedTask;
            }

            var activeDocument = documents?.MdiActiveDocument;
            if (activeDocument is null)
            {
                queue.UpdateApplicationSnapshot(false, false);
                _ = queue.Complete(item, Failure(item, CadStatus.NoActiveDocument));
                return Task.CompletedTask;
            }

            // ExecuteInCommandContextAsync changes Editor.IsQuiescent to false while its
            // callback is active. Busy was authoritatively rechecked on this same main thread in
            // OnIdle immediately before dispatch; reading it again here would reject every valid
            // command-context callback. Re-resolve the active document below so a document switch
            // or destruction still fails closed.

            var resolution = identities.ResolveActive(
                activeDocument,
                item.Request.ExpectedDocumentId);
            if (resolution.ErrorStatus is not null)
            {
                _ = queue.Complete(item, Failure(item, resolution.ErrorStatus.Value));
                return Task.CompletedTask;
            }

            var result = CadResultEnvelope.Ok(
                item.Request.RequestId,
                item.Request.TraceId,
                "AutoCAD document context dispatch completed",
                new ContextProbeData(
                    item.Request.ExpectedInstanceId,
                    item.DispatchId,
                    MainThreadVerified: true,
                    ExecutionContext: "DOCUMENT_COMMAND_CONTEXT",
                    DocumentState: "ACTIVE",
                    resolution.Identity!,
                    IsQuiescent: true,
                    QueueDelayMs: ElapsedMilliseconds(item.EnqueuedTimestamp),
                    ExecutionMs: ElapsedMilliseconds(executionStarted)),
                durationMs: ElapsedMilliseconds(item.EnqueuedTimestamp));
            _ = queue.Complete(item, result);
        }
        catch (Exception)
        {
            _ = queue.Complete(item, Failure(item, CadStatus.CommandContextFailed));
        }

        return Task.CompletedTask;
    }

    private void OnEnterModal(object? sender, EventArgs eventArgs)
    {
        try
        {
            queue.EnterModal();
        }
        catch (Exception)
        {
            // Modal notification must remain bounded and host-safe.
        }
    }

    private void OnLeaveModal(object? sender, EventArgs eventArgs)
    {
        try
        {
            queue.LeaveModal();
        }
        catch (Exception)
        {
            // Modal notification must remain bounded and host-safe.
        }
    }

    private void OnBeginQuit(object sender, BeginQuitEventArgs eventArgs)
    {
        try
        {
            queue.BeginStopping();
        }
        catch (Exception)
        {
            // BeginQuit cannot be vetoed or delayed by context cleanup.
        }
    }

    private void OnDocumentCreated(object sender, DocumentCollectionEventArgs eventArgs) =>
        ObserveDocument(eventArgs.Document);

    private void OnDocumentActivated(object sender, DocumentCollectionEventArgs eventArgs) =>
        ObserveDocument(eventArgs.Document);

    private void OnDocumentBecameCurrent(object sender, DocumentCollectionEventArgs eventArgs) =>
        ObserveDocument(eventArgs.Document);

    private void OnDocumentActivationChanged(object sender, DocumentActivationChangedEventArgs eventArgs) =>
        UpdateSnapshotSafely();

    private void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs eventArgs)
    {
        try
        {
            identities.Remove(eventArgs.Document);
            UpdateSnapshotSafely();
        }
        catch (Exception)
        {
            // Destruction invalidation is best-effort here and rechecked during every dispatch.
        }
    }

    private void OnDocumentDestroyed(object sender, DocumentDestroyedEventArgs eventArgs)
    {
        try
        {
            identities.Prune();
            UpdateSnapshotSafely();
        }
        catch (Exception)
        {
            // The FileName property is deliberately not read or recorded.
        }
    }

    private void ObserveDocument(Document document)
    {
        try
        {
            _ = identities.GetOrCreate(document);
            UpdateSnapshotSafely();
        }
        catch (Exception)
        {
            // Lifecycle events never propagate into AutoCAD.
        }
    }

    private void UpdateSnapshotSafely()
    {
        var activeDocument = documents?.MdiActiveDocument;
        queue.UpdateApplicationSnapshot(
            activeDocument is not null,
            activeDocument?.Editor.IsQuiescent ?? false);
    }

    private void UnsubscribeSafely()
    {
        try
        {
            AutoCADApplication.Idle -= OnIdle;
            AutoCADApplication.EnterModal -= OnEnterModal;
            AutoCADApplication.LeaveModal -= OnLeaveModal;
            AutoCADApplication.BeginQuit -= OnBeginQuit;
            if (documents is not null)
            {
                documents.DocumentCreated -= OnDocumentCreated;
                documents.DocumentActivated -= OnDocumentActivated;
                documents.DocumentBecameCurrent -= OnDocumentBecameCurrent;
                documents.DocumentActivationChanged -= OnDocumentActivationChanged;
                documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                documents.DocumentDestroyed -= OnDocumentDestroyed;
            }
        }
        catch (Exception)
        {
            // Repeated Terminate remains safe even if AutoCAD is already tearing down events.
        }
    }

    private static CadResultEnvelope Failure(
        DocumentDispatchWorkItem item,
        CadStatus status) =>
        CadResultEnvelope.Failure(
            item.Request.RequestId,
            item.Request.TraceId,
            status,
            status switch
            {
                CadStatus.NoActiveDocument => "AutoCAD has no active document",
                CadStatus.DocumentBusy => "The active AutoCAD document is busy",
                CadStatus.DocumentNotActive => "The requested document is not active",
                CadStatus.DocumentNotFound => "The requested document is not available",
                CadStatus.Cancelled => "CAD-MAX context probe was cancelled",
                CadStatus.Timeout => "CAD-MAX context probe timed out",
                CadStatus.MainThreadDispatchFailed => "AutoCAD main-thread dispatch failed",
                CadStatus.CommandContextFailed => "AutoCAD document command-context dispatch failed",
                _ => "CAD-MAX context probe failed",
            },
            durationMs: ElapsedMilliseconds(item.EnqueuedTimestamp));

    private static long ElapsedMilliseconds(long startedTimestamp) =>
        Math.Max(0, (long)Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);

    private sealed class AutoCadCommandContextScheduler(
        DocumentCollection documents,
        int mainThreadId) : IDocumentCommandContextScheduler
    {
        public bool IsOnMainThread =>
            Environment.CurrentManagedThreadId == mainThreadId;

        public async Task ExecuteAsync(Func<Task> callback) =>
            await documents.ExecuteInCommandContextAsync(_ => callback(), null);
    }
}
