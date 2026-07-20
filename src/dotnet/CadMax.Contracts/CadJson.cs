using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadMax.Contracts;

/// <summary>
/// Canonical camelCase JSON configuration for tests and non-ASP.NET callers.
/// </summary>
public static class CadJson
{
    /// <summary>Shared, immutable serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        return options;
    }
}
