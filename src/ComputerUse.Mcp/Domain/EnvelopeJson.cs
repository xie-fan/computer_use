using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComputerUse.Mcp.Domain;

internal static class EnvelopeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public static JsonElement? Details(object? value)
    {
        if (value is null)
            return null;
        if (value is JsonElement element)
            return element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : element;
        return JsonSerializer.SerializeToElement(value, Options);
    }
}
