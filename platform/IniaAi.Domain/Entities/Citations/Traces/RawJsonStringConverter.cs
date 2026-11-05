using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Entities.Citations.Traces;

/// <summary>
/// Writes a string that holds JSON as the JSON itself, so a downloaded workflow shows a citation
/// trace as readable nested objects rather than one escaped string, and reads either shape back
/// into the string.
/// </summary>
internal sealed class RawJsonStringConverter : JsonConverter<string>
{
    public override void WriteJson(JsonWriter writer, string? value, JsonSerializer serializer)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteValue(value);
            return;
        }

        JToken parsed;
        try
        {
            parsed = JsonText.Parse(value);
        }
        catch (JsonReaderException)
        {
            writer.WriteValue(value);
            return;
        }

        parsed.WriteTo(writer);
    }

    public override string? ReadJson(
        JsonReader reader,
        Type objectType,
        string? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        JToken token = JsonText.Load(reader);
        return token.Type switch
        {
            JTokenType.Null => null,
            JTokenType.String => (string?)token,
            _ => token.ToString(Formatting.None),
        };
    }
}
