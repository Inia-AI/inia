using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Common.Serialization;

/// <summary>
/// A <see cref="StringEnumConverter"/> that deserializes an unrecognized string value of
/// a nullable enum as null instead of throwing, so a value the API added after this build
/// cannot fail parsing of an entire response. Only string tokens are tolerated; any other
/// token type still throws, so a genuine change to the field's JSON type is not hidden.
/// </summary>
public class TolerantStringEnumConverter : StringEnumConverter
{
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        JsonToken valueTokenType = reader.TokenType;

        try
        {
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
        catch (JsonSerializationException) when (
            valueTokenType == JsonToken.String && Nullable.GetUnderlyingType(objectType) is not null)
        {
            return null;
        }
    }
}
