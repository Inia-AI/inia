using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.Common.Enums;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// Represents a source of data. The source can be of different types, but
/// currently only <see cref="SourceType.Base64"/> is supported.
/// </summary>
internal class Source : JsonSerializable<Source>
{
    /// <summary>
    /// The type of the source.
    /// </summary>
    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public SourceType? Type { get; set; } = SourceType.Base64;

    /// <summary>
    /// The media type of the source. Supported values are
    /// <list type="bullet">
    /// <item>
    /// <description>image/jpeg</description>
    /// </item>
    /// <item>
    /// <description>image/png</description>
    /// </item>
    /// <item>
    /// <description>image/gif</description>
    /// </item>
    /// <item>
    /// <description>image/webp</description>
    /// </item>
    /// </list>
    /// </summary>
    [JsonProperty("media_type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public MediaType? MediaType { get; set; }

    /// <summary>
    /// The data of the source. This is a base64 encoded string.
    /// </summary>
    [JsonProperty("data")]
    public string? Data { get; set; }

    /// <summary>
    /// The binary data of the source. Is converted from the <see cref="Data"/>
    /// property.
    /// </summary>
    [JsonIgnore]
    public BinaryData? BinaryData => Data is not null
        ? new BinaryData(Convert.FromBase64String(Data))
        : null;

    public Source()
    {
    }

    public Source(string data, MediaType mediaType)
    {
        MediaType = mediaType;
        Data = data;

        if (mediaType is IniaAi.Common.Enums.MediaType.TextPlain)
        {
            Type = SourceType.Text;
        }
    }

    public Source(BinaryData image, MediaType mediaType)
        : this(Convert.ToBase64String(image.ToArray()), mediaType)
    {
    }
}
