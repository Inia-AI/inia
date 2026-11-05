using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Infrastructure.Persistence.Serialization;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,

        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static readonly JsonSerializerOptions OptionsWithOriginalKeys = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null, // Preserve original dictionary key casing
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
