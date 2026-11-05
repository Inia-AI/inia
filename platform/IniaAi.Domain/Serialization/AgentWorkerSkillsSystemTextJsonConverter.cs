using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Domain.Serialization;

public sealed class AgentWorkerSkillsSystemTextJsonConverter : JsonConverter<List<AgentWorkerSkill>>
{
    public override List<AgentWorkerSkill> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        if (root.ValueKind is JsonValueKind.Object)
        {
            return [.. root.EnumerateObject().Select(static property => new AgentWorkerSkill
            {
                Name = property.Name,
                Content = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    _ => property.Value.GetRawText(),
                },
            })];
        }

        if (root.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return [.. root.EnumerateArray()
            .Where(static element => element.ValueKind is not JsonValueKind.Null)
            .Select(element => element.Deserialize<AgentWorkerSkill>(options))
            .OfType<AgentWorkerSkill>()];
    }

    public override void Write(Utf8JsonWriter writer, List<AgentWorkerSkill> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (AgentWorkerSkill skill in value)
        {
            JsonSerializer.Serialize(writer, skill, options);
        }

        writer.WriteEndArray();
    }
}
