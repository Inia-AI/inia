using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

public class AgentProperties : BlockProperties, IAgentRunProperties
{
    /// <summary>
    /// Worker skills bundled into the agent run request. Deliberately
    /// invisible to the engine's substitution machinery on both the template and
    /// execution-state sides: skill content is rendered by the block strategy under
    /// the strict double-brace agent-template grammar, so single-brace text (JSON
    /// literals, prose braces) stays literal instead of being resolved or defaulted.
    /// Double-brace engine tags are still enumerated — <see cref="GetAllPlaceholders"/>
    /// appends them via <see cref="AgentTemplateTags"/> so graph edges, dispatch
    /// inputs, and save-time checks see skill references.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [CloneIgnore]
    [JsonProperty(nameof(WorkerSkills), NullValueHandling = NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(AgentWorkerSkillsNewtonsoftConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(AgentWorkerSkillsSystemTextJsonConverter))]
    public List<AgentWorkerSkill> WorkerSkills { get; set; } = [];

    /// <summary>
    /// The worker's user prompt (the start request's required <c>prompt</c>). Rendered under
    /// the double-brace agent-template grammar like skills; blank falls back to the strategy's
    /// default, which states the task and never points at skill files.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(PromptTemplate), NullValueHandling = NullValueHandling.Ignore)]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Optional system prompt override for the worker (wire <c>systemPrompt</c>); blank keeps
    /// the runtime's default. Rendered under the double-brace agent-template grammar.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(SystemPromptTemplate), NullValueHandling = NullValueHandling.Ignore)]
    public string SystemPromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Optional JSON Schema the run's final output must conform to (wire
    /// <c>outputFormat.schema</c>, type json_schema). Kept verbatim — never templated —
    /// because schemas are structure, not prose, and substitution would corrupt them.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(OutputSchema), NullValueHandling = NullValueHandling.Ignore)]
    public string OutputSchema { get; set; } = string.Empty;

    /// <summary>
    /// A specific model for the worker run, overriding <see cref="ShouldUseAdvancedModel"/>;
    /// blank lets the tier choose. Stored provider-neutral (<c>claude-sonnet-5-5</c>) and resolved
    /// to the deployment's allowlist form when the run starts. Save-time validation accepts any
    /// provider's id of a model in <c>AgentRuntimeSettings.AllowedModels</c> and stores the
    /// neutral id, except on a block inside a shared-component link, which keeps the id its
    /// definition carries; the runtime's own allowlist
    /// (a 400 on start) is the backstop when the platform mirror drifts.
    /// Engine configuration, not run content: never templated, no execution-state mirror —
    /// the same rule applies to every future config knob (effort, budget, …).
    /// </summary>
    [JsonProperty(nameof(Model), NullValueHandling = NullValueHandling.Ignore)]
    public string? Model { get; set; }

    /// <summary>
    /// Runs the deployment's advanced agent model (<c>AgentRuntimeSettings.AdvancedModel</c>,
    /// Opus by default) when on, its basic agent model (<c>AgentRuntimeSettings.BasicModel</c>,
    /// Sonnet by default) when off. <see cref="Model"/>, when set, wins over both.
    /// </summary>
    [JsonProperty(nameof(ShouldUseAdvancedModel))]
    public bool ShouldUseAdvancedModel { get; set; } = true;

    /// <summary>
    /// Reasoning effort override for the worker run. Null omits effort from the start request
    /// so the runtime/SDK default applies; save-time validation restricts a set value to
    /// <c>AgentRuntimeSettings.EffortLevels</c>, with the runtime's own vocabulary check
    /// (a 400 on start) as the backstop. Engine configuration like <see cref="Model"/>:
    /// never templated, no execution-state mirror.
    /// </summary>
    [JsonProperty(nameof(Effort), NullValueHandling = NullValueHandling.Ignore)]
    public string? Effort { get; set; }

    /// <summary>
    /// Wall-clock deadline the run declares, in minutes — what an author reads and sets, while
    /// the millisecond wire value is composed from it. Null declares nothing: the fuse is left
    /// off the start request and the runtime's own deployed default governs the run. A value
    /// above the runtime's ceiling is refused with a 400 before any spend, never clamped here.
    /// Engine configuration like <see cref="Model"/>: never templated, no execution-state mirror.
    /// </summary>
    [JsonProperty(nameof(DeadlineMinutes), NullValueHandling = NullValueHandling.Ignore)]
    public int? DeadlineMinutes { get; set; }

    /// <summary>
    /// Spend ceiling the run declares, in dollars. Same declare-or-stay-silent contract as
    /// <see cref="DeadlineMinutes"/>. Deliberately per-run, not per-block: a block that fails on
    /// retryable infrastructure trouble starts a fresh run, and each pass is entitled to this
    /// budget again, so a block's worst-case spend is this value times
    /// <c>AgentRuntimeSettings.MaxRunPasses</c>.
    /// </summary>
    [JsonProperty(nameof(MaxBudgetUsd), NullValueHandling = NullValueHandling.Ignore)]
    public decimal? MaxBudgetUsd { get; set; }

    /// <summary>
    /// Turn ceiling the run declares. Same declare-or-stay-silent contract as
    /// <see cref="DeadlineMinutes"/>.
    /// </summary>
    [JsonProperty(nameof(MaxTurns), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxTurns { get; set; }

    /// <summary>
    /// How many MiB of output the run declares it will retain. Same declare-or-stay-silent
    /// contract as <see cref="DeadlineMinutes"/>. The runtime charges the declaration at
    /// admission, so a block that keeps little should say so rather than reserve capacity other
    /// runs could use.
    /// </summary>
    [JsonProperty(nameof(MaxRetainedOutputMib), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxRetainedOutputMib { get; set; }

    /// <summary>
    /// Paths under <c>out/</c> (for example <c>out/rows.json</c>) whose JSON content becomes part
    /// of the block output. Empty, the default, keeps the output as the model's answer. Set, the
    /// output is <c>{"answer": …, "files": {"rows": …}}</c>, built from the files the settled run
    /// published, and the agent's task names the files to write. The output schema still checks
    /// only the answer. Engine configuration like <see cref="Model"/>: never templated.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(OutputFiles), NullValueHandling = NullValueHandling.Ignore)]
    public List<string> OutputFiles { get; set; } = [];

    public bool ShouldSerializeOutputFiles()
    {
        return OutputFiles.Count > 0;
    }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public AgentPropertiesExecutionState? ExecutionState { get; set; }

    List<AgentRunPass>? IAgentRunProperties.RunLedger => ExecutionState?.RunPasses;

    public AgentProperties()
    {
        Type = BlockTypes.Agent;
    }

    /// <summary>
    /// Reflection discovery skips <see cref="WorkerSkills"/> (ignore-flagged), so engine tags
    /// referenced under the double-brace agent-template grammar are appended here — one tag set
    /// feeding graph edges, dispatch inputs, and save-time checks alike.
    /// </summary>
    public override List<string> GetAllPlaceholders()
    {
        return [.. base.GetAllPlaceholders()
            .Concat(WorkerSkills.SelectMany(static skill => AgentTemplateTags.ExtractEngineTags(skill.Content)))
            .Concat(WorkerSkills.SelectMany(static skill => AgentTemplateTags.ExtractEngineTags(skill.Description)))
            .Concat(WorkerSkills
                .SelectMany(static skill => skill.TagFilledAssets())
                .SelectMany(static asset => AgentTemplateTags.ExtractEngineTags(asset.Content)))
            .Concat(AgentTemplateTags.ExtractEngineTags(PromptTemplate))
            .Concat(AgentTemplateTags.ExtractEngineTags(SystemPromptTemplate))
            .Distinct()];
    }

    public override void TransformAgentTemplateTags(Func<string, string> transform)
    {
        foreach (AgentWorkerSkill skill in WorkerSkills)
        {
            skill.Content = AgentTemplateTags.TransformEngineTags(skill.Content, transform);
            skill.Description = AgentTemplateTags.TransformEngineTags(skill.Description, transform);

            foreach (AgentWorkerSkillAsset asset in skill.TagFilledAssets())
            {
                asset.Content = AgentTemplateTags.TransformEngineTags(asset.Content, transform);
            }
        }

        PromptTemplate = AgentTemplateTags.TransformEngineTags(PromptTemplate, transform);
        SystemPromptTemplate = AgentTemplateTags.TransformEngineTags(SystemPromptTemplate, transform);
    }

    public override BlockProperties Clone()
    {
        var clone = new AgentProperties();
        CloneHelper.CopyScalars(this, clone);
        clone.WorkerSkills = [.. WorkerSkills.Select(static skill => skill.Clone())];
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new AgentPropertiesExecutionState(this, ExecutionState);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new AgentPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
