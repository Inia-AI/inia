using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.PageOverviews;

public class ReadsPageOverviewsTests
{
    private static readonly Dictionary<RagTypes, bool> _readsOverviews = new()
    {
        [RagTypes.WholeDocument] = false,
        [RagTypes.TopNPages] = true,
        [RagTypes.TopNPagesPerFilterWithTwoNeighbors] = true,
        [RagTypes.TopPage] = true,
        [RagTypes.TopPageWithNFollowingPages] = true,
        [RagTypes.NPagesStartingFromIndex] = false,
        [RagTypes.KeywordBased] = false,
        [RagTypes.ExactPages] = false,
        [RagTypes.UseTableOfContents] = false,
        [RagTypes.UseAutoDetectedTableOfContents] = false,
        [RagTypes.RagAgent] = true,
    };

    [Fact]
    public void EveryRetrievalType_SaysWhetherItReadsOverviews()
    {
        foreach (RagTypes type in Enum.GetValues<RagTypes>())
        {
            Assert.True(_readsOverviews.TryGetValue(type, out bool expected), $"{type} is not classified here.");
            Assert.Equal(expected, new RagSettings { Type = type }.ReadsPageOverviews());
        }
    }

    [Fact]
    public void AKeywordSearch_ReadsOverviews_WhenItSearchesThem()
    {
        Assert.True(new RagSettings { Type = RagTypes.KeywordBased, ShouldSearchOverviews = true }.ReadsPageOverviews());
    }

    [Fact]
    public void AKeywordSearch_ReadsOverviews_WhenItAlsoMatchesBySimilarity()
    {
        Assert.True(new RagSettings { Type = RagTypes.KeywordBased, SmartFilterExpressions = ["revenue"] }.ReadsPageOverviews());
    }

    [Fact]
    public void ABlock_ReadsOverviews_ThroughItsRetrievalStep()
    {
        Block block = AiQuery(new AiQueryProperties());
        _ = new RagSettings(block) { Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors };

        Assert.True(block.ReadsPageOverviews());
    }

    [Fact]
    public void ABlock_ReadsOverviews_WithTheUseOverviewsSetting()
    {
        Assert.True(AiQuery(new AiQueryProperties { ShouldUseOverviews = true }).ReadsPageOverviews());
    }

    [Fact]
    public void ABlockThatTakesNoDocuments_DoesNotReadOverviews_ThroughALeftoverRetrievalStep()
    {
        var workflow = new Workflow("root");
        Block block = new(BlockTypes.MergeJson, new Section("Analysis", workflow), "merge");
        _ = new RagSettings(block) { Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors };

        Assert.False(block.ReadsPageOverviews());
    }

    [Fact]
    public void BuildTopics_ReadsOverviews()
    {
        var workflow = new Workflow("root");

        Assert.True(new Block(BlockTypes.BuildTopicsForAllDocuments, new Section("Analysis", workflow), "topics").ReadsPageOverviews());
    }

    private static Block AiQuery(AiQueryProperties properties)
    {
        var workflow = new Workflow("root");

        return new Block(BlockTypes.AiQuery, new Section("Analysis", workflow), "query") { Properties = properties };
    }
}
