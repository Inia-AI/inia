using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Replay;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.Core.Tests.Workflows.Scheduling.Helpers;

/// <summary>
/// Drives a persisted workflow run against the in-memory stores: a substituted block writer whose
/// per-call behaviour each test supplies, and the create-root-output-then-execute sequence the
/// engine expects.
/// </summary>
internal static class TestWorkflowRuns
{
    internal const string ORG_ID = "TestOrg";
    internal const string USER_ID = "TestUser";

    /// <summary>
    /// A linear chain of Merge blocks named C0..Cn, each reading the previous block's tag, so at
    /// cap=1 they dispatch in a known order. All output sources, so each produces a paragraph.
    /// </summary>
    internal static Workflow BuildChainedWorkflow(
        string idPrefix,
        int length = 4,
        string? criticalBlockName = null)
    {
        Workflow root = new() { Id = $"{idPrefix}-root", Title = idPrefix };
        Section section = new("S", root) { Id = $"{idPrefix}-sec", Order = 0 };

        for (int i = 0; i < length; i++)
        {
            string name = $"C{i}";
            _ = new Block(BlockTypes.Merge, section, replacementTag: $"{idPrefix}_{i}")
            {
                Id = $"{idPrefix}-blk-{i}",
                Name = name,
                IsOutputSource = true,
                Properties = new MergeProperties
                {
                    TextTemplate = i == 0 ? "start" : $"after {{{idPrefix}_{i - 1}}}",
                    IsCritical = name == criticalBlockName ? true : null,
                },
            };
        }

        return root;
    }

    /// <summary>
    /// Independent Merge blocks named P0..Pn with no cross-block placeholders — a single
    /// scheduling layer, so at cap >= 2 they dispatch concurrently.
    /// </summary>
    internal static Workflow BuildParallelWorkflow(
        string idPrefix,
        int count,
        string? criticalBlockName = null)
    {
        Workflow root = new() { Id = $"{idPrefix}-par-root", Title = $"{idPrefix}-par" };
        Section section = new("S", root) { Id = $"{idPrefix}-par-sec", Order = 0 };

        for (int i = 0; i < count; i++)
        {
            string name = $"P{i}";
            _ = new Block(BlockTypes.Merge, section, replacementTag: $"{idPrefix}p_{i}")
            {
                Id = $"{idPrefix}-par-blk-{i}",
                Name = name,
                IsOutputSource = true,
                Properties = new MergeProperties
                {
                    TextTemplate = $"independent {i}",
                    IsCritical = name == criticalBlockName ? true : null,
                },
            };
        }

        return root;
    }

    /// <summary>One section, one block, of any type — for tests about a single block's behavior inside a real run.</summary>
    internal static Workflow BuildSingleBlockWorkflow(
        string idPrefix,
        BlockTypes type,
        BlockProperties properties)
    {
        Workflow root = new() { Id = $"{idPrefix}-root", Title = idPrefix };
        Section section = new("S", root) { Id = $"{idPrefix}-sec", Order = 0 };
        _ = new Block(type, section, replacementTag: $"{idPrefix}_0")
        {
            Id = $"{idPrefix}-blk",
            Name = "B0",
            Properties = properties,
        };

        return root;
    }

    /// <summary>Reads the execution back through a scope of its own, so nothing the run left tracked can mask the stored row.</summary>
    internal static async Task<WorkflowExecution?> ReloadExecutionAsync(
        IServiceProvider serviceProvider,
        string workflowExecutionId)
    {
        using IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        return await unitOfWork.WorkflowExecutions
            .SingleOrDefaultAsync(predicate: x => x.Id == workflowExecutionId);
    }

    /// <summary>Stands in for another host or a user cancelling the run while this one is mid-flight.</summary>
    internal static async Task MarkTerminalFromOutsideAsync(
        IServiceProvider serviceProvider,
        string workflowExecutionId,
        WorkflowState terminalState)
    {
        using IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        _ = await unitOfWork.WorkflowExecutions.TryMarkTerminalIfActiveAsync(
            workflowExecutionId,
            ORG_ID,
            terminalState);
    }

    internal static IExecutedBlockWriter SubstitutedWriter(Func<CallInfo, Task> persistBlockResult)
    {
        IExecutedBlockWriter writer = Substitute.For<IExecutedBlockWriter>();
        _ = writer.PersistBlockResultAsync(
                Arg.Any<Block>(),
                Arg.Any<ReportParagraph?>(),
                Arg.Any<WorkflowExecutionLog?>(),
                Arg.Any<CancellationToken>())
            .Returns(persistBlockResult);

        return writer;
    }

    internal static async Task RunWithOutputAsync(
        IServiceProvider serviceProvider,
        WorkflowHandler handler,
        Workflow workflow,
        CancellationToken cancellationToken,
        IReadOnlyList<ReplayOutput>? replayOutputs = null)
    {
        using IServiceScope scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        await handler.CreateRootOutputAsync(
            unitOfWork,
            jobId: Guid.NewGuid().ToString(),
            workflow,
            ORG_ID,
            USER_ID);

        _ = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: Guid.NewGuid().ToString(),
            unitOfWork,
            USER_ID,
            inputs: [],
            replayOutputs: replayOutputs,
            cancellationToken: cancellationToken);
    }

    // "_w_r1_1"-shaped: role, round and pass are all single digits in these tests.
    private const int RUN_ID_SUFFIX_LENGTH = 7;

    /// <summary>
    /// The persisted execution re-keys blocks, so the executed block id comes out of the minted
    /// run id: what sits between the workflow-execution prefix and the role/round/pass suffix.
    /// </summary>
    internal static string ExecutedBlockIdOf(string executionId)
    {
        int prefixEnd = executionId.IndexOf('_', StringComparison.Ordinal) + 1;

        return executionId[prefixEnd..^RUN_ID_SUFFIX_LENGTH];
    }

    internal static Block ReadPersistedBlock(IServiceProvider serviceProvider, string blockId)
    {
        IDbContextFactory<UserDataDbContext> factory =
            serviceProvider.GetRequiredService<IDbContextFactory<UserDataDbContext>>();
        using UserDataDbContext read = factory.CreateDbContext();

        return read.Blocks.AsNoTracking()
            .Include(block => block.BlockResponse)
            .Single(block => block.Id == blockId);
    }
}
