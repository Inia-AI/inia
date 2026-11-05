using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

internal static class BlockPropertiesTestCodec
{
    // Mirrors production: the converter rides [JsonConverter] on Block.Properties, so
    // ReadJson runs against a serializer that does NOT itself carry the converter.
    // Registering it serializer-wide instead recurses infinitely on the inner ToObject.
    public static BlockProperties? DeserializeViaConverter(string json)
    {
        var converter = new BlockPropertiesNewtonsoftConverter();
        using JsonReader reader = JObject.Parse(json).CreateReader();
        return converter.ReadJson(reader, typeof(BlockProperties), null, false, JsonSerializer.CreateDefault());
    }
}
