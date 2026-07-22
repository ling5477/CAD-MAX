using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class PluginLifecycleTests
{
    [Fact]
    public void StateMachineAcceptsInitializeAndTerminatePath()
    {
        var stateMachine = new PluginLifecycleStateMachine();

        stateMachine.TransitionTo(PluginLifecycleState.Starting);
        stateMachine.TransitionTo(PluginLifecycleState.Listening);
        stateMachine.TransitionTo(PluginLifecycleState.Ready);
        stateMachine.TransitionTo(PluginLifecycleState.Stopping);
        stateMachine.TransitionTo(PluginLifecycleState.Stopped);

        Assert.Equal(PluginLifecycleState.Stopped, stateMachine.State);
    }

    [Fact]
    public void StateMachineRejectsIllegalTransition()
    {
        var stateMachine = new PluginLifecycleStateMachine();

        var exception = Assert.Throws<InvalidOperationException>(
            () => stateMachine.TransitionTo(PluginLifecycleState.Ready));

        Assert.Equal("ILLEGAL_LIFECYCLE_TRANSITION", exception.Message);
        Assert.Equal(PluginLifecycleState.Stopped, stateMachine.State);
    }

    [Fact]
    public void ControllerInitializesAndTerminatesWithExpectedEvidence()
    {
        var writer = new CapturingEvidenceWriter();
        var controller = CreateController(writer);
        var runtime = new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true);

        Assert.True(controller.Initialize(runtime, processId: 42));
        Assert.Equal(PluginLifecycleState.Ready, controller.State);
        Assert.True(controller.Terminate(processId: 42));
        Assert.Equal(PluginLifecycleState.Stopped, controller.State);

        Assert.Collection(
            writer.Entries,
            entry => Assert.Equal("PLUGIN_INITIALIZE_STARTED", entry.EventType),
            entry => Assert.Equal("BRIDGE_LISTENER_STARTED", entry.EventType),
            entry => Assert.Equal("PLUGIN_INITIALIZE_SUCCEEDED", entry.EventType),
            entry => Assert.Equal("PLUGIN_TERMINATE_STARTED", entry.EventType),
            entry => Assert.Equal("BRIDGE_LISTENER_STOPPED", entry.EventType),
            entry => Assert.Equal("PLUGIN_TERMINATE_SUCCEEDED", entry.EventType));
    }

    [Fact]
    public void InvalidMetadataFailsInitialization()
    {
        var writer = new CapturingEvidenceWriter();
        var metadata = new PluginMetadata("unexpected", "0.1.0", "1.0");
        var controller = CreateController(writer, metadata);

        var initialized = controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true));

        Assert.False(initialized);
        Assert.Equal(PluginLifecycleState.Failed, controller.State);
        Assert.Equal("PLUGIN_INITIALIZE_FAILED", writer.Entries[^1].EventType);
        Assert.Equal("PLUGIN_INITIALIZATION_FAILED", writer.Entries[^1].SafeErrorCode);
    }

    [Fact]
    public void EvidenceFailureDoesNotEscapeOrAdvertiseReadyState()
    {
        var controller = CreateController(new ThrowingEvidenceWriter());
        var initialized = true;

        var exception = Record.Exception(
            () => initialized = controller.Initialize(
                new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        Assert.Null(exception);
        Assert.False(initialized);
        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
    }

    [Fact]
    public void FalseEvidenceResultDoesNotAdvertiseReadyState()
    {
        var controller = CreateController(new RejectingEvidenceWriter());

        var initialized = controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true));

        Assert.False(initialized);
        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
    }

    [Fact]
    public void TerminateStopsSafelyWhenRequiredEvidenceFails()
    {
        var writer = new FailAfterEvidenceWriter(successfulWrites: 3);
        var controller = CreateController(writer);
        var runtime = new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true);

        Assert.True(controller.Initialize(runtime));

        Assert.False(controller.Terminate());
        Assert.Equal(PluginLifecycleState.Stopped, controller.State);
    }

    [Fact]
    public void InitializeAndTerminateAreIdempotentAtStableStates()
    {
        var writer = new CapturingEvidenceWriter();
        var controller = CreateController(writer);
        var runtime = new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true);

        Assert.True(controller.Initialize(runtime));
        Assert.True(controller.Initialize(runtime));
        Assert.Equal(3, writer.Entries.Count);

        Assert.True(controller.Terminate());
        Assert.True(controller.Terminate());
        Assert.Equal(6, writer.Entries.Count);
    }

    [Fact]
    public void MissingAutoCADHostAttestationFailsClosedWithSafeStatus()
    {
        var controller = CreateController(new CapturingEvidenceWriter());

        var initialized = controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", false));
        var status = controller.GetStatus();

        Assert.False(initialized);
        Assert.Equal(PluginLifecycleState.Failed, controller.State);
        Assert.Equal("CAD-MAX", status.PluginName);
        Assert.Equal("UNKNOWN", status.PluginVersion);
        Assert.Equal("1.0", status.SchemaVersion);
        Assert.Equal(0, status.AutoCADYear);
        Assert.True(status.ReadOnly);
        Assert.False(status.AllowWrite);
        Assert.False(status.AllowScript);
    }

    [Fact]
    public void MissingTokenDegradesWithoutStartingListener()
    {
        var factory = new FakeServerFactory();
        var controller = new PluginLifecycleController(
            PluginMetadata.CreateDefault(),
            new CapturingEvidenceWriter(),
            new FailedTokenSource("TOKEN_NOT_CONFIGURED"),
            factory);

        Assert.False(controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
        Assert.Equal("TOKEN_NOT_CONFIGURED", controller.GetStatus().SafeErrorCode);
        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public void PortConflictDegradesWithoutRandomFallback()
    {
        var factory = new FakeServerFactory(startError: "PORT_IN_USE");
        var controller = CreateController(new CapturingEvidenceWriter(), serverFactory: factory);

        Assert.False(controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
        Assert.Equal("PORT_IN_USE", controller.GetStatus().SafeErrorCode);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void SelfProbeFailureDegradesAndStopsListener()
    {
        var factory = new FakeServerFactory(selfProbeResult: false);
        var controller = CreateController(new CapturingEvidenceWriter(), serverFactory: factory);

        Assert.False(controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
        Assert.Equal("SELF_PROBE_FAILED", controller.GetStatus().SafeErrorCode);
        Assert.True(factory.Server.StopCalled);
    }

    [Fact]
    public void ShutdownTimeoutStopsWithoutThrowingAndRecordsSafeError()
    {
        var factory = new FakeServerFactory(stopResult: false);
        var controller = CreateController(new CapturingEvidenceWriter(), serverFactory: factory);
        Assert.True(controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        var exception = Record.Exception(() => Assert.False(controller.Terminate()));

        Assert.Null(exception);
        Assert.Equal(PluginLifecycleState.Stopped, controller.State);
    }

    [Fact]
    public void ListenerFaultMovesReadyLifecycleToDegraded()
    {
        var factory = new FakeServerFactory();
        var controller = CreateController(new CapturingEvidenceWriter(), serverFactory: factory);
        Assert.True(controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true)));

        factory.TriggerFault("ACCEPT_LOOP_FAILED");

        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
        Assert.Equal("ACCEPT_LOOP_FAILED", controller.GetStatus().SafeErrorCode);
        Assert.True(controller.Terminate());
    }

    private static PluginLifecycleController CreateController(
        IPluginLifecycleEvidenceWriter writer,
        PluginMetadata? metadata = null,
        ILoopbackBridgeServerFactory? serverFactory = null) =>
        new(
            metadata ?? PluginMetadata.CreateDefault(),
            writer,
            new SuccessfulTokenSource(),
            serverFactory ?? new FakeServerFactory());

    private sealed class CapturingEvidenceWriter : IPluginLifecycleEvidenceWriter
    {
        public List<PluginLifecycleEvidenceEntry> Entries { get; } = [];

        public bool TryAppend(PluginLifecycleEvidenceEntry entry)
        {
            Entries.Add(entry);
            return true;
        }
    }

    private sealed class ThrowingEvidenceWriter : IPluginLifecycleEvidenceWriter
    {
        public bool TryAppend(PluginLifecycleEvidenceEntry entry) =>
            throw new IOException("sensitive local path");
    }

    private sealed class RejectingEvidenceWriter : IPluginLifecycleEvidenceWriter
    {
        public bool TryAppend(PluginLifecycleEvidenceEntry entry) => false;
    }

    private sealed class FailAfterEvidenceWriter(int successfulWrites)
        : IPluginLifecycleEvidenceWriter
    {
        private int writeCount;

        public bool TryAppend(PluginLifecycleEvidenceEntry entry) =>
            Interlocked.Increment(ref writeCount) <= successfulWrites;
    }

    private sealed class SuccessfulTokenSource : IBridgeTokenSource
    {
        public BridgeTokenLoadResult Load()
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var token = Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            Assert.True(BridgeTokenCredential.TryCreate(token, out var credential));
            return new BridgeTokenLoadResult(credential, null);
        }
    }

    private sealed class FailedTokenSource(string errorCode) : IBridgeTokenSource
    {
        public BridgeTokenLoadResult Load() => BridgeTokenLoadResult.Failure(errorCode);
    }

    private sealed class FakeServerFactory(
        string? startError = null,
        bool selfProbeResult = true,
        bool stopResult = true) : ILoopbackBridgeServerFactory
    {
        private Action<string>? listenerFault;

        public FakeServer Server { get; } = new(startError, selfProbeResult, stopResult);

        public int CreateCount { get; private set; }

        public ILoopbackBridgeServer Create(
            LoopbackBridgeOptions options,
            BridgeTokenCredential credential,
            CadMax.Bridge.Core.BridgeInstanceResponseService responseService,
            DocumentContextDispatcher contextDispatcher,
            Action<string> listenerFault)
        {
            CreateCount++;
            this.listenerFault = listenerFault;
            return Server;
        }

        public void TriggerFault(string errorCode) => listenerFault?.Invoke(errorCode);
    }

    private sealed class FakeServer(
        string? startError,
        bool selfProbeResult,
        bool stopResult) : ILoopbackBridgeServer
    {
        public int ActiveConnectionCount => 0;

        public bool StopCalled { get; private set; }

        public string? TryStart() => startError;

        public Task<bool> RunAuthenticatedSelfProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(selfProbeResult);

        public Task<bool> StopAsync(TimeSpan deadline)
        {
            StopCalled = true;
            return Task.FromResult(stopResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
