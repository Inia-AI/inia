namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

/// <summary>
/// The run declaration shared by every agent-family block type: the worker skills, the two
/// prompt templates rendered under the double-brace agent-template grammar, the model/effort
/// engine configuration, and the four declared fuses. Guards, grammar sweeps, and editors
/// consume the family through this surface instead of switching per concrete type.
/// </summary>
public interface IAgentRunProperties
{
    List<AgentWorkerSkill> WorkerSkills { get; set; }

    string PromptTemplate { get; set; }

    string SystemPromptTemplate { get; set; }

    /// <summary>
    /// A specific model for the block's runs. Blank lets <see cref="ShouldUseAdvancedModel"/>
    /// choose the deployment's tier model; set, it wins over the tier.
    /// </summary>
    string? Model { get; set; }

    /// <summary>
    /// Runs the deployment's advanced agent model when on, its basic agent model when off.
    /// Applies only while <see cref="Model"/> is blank.
    /// </summary>
    bool ShouldUseAdvancedModel { get; set; }

    string? Effort { get; set; }

    int? DeadlineMinutes { get; set; }

    decimal? MaxBudgetUsd { get; set; }

    int? MaxTurns { get; set; }

    int? MaxRetainedOutputMib { get; set; }

    /// <summary>
    /// The block's run ledger — the <see cref="AgentRunPass"/> rows its execution state
    /// carries, null before any execution state exists. Family-level so consumers that settle
    /// or read runs across the family (the leaked-run janitor, the verifier's check-file
    /// staging) reach one surface instead of casting per concrete type.
    /// </summary>
    List<AgentRunPass>? RunLedger { get; }
}
