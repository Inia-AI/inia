using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// An auxiliary file shipped beside a worker skill: a runnable script, a fixture, a schema.
/// The agent runtime stages it read-only where the run's sandboxed Bash can read and execute
/// it, addressed as <c>$SKILL_ASSETS_DIR/&lt;skill-name&gt;/&lt;asset-name&gt;</c>. Content is
/// verbatim file text by default: never rendered under the agent-template grammar and never
/// scanned for engine tags, so tag-shaped strings inside a script stay literal. An author opts
/// one file into the skill-content treatment with <see cref="FillTags"/>.
/// </summary>
public sealed class AgentWorkerSkillAsset
{
    public string Name
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
    /// When true, the file's <c>{{tags}}</c> are filled at run time and scanned for dependencies
    /// exactly like skill content. Omitted by both serializers while false, so files saved before
    /// the flag existed keep their serialized bytes and workflow content hashes.
    /// </summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FillTags { get; set; }

    public AgentWorkerSkillAsset Clone()
    {
        return new AgentWorkerSkillAsset { Name = Name, Content = Content, FillTags = FillTags };
    }
}
