using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Annotations;

/// <summary>
/// Source expansion walks the annotation graph a run's <see cref="WorkflowAnnotationsContext"/>
/// holds, and blocks build their output in parallel, so two blocks reach the same upstream
/// <see cref="BlockSource"/> instance at the same time.
/// </summary>
public class BlockAnnotationProcessorConcurrencyTests
{
    private const string SHARED_VALUE = "100";
    private const string UPSTREAM_BLOCK_ID = "block-A";
    private const string SHARED_BLOCK_ID = "block-B";
    private const int UPSTREAM_FACT_COUNT = 200;
    private const int PARALLEL_BLOCKS = 8;
    private const int ROUNDS = 20;

    [Fact]
    public void ExpandUpstreamSources_WhenBlocksExpandTheSameUpstreamSourceInParallel_NeitherThrowsNorWritesIntoIt()
    {
        for (int round = 0; round < ROUNDS; round++)
        {
            WorkflowAnnotationsContext context = BuildSharedChain();
            using var start = new Barrier(PARALLEL_BLOCKS);
            Exception? failure = null;

            var blocks = new Thread[PARALLEL_BLOCKS];

            for (int i = 0; i < PARALLEL_BLOCKS; i++)
            {
                blocks[i] = new Thread(() =>
                {
                    var citation = new BlockSource
                    {
                        CitedText = SHARED_VALUE,
                        BlockId = SHARED_BLOCK_ID,
                        BlockTitle = "Block B"
                    };

                    start.SignalAndWait();

                    try
                    {
                        _ = BlockAnnotationProcessor.ExpandUpstreamSources(citation, context);
                    }
                    catch (Exception exception)
                    {
                        _ = Interlocked.CompareExchange(ref failure, exception, null);
                    }
                });

                blocks[i].Start();
            }

            foreach (Thread block in blocks)
            {
                block.Join();
            }

            Assert.Null(failure);
            BlockAnnotations? shared = context.GetAnnotationsForBlock(SHARED_BLOCK_ID);
            Assert.NotNull(shared);
            BlockSource sharedCitation = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(shared.Facts).Sources));
            Assert.Empty(sharedCitation.UpstreamSources);
        }
    }

    /// <summary>
    /// Block B cites block A through one <see cref="BlockSource"/> instance. Every expanding block
    /// reaches that same instance through block B's facts.
    /// </summary>
    private static WorkflowAnnotationsContext BuildSharedChain()
    {
        var upstreamAnnotations = new BlockAnnotations();

        for (int i = 0; i < UPSTREAM_FACT_COUNT; i++)
        {
            var fact = new Fact(SHARED_VALUE);

            fact.Sources.Add(new DocumentSource
            {
                CitedText = SHARED_VALUE,
                DocumentId = "doc1",
                PageId = $"page{i}",
                PageNumber = i,
                DocumentName = "Source.pdf"
            });

            upstreamAnnotations.AddFact(fact);
        }

        var sharedCitationToUpstream = new BlockSource
        {
            CitedText = SHARED_VALUE,
            BlockId = UPSTREAM_BLOCK_ID,
            BlockTitle = "Block A"
        };

        var sharedAnnotations = new BlockAnnotations();
        var sharedFact = new Fact(SHARED_VALUE);
        sharedFact.Sources.Add(sharedCitationToUpstream);
        sharedAnnotations.AddFact(sharedFact);

        var context = new WorkflowAnnotationsContext();
        context.SetAnnotationsForBlock(UPSTREAM_BLOCK_ID, upstreamAnnotations);
        context.SetAnnotationsForBlock(SHARED_BLOCK_ID, sharedAnnotations);

        return context;
    }
}
