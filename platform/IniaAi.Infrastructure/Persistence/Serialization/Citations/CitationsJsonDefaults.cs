using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace IniaAi.Infrastructure.Persistence.Serialization.Citations;

public static class CitationsJsonDefaults
{
    public static readonly JsonSerializerOptions Options;

    static CitationsJsonDefaults()
    {
        Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NonNullableMembers.KeepDefaultOnNull } },
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new BaseSourceSystemTextJsonConverter()
            }
        };
    }
}
