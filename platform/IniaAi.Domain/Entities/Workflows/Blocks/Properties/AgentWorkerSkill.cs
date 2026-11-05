using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

public sealed class AgentWorkerSkill
{
    public string Name
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    public string Description
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    public string Content
    {
        get;
        set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>
    /// Auxiliary files staged read-only beside the skill by the agent runtime (wire
    /// <c>skillFiles[].assets</c>). Normalized to null when empty so skills saved before
    /// assets existed keep their serialized bytes — and their workflow content hashes —
    /// unchanged: both serializers omit a null, and neither emits an empty list.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentWorkerSkillAsset>? Assets
    {
        get;
        set => field = value is { Count: > 0 } ? value : null;
    }

    /// <summary>
    /// The files opted in with <see cref="AgentWorkerSkillAsset.FillTags"/>. Every tag scan and
    /// rewrite that reads skill content reads these too; an unflagged file is never read for tags.
    /// </summary>
    public IEnumerable<AgentWorkerSkillAsset> TagFilledAssets()
    {
        return (Assets ?? []).Where(static asset => asset.FillTags);
    }

    public AgentWorkerSkill Clone()
    {
        return new AgentWorkerSkill
        {
            Name = Name,
            Description = Description,
            Content = Content,
            Assets = Assets?.Select(static asset => asset.Clone()).ToList(),
        };
    }
}
