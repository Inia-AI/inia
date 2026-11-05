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
internal class ContentRequest : JsonSerializable<ContentRequest>
{
    /// <summary>
    /// The type of content.
    /// </summary>
    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public ContentType Type { get; set; } = ContentType.Text;

    /// <summary>
    /// The source of the content.
    /// </summary>
    [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
    public Source? Source { get; set; }

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

    [JsonProperty("cache_control", NullValueHandling = NullValueHandling.Ignore)]
    public CacheControl? CacheControl { get; set; }

    [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
    public string? Title { get; set; }

    [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
    public string? Context { get; set; }

    [JsonProperty("citations", NullValueHandling = NullValueHandling.Ignore)]
    public CitationControl? CitationsControl { get; set; }

    public ContentRequest()
    {
    }

    public ContentRequest(RequestCommonContent commonContent, bool shouldAddCacheControl = false)
    {
        switch (commonContent.Type)
        {
            case CommonContentType.Text:
                ArgumentNullException.ThrowIfNull(commonContent.TextContent);

                SetupTextContent(commonContent);

                break;

            case CommonContentType.Image:
                ArgumentNullException.ThrowIfNull(commonContent.BinaryContent);

                SetupImageContent(commonContent);

                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported content type: {commonContent.Type}");
        }

        CacheControl = shouldAddCacheControl
            ? new()
            : null;
    }

    private void SetupImageContent(RequestCommonContent commonContent)
    {
        if (commonContent.IsInputContext)
        {
            SetupAsCitedImageContent(commonContent);

            return;
        }

        Type = ContentType.Image;
        Source = new(
            commonContent.BinaryContent!.BinaryData,
            commonContent.BinaryContent!.MediaType);
    }

    private void SetupAsCitedImageContent(RequestCommonContent commonContent)
    {
        ArgumentNullException.ThrowIfNull(commonContent.BinaryContent);

        Type = ContentType.Document;
        Source = new(
            commonContent.BinaryContent.BinaryData,
            commonContent.BinaryContent.MediaType);

        Title = commonContent.Title;
        Context = commonContent.Context;
        CitationsControl = new();
    }

    private void SetupTextContent(RequestCommonContent commonContent)
    {
        if (commonContent.IsInputContext
            || commonContent.IsBlockReference)
        {
            SetupAsCitedTextContent(commonContent);

            return;
        }

        Type = ContentType.Text;
        Text = commonContent.TextContent;
    }

    private void SetupAsCitedTextContent(RequestCommonContent commonContent)
    {
        ArgumentNullException.ThrowIfNull(commonContent.TextContent);

        Type = ContentType.Document;
        Source = new(
            commonContent.TextContent,
            MediaType.TextPlain);

        Title = string.IsNullOrWhiteSpace(commonContent.Title)
            ? null
            : commonContent.Title;
        Context = string.IsNullOrWhiteSpace(commonContent.Context)
            ? null
            : commonContent.Context;
        CitationsControl = new();
    }

    /// <summary>
    /// Creates a list of cached content from a content sequence.
    /// IMPORTANT: Only the last content in the sequence should be marked as cached.
    /// </summary>
    /// <param name="contentSequence"></param>
    /// <returns></returns>
    public static List<ContentRequest> CreateCachedContent(RequestContentSequence contentSequence)
    {
        List<ContentRequest> contentList = [];

        for (int i = 0; i < contentSequence.Count(); i++)
        {
            RequestCommonContent commonContent = contentSequence[i];

            bool isLastContent = i + 1 == contentSequence.Count();

            contentList.Add(new(
                commonContent,
                shouldAddCacheControl: isLastContent));
        }

        return contentList;
    }
}
