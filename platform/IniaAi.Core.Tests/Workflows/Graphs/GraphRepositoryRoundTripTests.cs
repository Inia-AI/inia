using IniaAi.Core.Workflows.Graph;
using IniaAi.Infrastructure.Persistence.Graph;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Graphs;

/// <summary>
/// Regression guard for the persisted-graph provenance path. Block nodes are keyed by
/// <c>block.Id</c> (so two blocks sharing a ReplacementTag stay distinct), but the public provenance
/// API addresses nodes by <c>ReplacementTag</c>. The tag must therefore survive a save/reload through
/// <see cref="GraphRepository"/>; if it does not, a tag-addressed query against a cache-hit reloaded
/// graph (where the node Id is the block.Id) matches neither the Id nor the null tag and throws.
/// </summary>
public class GraphRepositoryRoundTripTests : IClassFixture<WorkflowsFixture<GraphRepositoryRoundTripTests>>
{
    private const string RUN_ID = "round-trip-run";
    private const string BLOCK_ID = "blk-1";
    private const string TAG = "liquidity_usd";

    private readonly IServiceProvider _sp;

    public GraphRepositoryRoundTripTests(
        WorkflowsFixture<GraphRepositoryRoundTripTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task SaveThenGet_PreservesReplacementTag_OnIdKeyedNode_SoTagQueriesResolve()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        IDbContextFactory<UserDataDbContext> factory = _sp.GetRequiredService<IDbContextFactory<UserDataDbContext>>();

        WorkflowGraph graph = new()
        {
            Nodes =
            [
                new WorkflowGraphNode
                {
                    Id = BLOCK_ID,
                    Kind = WorkflowGraphNodeKinds.BLOCK,
                    Label = "Liquidity USD",
                    Order = 0,
                    ReplacementTag = TAG,
                },
            ],
        };

        await using (UserDataDbContext write = await factory.CreateDbContextAsync(ct))
        {
            var repo = new GraphRepository(write);
            await repo.SaveGraphAsync(RUN_ID, graph);
            _ = await write.SaveChangesAsync(ct);
        }

        await using (UserDataDbContext read = await factory.CreateDbContextAsync(ct))
        {
            var repo = new GraphRepository(read);
            WorkflowGraph? reloaded = await repo.GetGraphAsync(RUN_ID);

            Assert.NotNull(reloaded);
            WorkflowGraphNode node = Assert.Single(reloaded!.Nodes);

            // The node is keyed by block.Id, but the tag survived the round-trip.
            Assert.Equal(BLOCK_ID, node.Id);
            Assert.Equal(TAG, node.ReplacementTag);

            // The GraphQueryService resolution (n.Id == target || n.ReplacementTag == target) now finds
            // a tag-addressed target against the Id-keyed reloaded node.
            Assert.Contains(reloaded.Nodes, n => n.Id == TAG || n.ReplacementTag == TAG);
        }
    }
}
