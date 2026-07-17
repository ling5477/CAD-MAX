using System.Text.Json;
using CadMax.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace CadMax.Bridge.Core.Tests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task InvalidRequestReturnsInvalidArgument()
    {
        var dispatcher = CreateDispatcher();
        var request = CreateRequest(command: "", requestId: "not-a-uuid");

        var result = await dispatcher.DispatchAsync(request);

        Assert.False(result.Success);
        Assert.Equal(CadStatus.InvalidArgument, result.Status);
    }

    [Fact]
    public async Task UnknownCommandReturnsNotImplemented()
    {
        var dispatcher = CreateDispatcher();

        var result = await dispatcher.DispatchAsync(CreateRequest("drawing.status"));

        Assert.False(result.Success);
        Assert.Equal(CadStatus.NotImplemented, result.Status);
    }

    [Fact]
    public async Task HandlerSuccessReturnsOk()
    {
        var dispatcher = CreateDispatcher(new SuccessHandler());

        var result = await dispatcher.DispatchAsync(CreateRequest(SuccessHandler.Name));

        Assert.True(result.Success);
        Assert.Equal(CadStatus.Ok, result.Status);
        Assert.Equal("Handled", result.Message);
    }

    [Fact]
    public async Task HandlerExceptionIsSanitized()
    {
        var dispatcher = CreateDispatcher(new ThrowingHandler());

        var result = await dispatcher.DispatchAsync(CreateRequest(ThrowingHandler.Name));

        Assert.Equal(CadStatus.InternalError, result.Status);
        Assert.DoesNotContain("sensitive", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallerCancellationReturnsTimeoutStatus()
    {
        var dispatcher = CreateDispatcher(new WaitingHandler());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await dispatcher.DispatchAsync(
            CreateRequest(WaitingHandler.Name, timeoutMs: 5_000),
            cancellation.Token);

        Assert.Equal(CadStatus.Timeout, result.Status);
        Assert.Equal("Command was cancelled", result.Message);
    }

    [Fact]
    public async Task CommandTimeoutReturnsTimeoutStatus()
    {
        var dispatcher = CreateDispatcher(new WaitingHandler());

        var result = await dispatcher.DispatchAsync(
            CreateRequest(WaitingHandler.Name, timeoutMs: 20));

        Assert.Equal(CadStatus.Timeout, result.Status);
        Assert.Equal("Command timed out", result.Message);
    }

    private static CadCommandDispatcher CreateDispatcher(
        params ICadCommandHandler[] handlers) =>
        new(
            new CadCommandRegistry(handlers),
            new CadCommandValidator(),
            NullLogger<CadCommandDispatcher>.Instance);

    private static CadCommandRequest CreateRequest(
        string command,
        string? requestId = null,
        int timeoutMs = 30_000) =>
        new(
            CadProtocol.SchemaVersion,
            requestId ?? Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            command,
            JsonSerializer.SerializeToElement(new { }),
            timeoutMs);

    private sealed class SuccessHandler : ICadCommandHandler
    {
        public const string Name = "test.success";

        public string CommandName => Name;

        public Task<CadHandlerResult> ExecuteAsync(
            CadCommandRequest request,
            CadExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CadHandlerResult("Handled", new { value = 1 }));
    }

    private sealed class ThrowingHandler : ICadCommandHandler
    {
        public const string Name = "test.throw";

        public string CommandName => Name;

        public Task<CadHandlerResult> ExecuteAsync(
            CadCommandRequest request,
            CadExecutionContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive C:\\local\\path");
    }

    private sealed class WaitingHandler : ICadCommandHandler
    {
        public const string Name = "test.wait";

        public string CommandName => Name;

        public async Task<CadHandlerResult> ExecuteAsync(
            CadCommandRequest request,
            CadExecutionContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new CadHandlerResult("unreachable", new { });
        }
    }
}
