using IniaAi.Common.Reflection;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates.Abstractions;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

/// <summary>
/// Execution state for VerificationProperties, mirroring
/// <see cref="AgentPropertiesExecutionState"/>: the strategy overwrites the rubric members
/// with rendered per-run content and keeps <see cref="RunPasses"/> as the run ledger. No
/// output schema rides here — the verdict envelope is system-owned and injected at dispatch.
/// Engine configuration (model, effort, the fuses) deliberately has no mirror: config is
/// never templated, so the block's own properties already are the record of what was declared.
/// </summary>
public class VerificationPropertiesExecutionState
    : BlockPropertiesExecutionStateBase<VerificationPropertiesExecutionState>
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

    /// <summary>
    /// The runs this block bought, in order — the verifier's own legs. Redo legs belong to the
    /// members and land on their ledgers.
    /// </summary>
    [JsonProperty(nameof(RunPasses))]
    public List<AgentRunPass> RunPasses { get; set; } = [];

    public bool ShouldSerializeRunPasses()
    {
        return RunPasses.Count > 0;
    }

    /// <summary>
    /// What every round concluded, one entry per round — the derived verdict, or why the
    /// machinery could not judge. The loop reads the latest entry to steer; the release surface
    /// reads the whole ledger for provenance and per-round attribution; the failure record
    /// reads each round for its per-criterion rows. A round that re-runs (an editor re-run)
    /// replaces its own entry and no other.
    /// </summary>
    [JsonProperty(nameof(Judgments))]
    public List<VerificationJudgment> Judgments { get; set; } = [];

    public bool ShouldSerializeJudgments()
    {
        return Judgments.Count > 0;
    }

    /// <summary>The highest-round entry of <see cref="Judgments"/> — what the loop steers by.</summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public VerificationJudgment? LatestJudgment => Judgments.Count == 0
        ? null
        : Judgments.MaxBy(static judgment => judgment.Round);

    /// <summary>
    /// The provenance the members' work released under, derived from <see cref="Judgments"/> at
    /// settle. Null while the loop is in flight and on the unsatisfiable outcome, which fails
    /// the workflow instead of releasing.
    /// </summary>
    [JsonProperty(nameof(ReleaseMarker), NullValueHandling = NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public VerificationReleaseMarker? ReleaseMarker { get; set; }

    [JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    internal VerificationPropertiesExecutionState()
    {
    }

    public VerificationPropertiesExecutionState(VerificationProperties source)
    {
        WorkerSkills = [.. source.WorkerSkills.Select(static skill => skill.Clone())];
        Prompt = source.PromptTemplate;
        SystemPrompt = source.SystemPromptTemplate;
    }

    /// <summary>
    /// Re-resolution rebuilds the template-derived members but must not destroy the executed
    /// record: <see cref="RunPasses"/>, <see cref="Judgments"/> and <see cref="ReleaseMarker"/>
    /// carry over from the state being replaced.
    /// </summary>
    public VerificationPropertiesExecutionState(
        VerificationProperties source,
        VerificationPropertiesExecutionState? previous)
        : this(source)
    {
        RunPasses = previous?.RunPasses is null ? [] : [.. previous.RunPasses];
        Judgments = previous?.Judgments is null ? [] : [.. previous.Judgments];
        ReleaseMarker = previous?.ReleaseMarker;
    }
}
