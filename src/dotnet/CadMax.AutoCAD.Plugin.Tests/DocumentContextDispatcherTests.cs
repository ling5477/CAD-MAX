using CadMax.Contracts;
using System.Runtime.CompilerServices;
using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class DocumentContextDispatcherTests
{
    [Fact]
    public async Task QueueIsFifoAndAllowsOnlyOneInFlightItem()
    {
        using var dispatcher = CreateReadyDispatcher();
        var firstTask = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        var secondTask = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var first));
        Assert.NotNull(first);
        Assert.Equal(DocumentDispatchTakeResult.NoWork, dispatcher.TryTakeNext(out _));
        Assert.Equal(1, dispatcher.GetSnapshot().InFlightCount);
        Assert.Equal(1, dispatcher.GetSnapshot().QueueDepth);

        Assert.True(dispatcher.Complete(first!, Success(first!)));
        Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var second));
        Assert.NotNull(second);
        Assert.NotEqual(first.DispatchId, second!.DispatchId);
        Assert.True(dispatcher.Complete(second, Success(second)));

        Assert.True((await firstTask).Success);
        Assert.True((await secondTask).Success);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
    }

    [Fact]
    public async Task QueueFullFailsWithoutExceedingBound()
    {
        using var dispatcher = CreateReadyDispatcher(maxQueueDepth: 2);
        _ = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        _ = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);

        var rejected = await dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(CadStatus.QueueFull, rejected.Status);
        Assert.Equal(2, dispatcher.GetSnapshot().QueueDepth);
    }

    [Fact]
    public async Task ExpiredBeforeAdmissionAndWhileQueuedReturnTimeout()
    {
        using var dispatcher = CreateReadyDispatcher();
        var expired = CreateRequest(DateTimeOffset.UtcNow.AddMilliseconds(-1));
        var queued = CreateRequest(DateTimeOffset.UtcNow.AddMilliseconds(30));

        var expiredResult = await dispatcher.EnqueueAsync(expired, CancellationToken.None);
        var queuedResult = await dispatcher.EnqueueAsync(queued, CancellationToken.None);

        Assert.Equal(CadStatus.Timeout, expiredResult.Status);
        Assert.Equal(CadStatus.Timeout, queuedResult.Status);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
    }

    [Fact]
    public async Task CancellationBeforeAndAfterDequeueCompletesExactlyOnce()
    {
        using var dispatcher = CreateReadyDispatcher();
        using var queuedCancellation = new CancellationTokenSource();
        var queuedTask = dispatcher.EnqueueAsync(CreateRequest(), queuedCancellation.Token);
        queuedCancellation.Cancel();
        Assert.Equal(CadStatus.Cancelled, (await queuedTask).Status);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);

        using var activeCancellation = new CancellationTokenSource();
        var activeTask = dispatcher.EnqueueAsync(CreateRequest(), activeCancellation.Token);
        Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var active));
        activeCancellation.Cancel();
        Assert.Equal(CadStatus.Cancelled, (await activeTask).Status);
        Assert.False(dispatcher.Complete(active!, Success(active!)));
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
    }

    [Fact]
    public async Task AlreadyCancelledAdmissionDoesNotFaultDeadlineMonitor()
    {
        using var dispatcher = CreateReadyDispatcher();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await dispatcher.EnqueueAsync(CreateRequest(), cancellation.Token);

        Assert.Equal(CadStatus.Cancelled, result.Status);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
    }

    [Fact]
    public async Task ModalAndShutdownRejectAdmissionAndDrainPending()
    {
        using var dispatcher = CreateReadyDispatcher();
        var pending = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        dispatcher.EnterModal();

        Assert.Equal(CadStatus.ApplicationModal, (await pending).Status);
        Assert.True(dispatcher.GetSnapshot().Modal);
        Assert.Equal(
            CadStatus.ApplicationModal,
            (await dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None)).Status);

        dispatcher.LeaveModal();
        dispatcher.BeginStopping();
        Assert.Equal(
            CadStatus.BridgeStopping,
            (await dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None)).Status);
        Assert.Equal("STOPPING", dispatcher.GetSnapshot().DispatcherState);
    }

    [Fact]
    public async Task AdmissionSnapshotRejectsNoDocumentAndBusyWithoutQueueing()
    {
        using var dispatcher = new DocumentContextDispatcher();
        dispatcher.ConfigureInstance(InstanceId);
        dispatcher.MarkReady(activeDocumentExists: false, documentIsQuiescent: false);

        var noDocument = await dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        dispatcher.UpdateApplicationSnapshot(activeDocumentExists: true, documentIsQuiescent: false);
        var busy = await dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(CadStatus.NoActiveDocument, noDocument.Status);
        Assert.Equal(CadStatus.DocumentBusy, busy.Status);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
        Assert.Equal(ContextDispatchLastStatus.DocumentBusy, dispatcher.GetSnapshot().LastDispatchStatus);
    }

    [Fact]
    public async Task CommandContextCoordinatorVerifiesSchedulerAndCompletesExactlyOnce()
    {
        using var dispatcher = CreateReadyDispatcher();
        var coordinator = new DocumentCommandContextCoordinator(dispatcher);
        var task = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var item));
        var callbackCount = 0;
        var scheduler = new FakeCommandContextScheduler();

        coordinator.Start(
            item!,
            scheduler,
            workItem =>
            {
                callbackCount++;
                _ = dispatcher.Complete(workItem, Success(workItem));
                return Task.CompletedTask;
            });

        Assert.True((await task).Success);
        Assert.Equal(1, callbackCount);
        Assert.Equal(1, scheduler.ExecutionCount);
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
    }

    [Theory]
    [InlineData(false, false, CadStatus.MainThreadDispatchFailed)]
    [InlineData(true, true, CadStatus.MainThreadDispatchFailed)]
    [InlineData(true, false, CadStatus.CommandContextFailed)]
    public async Task CommandContextCoordinatorFailsClosed(
        bool isMainThread,
        bool throwSynchronously,
        CadStatus expectedStatus)
    {
        using var dispatcher = CreateReadyDispatcher();
        var coordinator = new DocumentCommandContextCoordinator(dispatcher);
        var task = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var item));
        var scheduler = new FakeCommandContextScheduler
        {
            IsOnMainThread = isMainThread,
            ThrowSynchronously = throwSynchronously,
            FailAsynchronously = isMainThread && !throwSynchronously,
        };

        coordinator.Start(item!, scheduler, _ => Task.CompletedTask);

        Assert.Equal(expectedStatus, (await task).Status);
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
    }

    [Fact]
    public async Task RepeatedConfigureCycleAndTenThousandItemsRetainNothing()
    {
        using var dispatcher = CreateReadyDispatcher();
        for (var index = 0; index < 10_000; index++)
        {
            var task = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
            Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var item));
            Assert.True(dispatcher.Complete(item!, Success(item!)));
            Assert.True((await task).Success);
        }

        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
        dispatcher.BeginStopping();
        dispatcher.AbandonInFlight();
        dispatcher.ConfigureInstance(Guid.NewGuid().ToString("D"));
        dispatcher.MarkReady(false, false);
        Assert.True(dispatcher.GetSnapshot().DispatcherReady);
    }

    [Fact]
    public void OpaqueIdentityIsStableDistinctRemovedAndRestartScoped()
    {
        var registry = new OpaqueSessionIdentityRegistry<object>();
        var first = new object();
        var second = new object();
        var firstId = registry.GetOrCreate(first);
        var secondId = registry.GetOrCreate(second);

        Assert.Matches("^doc_[A-Za-z0-9_-]{22}$", firstId);
        Assert.Equal(firstId, registry.GetOrCreate(first));
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(CadStatus.DocumentNotActive, registry.ResolveActive(second, firstId).ErrorStatus);
        registry.Remove(first);
        Assert.Equal(CadStatus.DocumentNotFound, registry.ResolveActive(second, firstId).ErrorStatus);

        registry.Reset();
        var restartedId = registry.GetOrCreate(first);
        Assert.NotEqual(firstId, restartedId);
        Assert.DoesNotContain("path", restartedId, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, registry.EntryCount);
    }

    [Fact]
    public void OpaqueIdentityDoesNotRetainDestroyedObject()
    {
        var registry = new OpaqueSessionIdentityRegistry<object>();
        var weakReference = CreateWeakIdentity(registry);

        for (var attempt = 0; attempt < 10 && weakReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        registry.Prune();
        Assert.False(weakReference.IsAlive);
        Assert.Equal(0, registry.EntryCount);
    }

    [Fact]
    public async Task ApplicationDrawingInspectionDoesNotRequireAnActiveDocument()
    {
        using var dispatcher = new DocumentContextDispatcher();
        dispatcher.ConfigureInstance(InstanceId);
        dispatcher.MarkReady(activeDocumentExists: false, documentIsQuiescent: false);
        var request = CreateDrawingRequest(DrawingOperation.ListDocuments);

        var task = dispatcher.EnqueueDrawingAsync(request, CancellationToken.None);

        Assert.Equal(
            DocumentDispatchTakeResult.Started,
            dispatcher.TryTakeNext(out var item));
        Assert.NotNull(item);
        Assert.Equal(DocumentDispatchScope.Application, item.Scope);
        Assert.Same(request, item.DrawingRequest);
        Assert.True(dispatcher.Complete(item, Success(item)));
        Assert.True((await task).Success);
    }

    [Fact]
    public async Task DocumentDrawingInspectionRequiresActiveQuiescentDocument()
    {
        using var dispatcher = new DocumentContextDispatcher();
        dispatcher.ConfigureInstance(InstanceId);
        dispatcher.MarkReady(activeDocumentExists: false, documentIsQuiescent: false);

        var result = await dispatcher.EnqueueDrawingAsync(
            CreateDrawingRequest(DrawingOperation.Units),
            CancellationToken.None);

        Assert.Equal(CadStatus.NoActiveDocument, result.Status);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
    }

    [Fact]
    public async Task ApplicationOperationRejectsDocumentSelector()
    {
        using var dispatcher = CreateReadyDispatcher();
        var request = CreateDrawingRequest(DrawingOperation.Status) with
        {
            ExpectedDocumentId = "doc_AAAAAAAAAAAAAAAAAAAAAA",
        };

        var result = await dispatcher.EnqueueDrawingAsync(request, CancellationToken.None);

        Assert.Equal(CadStatus.InvalidArgument, result.Status);
        Assert.Equal(0, dispatcher.GetSnapshot().QueueDepth);
    }

    [Fact]
    public async Task CompletionSurvivesDisposedDeadlineMonitorCancellation()
    {
        using var dispatcher = CreateReadyDispatcher();
        var task = dispatcher.EnqueueAsync(CreateRequest(), CancellationToken.None);
        Assert.Equal(DocumentDispatchTakeResult.Started, dispatcher.TryTakeNext(out var item));
        Assert.NotNull(item);

        item!.DeadlineMonitorCancellation.Dispose();

        Assert.True(dispatcher.Complete(item, Success(item)));
        Assert.True((await task.WaitAsync(TimeSpan.FromSeconds(1))).Success);
        Assert.Equal(0, dispatcher.GetSnapshot().InFlightCount);
    }

    private static DocumentContextDispatcher CreateReadyDispatcher(int maxQueueDepth = 32)
    {
        var dispatcher = new DocumentContextDispatcher(maxQueueDepth);
        dispatcher.ConfigureInstance(InstanceId);
        dispatcher.MarkReady(activeDocumentExists: true, documentIsQuiescent: true);
        return dispatcher;
    }

    private static ContextProbeRequest CreateRequest(DateTimeOffset? deadline = null) =>
        new(
            CadProtocol.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            deadline ?? DateTimeOffset.UtcNow.AddSeconds(5),
            InstanceId,
            ExpectedDocumentId: null);

    private static DrawingInspectRequest CreateDrawingRequest(DrawingOperation operation) =>
        new(
            CadProtocol.SchemaVersion,
            Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow.AddSeconds(5),
            InstanceId,
            operation,
            ExpectedDocumentId: null);

    private static CadResultEnvelope Success(DocumentDispatchWorkItem item) =>
        CadResultEnvelope.Ok(
            item.Request.RequestId,
            item.Request.TraceId,
            "ok");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakIdentity(OpaqueSessionIdentityRegistry<object> registry)
    {
        var value = new object();
        _ = registry.GetOrCreate(value);
        return new WeakReference(value);
    }

    private static string InstanceId { get; } = Guid.NewGuid().ToString("D");

    private sealed class FakeCommandContextScheduler : IDocumentCommandContextScheduler
    {
        public bool IsOnMainThread { get; init; } = true;

        public bool ThrowSynchronously { get; init; }

        public bool FailAsynchronously { get; init; }

        public int ExecutionCount { get; private set; }

        public Task ExecuteAsync(Func<Task> callback)
        {
            ExecutionCount++;
            if (ThrowSynchronously)
            {
                throw new InvalidOperationException("FAKE_APPLICATION_CONTEXT_FAILED");
            }

            return FailAsynchronously
                ? Task.FromException(new InvalidOperationException("FAKE_COMMAND_CONTEXT_FAILED"))
                : callback();
        }
    }
}
