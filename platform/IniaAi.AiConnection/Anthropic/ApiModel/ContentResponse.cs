using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common.Enums;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

/// <summary>
/// Represents the content of a message.
/// </summary>
internal class ContentResponse : JsonSerializable<ContentResponse>
{
    /// <summary>
    /// The type of content.
    /// </summary>
    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public ContentType Type { get; set; } = ContentType.Text;

    /// <summary>
    /// The text content.
    /// </summary>
    [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; set; }

    /// <summary>
    /// The thinking content.
    /// </summary>
    [JsonProperty("thinking", NullValueHandling = NullValueHandling.Ignore)]
    public string? Thinking { get; set; }

    [JsonProperty("signature", NullValueHandling = NullValueHandling.Ignore)]
    public string? ThinkingSignature { get; set; }

    [JsonProperty("citations", NullValueHandling = NullValueHandling.Ignore)]
    public List<Citation>? Citations { get; set; }

    public ContentResponse()
    {
    }

    public ResponseCommonContent? ToResponseCommonContent()
    {
        return Type switch
        {
            ContentType.Text or ContentType.TextDelta => new ResponseCommonContent(CommonContentType.Text)
            {
                TextContent = Text,
                SourceReferences = Citation.ConvertCitationsToSourceReferences(Citations),
            },
            ContentType.Image => new ResponseCommonContent(CommonContentType.Image)
            {
                BinaryContent = Text is null
                    ? null
                    : new(Convert.FromBase64String(Text), MediaType.ImagePng),
                SourceReferences = Citation.ConvertCitationsToSourceReferences(Citations),
            },
            _ => null,
        };
    }

    public ResponseCommonContent? ToResponseCommonContentIncludeThinking()
    {
        return Type switch
        {
            ContentType.Text or ContentType.TextDelta => new ResponseCommonContent(CommonContentType.Text)
            {
                TextContent = Text,
                SourceReferences = Citation.ConvertCitationsToSourceReferences(Citations),
            },
            ContentType.Thinking or ContentType.ThinkingDelta => new ResponseCommonContent(CommonContentType.Text)
            {
                TextContent = $"Thinking response: {Thinking}",
                SourceReferences = Citation.ConvertCitationsToSourceReferences(Citations),
            },
            ContentType.Image => new ResponseCommonContent(CommonContentType.Image)
            {
                BinaryContent = Text is null
                    ? null
                    : new(Convert.FromBase64String(Text), MediaType.ImagePng),
                SourceReferences = Citation.ConvertCitationsToSourceReferences(Citations),
            },
            _ => null,
        };
    }
}
