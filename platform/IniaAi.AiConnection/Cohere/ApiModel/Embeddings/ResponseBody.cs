using IniaAi.Common.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings;

internal class ResponseBody : JsonSerializable<ResponseBody>
{
    [JsonProperty("response_type")]
    public string ResponseType { get; set; } = "";

    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("embeddings")]
    [JsonConverter(typeof(EmbeddingsConverter))]
    public double[][] Embeddings { get; set; } = [];

    [JsonProperty("texts")]
    public string[] Texts { get; set; } = [];

    [JsonProperty("meta")]
    public Meta Meta { get; set; } = new();
}

/// <summary>
/// Handles both v3 format (double[][]) and v4 format ({"float": double[][]})
/// </summary>
internal class EmbeddingsConverter : JsonConverter<double[][]>
{
    public override double[][]? ReadJson(JsonReader reader, Type objectType, double[][]? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var token = JToken.Load(reader);

        // V4 format: {"float": [[...]]}
        if (token.Type is JTokenType.Object)
        {
            var obj = (JObject)token;
            // Try "float" first (most common), then other types
            JToken? floatEmbeddings = obj["float"];
            if (floatEmbeddings is not null)
            {
                return floatEmbeddings.ToObject<double[][]>() ?? [];
            }
            // Fallback to first property if it's an array
            foreach (JProperty prop in obj.Properties())
            {
                if (prop.Value.Type is JTokenType.Array)
                {
                    return prop.Value.ToObject<double[][]>() ?? [];
                }
            }

            return [];
        }

        // V3 format: [[...]]
        return token.Type is JTokenType.Array ? token.ToObject<double[][]>() ?? [] : [];
    }

    public override void WriteJson(JsonWriter writer, double[][]? value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, value);
    }
}
