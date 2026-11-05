using IniaAi.Domain.Entities.WorkflowExecutions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Infrastructure.Persistence.Serialization.Blocks;

/// <summary>
/// Custom JSON converter for polymorphic WorkflowOutputData serialization.
/// Handles both TableOutput and ChartOutput types using the OutputType discriminator.
/// </summary>
public class WorkflowOutputDataJsonConverter : JsonConverter<WorkflowOutputData>
{
    public override bool CanWrite => true;
    public override bool CanRead => true;

    public override WorkflowOutputData? ReadJson(JsonReader reader, Type objectType, WorkflowOutputData? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType is JsonToken.Null)
        {
            return null;
        }

        var jsonObject = JObject.Load(reader);
        string? outputType = jsonObject["OutputType"]?.Value<string>();

        return outputType switch
        {
            "Table" => jsonObject.ToObject<TableOutput>(serializer),
            "Chart" => jsonObject.ToObject<ChartOutput>(serializer),
            "GridLayout" => jsonObject.ToObject<GridLayoutOutput>(serializer),
            _ => throw new JsonSerializationException($"Unknown WorkflowOutputData type: {outputType}")
        };
    }

    public override void WriteJson(JsonWriter writer, WorkflowOutputData? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        var jsonObject = JObject.FromObject(value, serializer);
        jsonObject.WriteTo(writer);
    }
}
