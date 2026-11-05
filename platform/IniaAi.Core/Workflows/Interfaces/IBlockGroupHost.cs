using IniaAi.Configuration;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Workflows.Interfaces;

/// <summary>
/// The engine operations an <see cref="IBlockGroupStrategy"/> runs its group with. Each is the
/// engine's own step, so a group's passes persist, fence and materialize exactly as any block does.
/// </summary>
internal interface IBlockGroupHost
{
    ILogger Logger { get; }

    PlatformSettings PlatformSettings { get; }

    IServiceScopeFactory ServiceScopeFactory { get; }

    /// <summary>The execution row of the run; null for an editor run, which persists nothing.</summary>
    WorkflowExecution? RootOutput { get; }

    string? OrganizationId { get; }

    /// <summary>True once another writer published a terminal state, after which nothing may land on the row.</summary>
    bool IsFenced { get; }

    /// <summary>The run's block-concurrency cap; every pass a group dispatches takes a permit here.</summary>
    IBlockUnitPermit UnitPermits { get; }

    /// <summary>The once-per-block prelude. Returns the skip reason, or null when the block may run.</summary>
    Task<string?> PrepareBlockRunAsync(Section section, Block block, CancellationToken cancellationToken);

    /// <summary>One strategy execution of a group's block in a fresh service scope, marked as group work.</summary>
    Task<BlockAnnotations?> ExecuteGroupPassAsync(
        BlockGroupRun run,
        Section section,
        Block block,
        int round,
        VerificationRedoLeg? redoLeg,
        bool runPrelude,
        CancellationToken cancellationToken);

    /// <summary>Persists a skip, or builds the group block's own report output.</summary>
    Task<string?> MaterializeGroupBlockOutputAsync(
        BlockGroupRun run,
        BlockAnnotations? blockAnnotations,
        CancellationToken cancellationToken);

    /// <summary>Materializes the held-back report output of every completed member of the group.</summary>
    Task ReleaseWithheldMembersAsync(BlockGroupRun run, CancellationToken cancellationToken);

    /// <summary>Replaces the held-back annotations a member's report output materializes from.</summary>
    void Withhold(Block member, BlockAnnotations? blockAnnotations);

    /// <summary>Persists a block's state with no report paragraph and no log row.</summary>
    Task PersistBlockStateAsync(Block block, CancellationToken cancellationToken);

    Task FenceIfExecutionWentTerminalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the annotation pipeline again on a block's settled response, with the agent citation
    /// facts stored beside it, as when the block's own run produced it.
    /// </summary>
    BlockAnnotations RebuildAnnotations(Section section, Block block, string? citationFactsJson);
}
