using IniaAi.Configuration;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Re-execution shape: each snapshot block ALREADY carries a <see cref="BlockResponse"/> (a
/// fresh-GUID copy of a prior run's response) before the per-block persist runs, and the strategy
/// then mints ANOTHER fresh-GUID response for the same block. <c>BlockResponses</c> is UNIQUE on
/// <c>BlockId</c>, so an upsert keyed on the PK would INSERT a duplicate -> Postgres 23505. The
/// writer keys its upsert on <c>BlockId</c>, so there must be exactly one row per block and it must
/// hold this run's result, not the stale one.
/// <para>
/// In-memory EF does not enforce the unique index, so this only reproduces on Npgsql. CI-excluded;
/// run locally against a real Postgres via <c>appsettings.Tests.json</c>.
/// </para>
/// </summary>
public class ParallelWorkflowPersistenceReExecutionTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string MERGE_FIRST_TEMPLATE = "first block, no upstream";
    private const string STALE_RESPONSE_MARKER = "STALE_PRIOR_RUN_RESPONSE_DO_NOT_KEEP";

    private readonly ApiTestFixture _fixture;

    public ParallelWorkflowPersistenceReExecutionTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ExecuteWorkflow_WhenSnapshotBlocksAlreadyHaveAResponse_PersistsExactlyOneFreshResponsePerBlock()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PooledUserHandle handle = _fixture.UserPool.CheckoutUserHandle();

        IServiceScope scope = _fixture.ClientBuilder.GetNewScope<IServiceScopeFactory>().Service.CreateScope();

        try
        {
            AssertParallelismIsAtLeastTwo(scope);

            IUserDataUnitOfWork userDataUnitOfWork = scope.ServiceProvider
                .GetRequiredService<IUserDataUnitOfWork>();
            IWorkflowHandler handler = ResolveGenericHandler(scope);

            Workflow workflow = BuildMultiSectionMergeWorkflowWithPriorResponses(
                handle.User.OrganizationId,
                handle.User.Id);

            await handler.CreateRootOutputAsync(
                userDataUnitOfWork,
                Guid.NewGuid().ToString(),
                workflow,
                handle.User.OrganizationId,
                handle.User.Id);

            Workflow executed = await handler.ExecuteWorkflowAsync(
                workflow,
                anonymisationMap: new(),
                organizationId: handle.User.OrganizationId,
                jobId: Guid.NewGuid().ToString(),
                userDataUnitOfWork,
                userId: handle.User.Id,
                inputs: [],
                cancellationToken: cancellationToken);

            List<Block> executedBlocks = CollectBlocks(executed);
            Assert.True(executedBlocks.Count >= 6, "Expected a multi-block workflow.");

            await using UserDataDbContext db = await CreateReadContextAsync(scope);

            foreach (Block block in executedBlocks)
            {
                List<BlockResponse> responses = await db.BlockResponses
                    .AsNoTracking()
                    .Where(r => r.BlockId == block.Id)
                    .ToListAsync(cancellationToken);

                Assert.True(
                    responses.Count == 1,
                    $"Block {block.Id} ({block.ReplacementTag}) has {responses.Count} BlockResponse rows; expected exactly 1.");

                BlockResponse persisted = responses[0];

                Assert.False(
                    string.IsNullOrEmpty(persisted.RawResponse),
                    $"Block {block.Id} ({block.ReplacementTag}) persisted an empty RawResponse.");
                Assert.False(
                    persisted.RawResponse!.Contains(STALE_RESPONSE_MARKER, StringComparison.Ordinal),
                    $"Block {block.Id} ({block.ReplacementTag}) kept the STALE prior-run response: '{persisted.RawResponse}'.");
                Assert.Contains(MERGE_FIRST_TEMPLATE, persisted.RawResponse, StringComparison.Ordinal);
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    private static void AssertParallelismIsAtLeastTwo(IServiceScope scope)
    {
        PlatformSettings settings = scope.ServiceProvider
            .GetRequiredService<IOptions<PlatformSettings>>()
            .Value;

        Assert.True(
            settings.MaxBlockParallelism >= 2,
            $"This test must run at MaxBlockParallelism >= 2 to exercise the race; configured value was {settings.MaxBlockParallelism}.");
    }

    private static IWorkflowHandler ResolveGenericHandler(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredKeyedService<IWorkflowHandler>(WorkflowType.GENERIC);
    }

    private static async Task<UserDataDbContext> CreateReadContextAsync(IServiceScope scope)
    {
        IDbContextFactory<UserDataDbContext> factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        return await factory.CreateDbContextAsync();
    }

    private static Workflow BuildMultiSectionMergeWorkflowWithPriorResponses(
        string organizationId,
        string userId)
    {
        Workflow root = new()
        {
            Id = Guid.NewGuid().ToString(),
            Title = "parallel-persistence-reexec-root",
            OrganizationId = organizationId,
            UserId = userId,
        };

        for (int sectionIndex = 0; sectionIndex < 3; sectionIndex++)
        {
            Section section = new($"section-{sectionIndex}", root)
            {
                Id = Guid.NewGuid().ToString(),
                Title = $"section-{sectionIndex}",
                Order = sectionIndex,
            };

            string firstTag = $"sec{sectionIndex}_first";
            string secondTag = $"sec{sectionIndex}_second";

            Block first = new(BlockTypes.Merge, section, replacementTag: firstTag)
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Section {sectionIndex} First",
                Properties = new MergeProperties
                {
                    TextTemplate = MERGE_FIRST_TEMPLATE,
                },
            };

            Block second = new(BlockTypes.Merge, section, replacementTag: secondTag)
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"Section {sectionIndex} Second",
                Properties = new MergeProperties
                {
                    TextTemplate = $"second reads from {{{firstTag}}}",
                },
            };

            SeedPriorResponse(first);
            SeedPriorResponse(second);
        }

        return root;
    }

    private static void SeedPriorResponse(Block block)
    {
        // The BlockResponse(Block) ctor wires BlockId / BlockResponseId / back-reference.
        _ = new BlockResponse(block)
        {
            RawResponse = $"{STALE_RESPONSE_MARKER} for {block.ReplacementTag}",
        };
    }

    private static List<Block> CollectBlocks(Workflow root)
    {
        List<Block> blocks = [];

        foreach (Section section in root.Sections.OrderBy(s => s.Order))
        {
            blocks.AddRange(section.Blocks.OrderBy(b => b.Order));
        }

        return blocks;
    }
}
