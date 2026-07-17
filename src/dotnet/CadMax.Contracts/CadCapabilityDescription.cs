namespace CadMax.Contracts;

/// <summary>
/// Truthful capability descriptor returned by the bridge host.
/// </summary>
/// <param name="Name">Stable capability name.</param>
/// <param name="Implemented">Whether a real implementation is present.</param>
/// <param name="ReadOnly">Whether the capability is non-mutating.</param>
public sealed record CadCapabilityDescription(string Name, bool Implemented, bool ReadOnly);
