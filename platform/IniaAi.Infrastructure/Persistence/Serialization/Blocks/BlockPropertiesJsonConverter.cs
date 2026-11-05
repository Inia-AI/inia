using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Infrastructure.Persistence.Serialization.Blocks;

public class BlockPropertiesJsonConverter : JsonConverter<BlockProperties>
{
    private const string TYPE = "type";

    public override BlockProperties Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        JsonElement settings = jsonDoc.RootElement;

        if (settings.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("Invalid JSON format for BlockProperties");
        }

        // Any case: the settings of an unavailable block may have been read from a workflow file,
        // whose serializer writes "Type".
        JsonElement typeProp = settings.TryGetProperty(TYPE, out JsonElement exact)
            ? exact
            : settings.EnumerateObject()
                .FirstOrDefault(static property => string.Equals(property.Name, TYPE, StringComparison.OrdinalIgnoreCase))
                .Value;
        if (typeProp.ValueKind is JsonValueKind.Undefined)
        {
            throw new JsonException("Missing 'type' property in BlockProperties JSON");
        }

        return BlockTypeCatalog.PropertiesTypeOf(typeProp.ToString(), out BlockTypes blockType) is { } propertiesType
            ? settings.Deserialize(propertiesType, options) as BlockProperties
                ?? throw new JsonException("Failed to deserialize BlockProperties")
            : new UnavailableBlockProperties(blockType, settings.GetRawText());
    }

    public override void Write(
        Utf8JsonWriter writer,
        BlockProperties value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
