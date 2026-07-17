namespace CadMax.Bridge.Core;

/// <summary>
/// Successful handler payload before the dispatcher adds the common envelope.
/// </summary>
/// <param name="Message">Client-safe success message.</param>
/// <param name="Data">JSON-serializable result data.</param>
/// <param name="Warnings">Non-fatal warnings.</param>
public sealed record CadHandlerResult(
    string Message,
    object Data,
    IReadOnlyList<string>? Warnings = null);
