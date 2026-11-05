using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

/// <summary>
internal class Meta : JsonSerializable<Meta>
{
    [JsonProperty("api_version")]
    public ApiVersion ApiVersion { get; set; } = new();

    [JsonProperty("billed_units")]
    public BilledUnits BilledUnits { get; set; } = new();

    [JsonProperty("tokens")]
    public Tokens Tokens { get; set; } = new();

    [JsonProperty("warnings")]
    public string[] Warnings { get; set; } = [];
}
