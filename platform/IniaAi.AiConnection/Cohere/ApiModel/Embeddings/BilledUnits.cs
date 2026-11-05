using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

internal class BilledUnits : JsonSerializable<BilledUnits>
{
    [JsonProperty("input_tokens")]
    public int InputTokens { get; set; }

    [JsonProperty("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonProperty("search_units")]
    public int SearchUnits { get; set; }

    [JsonProperty("classifications")]
    public int Classifications { get; set; }
}
