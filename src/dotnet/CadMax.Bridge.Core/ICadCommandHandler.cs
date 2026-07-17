using CadMax.Contracts;

namespace CadMax.Bridge.Core;

/// <summary>
/// Implements one explicitly registered bridge command.
/// </summary>
public interface ICadCommandHandler
{
    /// <summary>Stable command name owned by this handler.</summary>
    string CommandName { get; }

    /// <summary>
    /// Execute within an abstract CAD context. Implementations must honor cancellation,
    /// avoid long-running external calls inside database transactions, and return only
    /// JSON-serializable data.
    /// </summary>
    Task<CadHandlerResult> ExecuteAsync(
        CadCommandRequest request,
        CadExecutionContext context,
        CancellationToken cancellationToken);
}
