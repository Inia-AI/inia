using IniaAi.Domain.Entities.WorkflowExecutions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Infrastructure.Persistence.Serialization.Blocks;

/// <summary>
/// System.Text.Json converter for polymorphic WorkflowOutputData serialization.
/// Follows the same pattern as BlockPropertiesJsonConverter.
/// </summary>
public class WorkflowOutputDataSystemTextJsonConverter : JsonConverter<WorkflowOutputData>
{
    public override WorkflowOutputData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        string json = jsonDoc.RootElement.GetRawText();

        // Try to get explicit discriminator first
        string? outputType = null;

        if (jsonDoc.RootElement.TryGetProperty("outputType", out JsonElement outputTypeProp))
        {
            outputType = outputTypeProp.GetString();
        }
        else if (jsonDoc.RootElement.TryGetProperty("OutputType", out outputTypeProp))
        {
            outputType = outputTypeProp.GetString();
        }
        else if (jsonDoc.RootElement.TryGetProperty("$type", out outputTypeProp))
        {
            outputType = outputTypeProp.GetString();
        }

        // If no discriminator found, default to Table for legacy data compatibility
        if (string.IsNullOrEmpty(outputType))
        {
            outputType = "Table";
        }

        return outputType switch
        {
            "Table" => JsonSerializer.Deserialize<TableOutput>(json, options),
            "Chart" => JsonSerializer.Deserialize<ChartOutput>(json, options),
            "GridLayout" => JsonSerializer.Deserialize<GridLayoutOutput>(json, options),
            _ => throw new JsonException($"Unknown WorkflowOutputData type: {outputType}")
        };
    }

    public override void Write(Utf8JsonWriter writer, WorkflowOutputData value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}