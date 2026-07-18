using Xunit;

namespace CadMax.AutoCAD.Plugin.Tests;

public sealed class PluginLifecycleTests
{
    [Fact]
    public void StateMachineAcceptsInitializeAndTerminatePath()
    {
        var stateMachine = new PluginLifecycleStateMachine();

        stateMachine.TransitionTo(PluginLifecycleState.Starting);
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
        var controller = new PluginLifecycleController(PluginMetadata.CreateDefault(), writer);
        var runtime = new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true);

        Assert.True(controller.Initialize(runtime, processId: 42));
        Assert.Equal(PluginLifecycleState.Ready, controller.State);
        Assert.True(controller.Terminate(processId: 42));
        Assert.Equal(PluginLifecycleState.Stopped, controller.State);

        Assert.Collection(
            writer.Entries,
            entry => Assert.Equal("PLUGIN_INITIALIZE_STARTED", entry.EventType),
            entry => Assert.Equal("PLUGIN_INITIALIZE_SUCCEEDED", entry.EventType),
            entry => Assert.Equal("PLUGIN_TERMINATE_STARTED", entry.EventType),
            entry => Assert.Equal("PLUGIN_TERMINATE_SUCCEEDED", entry.EventType));
    }

    [Fact]
    public void InvalidMetadataFailsInitialization()
    {
        var writer = new CapturingEvidenceWriter();
        var metadata = new PluginMetadata("unexpected", "0.1.0", "1.0");
        var controller = new PluginLifecycleController(metadata, writer);

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
        var controller = new PluginLifecycleController(
            PluginMetadata.CreateDefault(),
            new ThrowingEvidenceWriter());
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
        var controller = new PluginLifecycleController(
            PluginMetadata.CreateDefault(),
            new RejectingEvidenceWriter());

        var initialized = controller.Initialize(
            new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true));

        Assert.False(initialized);
        Assert.Equal(PluginLifecycleState.Degraded, controller.State);
    }

    [Fact]
    public void TerminateStopsSafelyWhenRequiredEvidenceFails()
    {
        var writer = new FailAfterEvidenceWriter(successfulWrites: 2);
        var controller = new PluginLifecycleController(PluginMetadata.CreateDefault(), writer);
        var runtime = new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true);

        Assert.True(controller.Initialize(runtime));

        Assert.False(controller.Terminate());
        Assert.Equal(PluginLifecycleState.Stopped, controller.State);
    }

    [Fact]
    public void InitializeAndTerminateAreIdempotentAtStableStates()
    {
        var writer = new CapturingEvidenceWriter();
        var controller = new PluginLifecycleController(PluginMetadata.CreateDefault(), writer);
        var runtime = new PluginRuntimeInfo(2025, "R25.0.58.0.0", "0.1.0", true);

        Assert.True(controller.Initialize(runtime));
        Assert.True(controller.Initialize(runtime));
        Assert.Equal(2, writer.Entries.Count);

        Assert.True(controller.Terminate());
        Assert.True(controller.Terminate());
        Assert.Equal(4, writer.Entries.Count);
    }

    [Fact]
    public void MissingAutoCADHostAttestationFailsClosedWithSafeStatus()
    {
        var controller = new PluginLifecycleController(
            PluginMetadata.CreateDefault(),
            new CapturingEvidenceWriter());

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
}
