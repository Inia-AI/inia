using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for AgentProperties. Initialized with the template content; the block
/// strategy overwrites <see cref="WorkerSkills"/>, <see cref="Prompt"/>, and
/// <see cref="SystemPrompt"/> with the rendered per-run content, so the persisted executed
/// workflow carries exactly what the run received, and keeps <see cref="RunPasses"/> as the
/// run ledger (write timing documented on <see cref="AgentRunPass"/>). Engine configuration
/// (model, effort, the fuses) deliberately has no mirror here: config is never templated, so
/// the block's own properties already are the record of what was declared.
/// </summary>
public class AgentPropertiesExecutionState : BlockPropertiesExecutionStateBase<AgentPropertiesExecutionState>
{
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(WorkerSkills), NullValueHandling = NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(AgentWorkerSkillsNewtonsoftConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(AgentWorkerSkillsSystemTextJsonConverter))]
    public List<AgentWorkerSkill> WorkerSkills { get; set; } = [];

    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(Prompt), NullValueHandling = NullValueHandling.Ignore)]
    public string Prompt { get; set; } = string.Empty;

    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(SystemPrompt), NullValueHandling = NullValueHandling.Ignore)]
    public string SystemPrompt { get; set; } = string.Empty;

    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(OutputSchema), NullValueHandling = NullValueHandling.Ignore)]
    public string OutputSchema { get; set; } = string.Empty;

    /// <summary>
    /// The runs this block bought, in order. Not template content like the members above but
    /// the executed record of what the block spent and which runs produced it, so a failure
    /// here is a starting point rather than a dead end.
    /// </summary>
    [JsonProperty(nameof(RunPasses))]
    public List<AgentRunPass> RunPasses { get; set; } = [];

    public bool ShouldSerializeRunPasses()
    {
        return RunPasses.Count > 0;
    }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal AgentPropertiesExecutionState()
    {
    }

    public AgentPropertiesExecutionState(AgentProperties source)
    {
        WorkerSkills = [.. source.WorkerSkills.Select(static skill => skill.Clone())];
        Prompt = source.PromptTemplate;
        SystemPrompt = source.SystemPromptTemplate;
        OutputSchema = source.OutputSchema;
    }

    /// <summary>
    /// Re-resolution rebuilds the template-derived members but must not destroy the executed
    /// record: <see cref="RunPasses"/> carries over from the state being replaced.
    /// </summary>
    public AgentPropertiesExecutionState(AgentProperties source, AgentPropertiesExecutionState? previous)
        : this(source)
    {
        RunPasses = previous?.RunPasses is null ? [] : [.. previous.RunPasses];
    }
}
