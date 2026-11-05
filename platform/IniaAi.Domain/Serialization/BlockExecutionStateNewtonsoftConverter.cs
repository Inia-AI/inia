using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Serialization;

/// <summary>
/// Custom JSON converter for deserializing <see cref="BlockExecutionState"/> polymorphically
/// based on the "type" discriminator property using Newtonsoft.Json.
/// </summary>
/// <remarks>
/// For backward compatibility with old execution state JSON that lacks a "type" discriminator,
/// this converter returns null, allowing the execution state to be rebuilt on next execution.
/// </remarks>
public class BlockExecutionStateNewtonsoftConverter : JsonConverter<BlockExecutionState>
{
    /// <summary>
    /// Reads and deserializes the <see cref="BlockExecutionState"/> object from the JSON input.
    /// </summary>
    /// <param name="reader">The JSON reader positioned at the start of the object or null.</param>
    /// <param name="objectType">The target type being deserialized (always <see cref="BlockExecutionState"/>).</param>
    /// <param name="existingValue">The existing value (unused).</param>
    /// <param name="hasExistingValue">Whether there is an existing value (unused).</param>
    /// <param name="serializer">The JSON serializer instance to use for inner deserialization.</param>
    /// <returns>The deserialized <see cref="BlockExecutionState"/> instance, or null if the JSON was null, missing type, or unsupported type.</returns>
    public override BlockExecutionState? ReadJson(
        JsonReader reader,
        Type objectType,
        BlockExecutionState? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType is JsonToken.Null)
        {
            return null;
        }

        JObject jo = JsonText.LoadObject(reader);

        // Case-insensitive lookup for the discriminator ("type" or "Type", etc.)
        // If missing (old format), return null to trigger rebuild on next execution
        if (!jo.TryGetValue("type", StringComparison.OrdinalIgnoreCase, out JToken? typeToken))
        {
            // Backward compatibility: Old ExecutionState JSON without "type" discriminator
            // Return null and let the execution state be rebuilt from Block properties
            return null;
        }

        string typeStr = typeToken.ToString();

        // Parse the block type enum
        if (!Enum.TryParse(typeStr, ignoreCase: true, out BlockTypes _))
        {
            // Unknown or invalid type - return null to trigger rebuild
            return null;
        }

        // Deserialize to BlockExecutionState (no longer using subclasses)
        // All blocks use the same BlockExecutionState with typed Properties property
        return jo.ToObject<BlockExecutionState>(serializer);
    }

    /// <summary>
    /// Serializes the <see cref="BlockExecutionState"/> instance to JSON.
    /// </summary>
    /// <param name="writer">The JSON writer to write to.</param>
    /// <param name="value">The <see cref="BlockExecutionState"/> instance to serialize.</param>
    /// <param name="serializer">The serializer to use for writing the object.</param>
    public override void WriteJson(JsonWriter writer, BlockExecutionState? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        // Serialize the concrete type with all its properties
        serializer.Serialize(writer, value, value.GetType());
    }
}
