using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal class Citation : JsonSerializable<Citation>
{
    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public CitationType? Type { get; set; }

    [JsonProperty("cited_text", NullValueHandling = NullValueHandling.Ignore)]
    public string? CitedText { get; set; }

    /// <summary>
    /// The index of the document from which the text is cited. This is 0-indexed.
    /// </summary>
    [JsonProperty("document_index", NullValueHandling = NullValueHandling.Ignore)]
    public int? DocumentIndex { get; set; }

    [JsonProperty("document_title", NullValueHandling = NullValueHandling.Ignore)]
    public string? DocumentTitle { get; set; }

    /// <summary>
    /// The starting character index of the cited text. This is 0-indexed.
    /// </summary>
    [JsonProperty("start_char_index", NullValueHandling = NullValueHandling.Ignore)]
    public int? StartCharIndex { get; set; }

    /// <summary>
    /// The ending character index of the cited text. This is exclusive and 0-indexed.
    /// </summary>
    [JsonProperty("end_char_index", NullValueHandling = NullValueHandling.Ignore)]
    public int? EndCharIndex { get; set; }

    /// <summary>
    /// The starting page number of the cited text. This is 1-indexed.
    /// </summary>
    [JsonProperty("start_page_number", NullValueHandling = NullValueHandling.Ignore)]
    public int? StartPageNumber { get; set; }

    /// <summary>
    /// The ending page number of the cited text. This is exclusive and 1-indexed.
    /// </summary>
    [JsonProperty("end_page_number", NullValueHandling = NullValueHandling.Ignore)]
    public int? EndPageNumber { get; set; }

    /// <summary>
    /// The starting content block index of the cited text. This is 0-indexed.
    /// </summary>
    [JsonProperty("start_block_index", NullValueHandling = NullValueHandling.Ignore)]
    public int? StartBlockIndex { get; set; }

    /// <summary>
    /// The ending content block index of the cited text. This is exclusive and 0-indexed.
    /// </summary>
    [JsonProperty("end_block_index", NullValueHandling = NullValueHandling.Ignore)]
    public int? EndBlockIndex { get; set; }

    internal static List<SourceReference>? ConvertCitationsToSourceReferences(List<Citation>? citations)
    {
        if (citations is null || citations.Count == 0)
        {
            return null;
        }

        List<SourceReference> sourceReferences = [];

        foreach (Citation citation in citations)
        {
            if (citation.DocumentIndex is null)
            {
                continue;
            }

            SourceReference sourceReference = new()
            {
                Title = citation.DocumentTitle,
                CitedText = citation.CitedText,
                Index = citation.DocumentIndex.Value,
            };

            sourceReferences.Add(sourceReference);
        }

        return sourceReferences.Count == 0
            ? null
            : sourceReferences;
    }
}
