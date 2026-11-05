using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// The verification block: judges a named group of Agent blocks — its members — against the
/// engineer-declared criteria, and on failure sends members back to redo their work. The
/// verifier never rewrites member work, and it deliberately carries no output schema: the
/// verdict envelope is system-owned and injected at dispatch, so no authored property may
/// redefine it.
/// </summary>
public class VerificationProperties : BlockProperties, IAgentRunProperties
{
    /// <summary>
    /// The hard ceiling on <see cref="PassCap"/>, shared by the save-time gate and the editor's
    /// input so the two can never drift.
    /// </summary>
    public const int PASS_CAP_CEILING = 3;

    /// <summary>
    /// The verified group, each entry an Agent block's replacement tag plus that member's redo
    /// mode. Configuration, not run content: invisible to the substitution machinery, while
    /// <see cref="GetAllPlaceholders"/> surfaces the tags so graph edges and dispatch inputs
    /// see the verifier consuming its members' outputs. A null entry from the template API is
    /// dropped on the way in, so the save-time gates read a well-shaped list or none at all.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [CloneIgnore]
    [JsonProperty(
        nameof(Members),
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<VerificationMember> Members
    {
        get;
        set => field = value is null ? [] : [.. value.Where(static member => member is not null)];
    } = [];

    /// <summary>
    /// The declared criteria the verifier judges. Declared as structured configuration, never
    /// extracted from prose: the prose rubric in <see cref="WorkerSkills"/> and
    /// <see cref="PromptTemplate"/> explains how to judge and references these declarations. A
    /// null entry from the template API is dropped on the way in, like <see cref="Members"/>.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [CloneIgnore]
    [JsonProperty(
        nameof(Criteria),
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<VerificationCriterion> Criteria
    {
        get;
        set => field = value is null ? [] : [.. value.Where(static criterion => criterion is not null)];
    } = [];

    /// <summary>
    /// How many verify passes the loop may spend, counting the first (default 2 = verify,
    /// redo, re-verify; save-time validation caps it at 3). Repair under a noisy verifier
    /// measurably damages valid work past that, so the ceiling is a rule, not a tuning knob.
    /// </summary>
    [JsonProperty(nameof(PassCap))]
    public int PassCap { get; set; } = 2;

    /// <summary>
    /// The rubric's skill files, rendered by the block strategy under the strict double-brace
    /// agent-template grammar exactly like an Agent block's — see
    /// <see cref="AgentProperties.WorkerSkills"/> for why the substitution machinery must not
    /// touch them.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [CloneIgnore]
    [JsonProperty(nameof(WorkerSkills), NullValueHandling = NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(AgentWorkerSkillsNewtonsoftConverter))]
    [System.Text.Json.Serialization.JsonConverter(typeof(AgentWorkerSkillsSystemTextJsonConverter))]
    public List<AgentWorkerSkill> WorkerSkills { get; set; } = [];

    /// <summary>
    /// The verifier's user prompt. Rendered under the double-brace agent-template grammar;
    /// blank falls back to the strategy's default.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(PromptTemplate), NullValueHandling = NullValueHandling.Ignore)]
    public string PromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Optional system prompt override; blank keeps the runtime's default. Rendered under the
    /// double-brace agent-template grammar.
    /// </summary>
    [PlaceholderReplacementIgnore]
    [JsonProperty(nameof(SystemPromptTemplate), NullValueHandling = NullValueHandling.Ignore)]
    public string SystemPromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// A specific model for the verifier's runs, overriding <see cref="ShouldUseAdvancedModel"/>;
    /// blank lets the tier choose. Save-time validation restricts the model that runs, override
    /// or tier, to the allowed model list AND to Sonnet-class or above, since a small model produces evidence the
    /// guard discards. Engine configuration, not run content: never templated, no
    /// execution-state mirror.
    /// </summary>
    [JsonProperty(nameof(Model), NullValueHandling = NullValueHandling.Ignore)]
    public string? Model { get; set; }

    /// <summary>
    /// Runs the deployment's advanced agent model when on, its basic agent model when off, as
    /// <see cref="AgentProperties.ShouldUseAdvancedModel"/> does. <see cref="Model"/>, when set,
    /// wins over both.
    /// </summary>
    [JsonProperty(nameof(ShouldUseAdvancedModel))]
    public bool ShouldUseAdvancedModel { get; set; } = true;

    /// <summary>
    /// Reasoning effort override for the verifier's runs; null lets the runtime/SDK default
    /// apply. Engine configuration like <see cref="Model"/>.
    /// </summary>
    [JsonProperty(nameof(Effort), NullValueHandling = NullValueHandling.Ignore)]
    public string? Effort { get; set; }

    /// <summary>
    /// Wall-clock deadline each verifier leg declares, in minutes. Unlike an Agent block's
    /// optional fuse, save-time validation requires it: the loop's wall-clock ceiling is
    /// checked against the janitor window, and an undeclared deadline would make that ceiling
    /// a fiction.
    /// </summary>
    [JsonProperty(nameof(DeadlineMinutes), NullValueHandling = NullValueHandling.Ignore)]
    public int? DeadlineMinutes { get; set; }

    /// <summary>
    /// Spend ceiling each verifier leg declares, in dollars. Required at save time like
    /// <see cref="DeadlineMinutes"/>, so the displayed loop-spend ceiling is real.
    /// </summary>
    [JsonProperty(nameof(MaxBudgetUsd), NullValueHandling = NullValueHandling.Ignore)]
    public decimal? MaxBudgetUsd { get; set; }

    /// <summary>
    /// Turn ceiling each verifier leg declares. Optional like an Agent block's.
    /// </summary>
    [JsonProperty(nameof(MaxTurns), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxTurns { get; set; }

    /// <summary>
    /// How many MiB of output each verifier leg declares it will retain. Optional like an
    /// Agent block's.
    /// </summary>
    [JsonProperty(nameof(MaxRetainedOutputMib), NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxRetainedOutputMib { get; set; }

    [NotMapped]
    [JsonProperty(nameof(ExecutionState), NullValueHandling = NullValueHandling.Ignore)]
    public VerificationPropertiesExecutionState? ExecutionState { get; set; }

    List<AgentRunPass>? IAgentRunProperties.RunLedger => ExecutionState?.RunPasses;

    public VerificationProperties()
    {
        Type = BlockTypes.Verification;
    }

    /// <summary>
    /// Member tags are appended alongside the rubric's engine tags: the verifier consumes its
    /// members' outputs, so graph edges and dispatch inputs must see the dependency without
    /// waiting for explicit co-producer wiring.
    /// </summary>
    public override List<string> GetAllPlaceholders()
    {
        return [.. base.GetAllPlaceholders()
            .Concat(DeclaredMemberTags())
            .Concat(WorkerSkills.SelectMany(static skill => AgentTemplateTags.ExtractEngineTags(skill.Content)))
            .Concat(WorkerSkills.SelectMany(static skill => AgentTemplateTags.ExtractEngineTags(skill.Description)))
            .Concat(WorkerSkills
                .SelectMany(static skill => skill.TagFilledAssets())
                .SelectMany(static asset => AgentTemplateTags.ExtractEngineTags(asset.Content)))
            .Concat(AgentTemplateTags.ExtractEngineTags(PromptTemplate))
            .Concat(AgentTemplateTags.ExtractEngineTags(SystemPromptTemplate))
            .Distinct()];
    }

    /// <summary>
    /// The member tags as every consumer must read them — blanks dropped, duplicates collapsed.
    /// The graph builder, both shape checks, and the save-time guard all key on this projection,
    /// so it exists exactly once.
    /// </summary>
    public IEnumerable<string> DeclaredMemberTags()
    {
        return Members
            .Select(static member => member.MemberTag)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// The same emptiness answer as <see cref="DeclaredMemberTags"/> without building the
    /// projection. The permit-shape answer and the strategy's memberless check both read this,
    /// so the two can never drift into a node that holds no permit and dispatches nothing.
    /// </summary>
    public bool HasDeclaredMembers()
    {
        return Members.Any(static member => !string.IsNullOrWhiteSpace(member.MemberTag));
    }

    /// <summary>
    /// The declared redo mode of the member carrying <paramref name="memberTag"/>, or null when
    /// no declared member carries it. First declaration wins when duplicates slipped past the
    /// save gate, matching the failure record's collapse.
    /// </summary>
    public VerificationRedoMode? ConfiguredRedoModeFor(string memberTag)
    {
        return Members
            .FirstOrDefault(member => string.Equals(member.MemberTag, memberTag, StringComparison.Ordinal))
            ?.RedoMode;
    }

    /// <summary>
    /// The pass cap the loop actually spends: the declared cap clamped into
    /// [1, <see cref="PASS_CAP_CEILING"/>]. The save gate refuses an out-of-range declaration,
    /// so the clamp matters only for rows that predate it — and the editor's estimates quote
    /// this same number for any cap they price at all (a declaration below 1 gets no estimate).
    /// </summary>
    public int GetEffectivePassCap()
    {
        return Math.Clamp(PassCap, 1, PASS_CAP_CEILING);
    }

    public override BlockProperties Clone()
    {
        var clone = new VerificationProperties();
        CloneHelper.CopyScalars(this, clone);
        clone.Members = [.. Members.Select(static member => member.Clone())];
        clone.Criteria = [.. Criteria.Select(static criterion => criterion.Clone())];
        clone.WorkerSkills = [.. WorkerSkills.Select(static skill => skill.Clone())];
        return clone;
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState = new VerificationPropertiesExecutionState(this, ExecutionState);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
        ExecutionState ??= new VerificationPropertiesExecutionState(this);
        ExecutionState.ReplaceAllPlaceholders(asPlaceHolder, inputs, stringComparison);
    }

    public override void ClearExecutionState()
    {
        ExecutionState = null;
    }
}
