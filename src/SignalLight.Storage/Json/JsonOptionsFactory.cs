using System.Text.Json;
using System.Text.Json.Serialization;

namespace SignalLight.Storage.Json;

internal static class JsonOptionsFactory
{
    public static JsonSerializerOptions Create()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
    }
}
