using CadMax.Contracts;

namespace CadMax.Bridge.Core;

/// <summary>
/// Validates contract version, correlation IDs, command names, and timeout bounds.
/// </summary>
public sealed class CadCommandValidator
{
    /// <summary>Return all client-safe validation errors without throwing.</summary>
    public IReadOnlyList<string> Validate(CadCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        if (!string.Equals(
                request.SchemaVersion,
                CadProtocol.SchemaVersion,
                StringComparison.Ordinal))
        {
            errors.Add("schemaVersion must be 1.0");
        }

        if (!Guid.TryParse(request.RequestId, out _))
        {
            errors.Add("requestId must be a UUID");
        }

        if (!Guid.TryParse(request.TraceId, out _))
        {
            errors.Add("traceId must be a UUID");
        }

        if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Length > 128)
        {
            errors.Add("command must contain 1 to 128 characters");
        }

        if (request.Parameters.ValueKind is not JsonValueKind.Object)
        {
            errors.Add("parameters must be a JSON object");
        }

        if (request.TimeoutMs is < 1 or > 120_000)
        {
            errors.Add("timeoutMs must be between 1 and 120000");
        }

        return errors;
    }
}
