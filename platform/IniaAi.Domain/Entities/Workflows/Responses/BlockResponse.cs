using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Reflection;
using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Responses;

public class BlockResponse : JsonSerializable<BlockResponse>
{
    public const string JSON_RESPONSE = "Json";
    public const string HTML_RESPONSE = "Html";
    public const string MARKDOWN_RESPONSE = "Markdown";

    [Key]
    [JsonProperty(nameof(Id))]
    [CloneIgnore]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty(nameof(BlockId))]
    [CloneIgnore]
    public string? BlockId { get; internal set; } = string.Empty;

    [ForeignKey(nameof(BlockId))]
    [JsonIgnore]
    [CloneIgnore]
    public Block? Block { get; set; }

    [JsonProperty(nameof(RawResponse))]
    public string? RawResponse
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    [JsonProperty(nameof(JsonResponse), NullValueHandling = NullValueHandling.Ignore)]
    public string? JsonResponse
    {
        get;
        set => field = value.RemoveNullCharacters();
    }

    [JsonProperty(nameof(HtmlResponse), NullValueHandling = NullValueHandling.Ignore)]
    public string? HtmlResponse { get; set; }

    [JsonProperty(nameof(MarkdownResponse), NullValueHandling = NullValueHandling.Ignore)]
    public string? MarkdownResponse { get; set; }

    [JsonProperty(nameof(DebugInfo), NullValueHandling = NullValueHandling.Ignore)]
    public string? DebugInfo { get; set; }

    // TODO: Future — cache extracted regression facts on the block response to avoid re-extraction
    [JsonProperty(nameof(RegressionFacts), NullValueHandling = NullValueHandling.Ignore)]
    public string? RegressionFacts { get; set; }

    [JsonIgnore]
    [CloneIgnore]
    public byte[]? BinaryResponse { get; set; }

    [JsonProperty(nameof(MediaType))]
    [JsonConverter(typeof(StringEnumConverter))]
    public MediaType MediaType { get; set; } = MediaType.Unknown;

    public bool ShouldSerializeMediaType()
    {
        return MediaType is not MediaType.Unknown;
    }

    [JsonConstructor]
    private BlockResponse()
    {
    }

    public BlockResponse(Block block)
    {
        Block = block;
        BlockId = block.Id;
        block.BlockResponse = this;
        block.BlockResponseId = Id;
    }

    public BlockResponse CloneToBlock(Block block)
    {
        BlockResponse clone = new(block);
        CloneHelper.CopyScalars(this, clone);
        clone.BinaryResponse = BinaryResponse;
        return clone;
    }

    public string? GetResponseProperty(string propertyTag)
    {
        return string.IsNullOrWhiteSpace(RawResponse)
            ? null
            : Utils.GetStringFromTag(
                RawResponse,
                propertyTag,
                processMultipleTags: true);
    }

    public void SetResponseProperty(
        string propertyTag,
        string value,
        bool shouldAppend = false)
    {
        RawResponse = Utils.SetStringToTag(
            RawResponse,
            propertyTag,
            value,
            shouldAppend);
    }
}
