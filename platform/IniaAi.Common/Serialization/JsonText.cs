using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Common.Serialization;

/// <summary>
/// Parses JSON into tokens that keep every string as it was written. Newtonsoft's default turns a
/// string that looks like a date and time into a date, so text written back from the token comes
/// out reformatted, or in the process culture's format when it is read into a string.
/// </summary>
public static class JsonText
{
    public static JToken Parse(string json)
    {
        using JsonTextReader reader = CreateReader(json);
        JToken token = JToken.Load(reader);

        // Rejects content after the value, as JToken.Parse does.
        while (reader.Read())
        {
        }

        return token;
    }

    public static JObject ParseObject(string json)
    {
        using JsonTextReader reader = CreateReader(json);
        return JObject.Load(reader);
    }

    /// <summary>Loads the value at a converter's reader with dates left as text, whatever its caller set.</summary>
    public static JToken Load(JsonReader reader)
    {
        DateParseHandling callerHandling = reader.DateParseHandling;
        reader.DateParseHandling = DateParseHandling.None;
        try
        {
            return JToken.Load(reader);
        }
        finally
        {
            reader.DateParseHandling = callerHandling;
        }
    }

    /// <inheritdoc cref="Load(JsonReader)"/>
    public static JObject LoadObject(JsonReader reader)
    {
        return Load(reader) as JObject
            ?? throw new JsonReaderException($"Expected a JSON object at {reader.Path}.");
    }

    public static JsonTextReader CreateReader(string json)
    {
        return new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
    }
}
