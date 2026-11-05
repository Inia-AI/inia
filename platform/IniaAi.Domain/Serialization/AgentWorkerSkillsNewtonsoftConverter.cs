using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Serialization;

public sealed class AgentWorkerSkillsNewtonsoftConverter : JsonConverter<List<AgentWorkerSkill>>
{
    public override List<AgentWorkerSkill> ReadJson(
        JsonReader reader,
        Type objectType,
        List<AgentWorkerSkill>? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        JToken token = JsonText.Load(reader);

        if (token.Type is JTokenType.Object)
        {
            return [.. ((JObject)token).Properties().Select(static property => new AgentWorkerSkill
            {
                Name = property.Name,
                Content = property.Value.Type switch
                {
                    JTokenType.Null => string.Empty,
                    JTokenType.String => property.Value.Value<string>() ?? string.Empty,
                    _ => property.Value.ToString(Formatting.None),
                },
            })];
        }

        if (token.Type is not JTokenType.Array)
        {
            return [];
        }

        return [.. token
            .Where(static item => item.Type is not JTokenType.Null)
            .Select(item => item.ToObject<AgentWorkerSkill>(serializer))
            .OfType<AgentWorkerSkill>()];
    }

    public override void WriteJson(JsonWriter writer, List<AgentWorkerSkill>? value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        foreach (AgentWorkerSkill skill in value ?? [])
        {
            serializer.Serialize(writer, skill);
        }

        writer.WriteEndArray();
    }
}
