using Newtonsoft.Json;

namespace IniaAi.Common.Serialization;

public abstract class JsonSerializable<T> where T : class
{
    private static readonly JsonSerializerSettings _defaultSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    /// <summary>
    /// Converts the JSON representation of the object to the object.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="json">The JSON representation of the object.</param>
    /// <returns>The exceptions are caught and null is returned if the JSON is
    /// invalid. Otherwise, the object is returned.</returns>
    public static T? FromJson(string? json)
    {
        return FromJson(json, _defaultSettings);
    }

    public static T? FromJson(string? json, JsonSerializerSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            json = SanitizeJson(json);

            return JsonConvert.DeserializeObject<T>(json, settings ?? _defaultSettings);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the JSON representation of the object.
    /// </summary>
    /// <param name="pretty"></param>
    /// <returns></returns>
    public virtual string ToJson(bool pretty)
    {
        return ToJson(pretty, _defaultSettings);
    }

    /// <summary>
    /// Returns the JSON representation of the object.
    /// </summary>
    /// <param name="pretty"></param>
    /// <returns></returns>
    public virtual string ToJson(bool pretty, JsonSerializerSettings settings)
    {
        return JsonConvert.SerializeObject(this, pretty ? Formatting.Indented : Formatting.None, settings: settings ?? _defaultSettings)
        .Replace("\r\n", "\n")
        .Replace("\r", "\n");
    }

    /// <summary>
    /// Returns the JSON representation of the object.
    /// NOTE: This method is intentionally NOT overriding ToString() to avoid performance issues
    /// when entities with navigation properties are rendered in UI components or logged.
    /// Use ToJson() explicitly if you need JSON serialization.
    /// </summary>
    /// <returns></returns>
    public string ToJsonString()
    {
        return ToJson(false);
    }

    // Intentionally NOT overriding ToString() - see ToJsonString() if you need JSON representation

    /// <summary>
    /// Remove all text before and after the JSON object
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    protected static string SanitizeJson(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');

        return start < 0 || end < 0
            ? text
            : text[start..(end + 1)];
    }
}
