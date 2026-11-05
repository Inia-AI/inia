using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Contracts.Converters;

/// <summary>
/// JSON converter specifically for WorkflowVariable Options dictionary that preserves original key casing.
/// This prevents the global DictionaryKeyPolicy from converting WorkflowVariable option keys to camelCase.
/// </summary>
public class WorkflowVariableOptionsConverter : JsonConverter<Dictionary<string, string>>
{
    /// <summary>
    /// Reads a JSON object and converts it to a Dictionary&lt;string, string&gt; preserving key casing.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">JSON serialization options.</param>
    /// <returns>Dictionary with original key casing preserved.</returns>
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dictionary = new Dictionary<string, string>();

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return dictionary;
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            string key = reader.GetString()!;
            _ = reader.Read();
            string value = reader.GetString()!;

            dictionary[key] = value;
        }

        throw new JsonException("Expected EndObject token");
    }

    /// <summary>
    /// Writes a Dictionary&lt;string, string&gt; to JSON preserving original key casing.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The dictionary to write.</param>
    /// <param name="options">JSON serialization options.</param>
    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (KeyValuePair<string, string> kvp in value)
        {
            // Write keys with their original casing, bypassing the global DictionaryKeyPolicy
            writer.WriteString(kvp.Key, kvp.Value);
        }

        writer.WriteEndObject();
    }
}
