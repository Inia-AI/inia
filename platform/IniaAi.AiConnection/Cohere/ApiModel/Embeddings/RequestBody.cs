using IniaAi.AiConnection.Cohere.ApiModel.Embeddings.Enums;
using IniaAi.Common.Serialization;
using IniaAi.Configuration.Enums.Cohere;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

internal class RequestBody : JsonSerializable<RequestBody>
{
    [JsonProperty("texts")]
    public string[] Texts { get; set; } = [];

    [JsonProperty("model", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(StringEnumConverter))]
    public AiModel? Model { get; set; } = AiModel.EmbedEnglishV30;

    [JsonProperty("input_type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public InputType InputType { get; set; }

    [JsonProperty("embedding_types", NullValueHandling = NullValueHandling.Ignore)]
    public string[]? EmbeddingTypes { get; set; }

    [JsonProperty("output_dimension", NullValueHandling = NullValueHandling.Ignore)]
    public int? OutputDimension { get; set; }

    [JsonProperty("truncate")]
    [JsonConverter(typeof(StringEnumConverter))]
    public TruncateType Truncate { get; set; } = TruncateType.End;

    public RequestBody() { }

    public RequestBody(List<string> texts)
    {
        Texts = [.. texts];
    }
}
