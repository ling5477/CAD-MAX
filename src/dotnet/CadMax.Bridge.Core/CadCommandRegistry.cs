namespace CadMax.Bridge.Core;

/// <summary>
/// Immutable command registry. Unknown commands are never treated as successful.
/// </summary>
public sealed class CadCommandRegistry
{
    private readonly Dictionary<string, ICadCommandHandler> handlers;

    /// <summary>Create a registry and reject duplicate command ownership.</summary>
    public CadCommandRegistry(IEnumerable<ICadCommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        this.handlers = handlers.ToDictionary(
            handler => handler.CommandName,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Try to resolve a real handler for the requested command.</summary>
    public bool TryGet(string commandName, out ICadCommandHandler? handler) =>
        handlers.TryGetValue(commandName, out handler);

    /// <summary>List registered commands for truthful capability discovery.</summary>
    public IReadOnlyList<string> ListCommandNames() =>
        handlers.Keys.Order(StringComparer.Ordinal).ToArray();
}
