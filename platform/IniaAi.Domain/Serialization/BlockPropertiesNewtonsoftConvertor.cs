using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Serialization;

/// <summary>
/// Custom JSON converter for deserializing <see cref="BlockProperties"/> polymorphically
/// based on the "type" discriminator property using Newtonsoft.Json.
/// </summary>
public class BlockPropertiesNewtonsoftConverter : JsonConverter<BlockProperties>
{
    /// <summary>
    /// Reads and deserializes the <see cref="BlockProperties"/> object from the JSON input.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the start of the object or null.</param>
    /// <param name="objectType">The target type being deserialized (always <see cref="BlockProperties"/>).</param>
    /// <param name="existingValue">The existing value (unused).</param>
    /// <param name="hasExistingValue">Whether there is an existing value (unused).</param>
    /// <param name="serializer">The JSON serializer instance to use for inner deserialization.</param>
    /// <returns>The deserialized <see cref="BlockProperties"/> instance, or null if the JSON was null.</returns>
    public override BlockProperties? ReadJson(
        JsonReader reader,
        Type objectType,
        BlockProperties? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType is JsonToken.Null)
        {
            return null;
        }

        JObject jo = JsonText.LoadObject(reader);

        // Case-insensitive lookup for the discriminator ("type" or "Type", etc.)
        if (!jo.TryGetValue("type", StringComparison.OrdinalIgnoreCase, out JToken? typeToken))
        {
            throw new JsonException("Missing 'type' discriminator (case-insensitive).");
        }

        return BlockTypeCatalog.PropertiesTypeOf(typeToken.ToString(), out BlockTypes blockType) is { } propertiesType
            ? (BlockProperties?)jo.ToObject(propertiesType, serializer)
            : new UnavailableBlockProperties(blockType, jo.ToString(Formatting.None));
    }

    /// <summary>
    /// Serializes the <see cref="BlockProperties"/> instance to JSON.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="value">The <see cref="BlockProperties"/> instance to serialize.</param>
    /// <param name="serializer">The serializer to use for writing the object.</param>
    public override void WriteJson(JsonWriter writer, BlockProperties? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        // A settings class with its own converter is written by that converter here: handed back to
        // the serializer, it would meet the same object on the serializer's stack, and a serializer
        // that ignores reference loops, as the workflow canonicalizer's does, would write nothing.
        if (serializer.ContractResolver.ResolveContract(value.GetType()).Converter is { CanWrite: true } own)
        {
            own.WriteJson(writer, value, serializer);
            return;
        }

        serializer.Serialize(writer, value, value.GetType());
    }
}
