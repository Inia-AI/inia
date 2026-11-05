using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace IniaAi.Infrastructure.Persistence.Serialization.Blocks;

public static class WorkflowOutputDataJsonDefaults
{
    public static readonly JsonSerializerOptions Options;

    static WorkflowOutputDataJsonDefaults()
    {
        // Create options that can handle both camelCase (from DB) and PascalCase (domain entities)
        Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { NonNullableMembers.KeepDefaultOnNull } },
            Converters = {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new WorkflowOutputDataSystemTextJsonConverter()
            }
        };
    }
}