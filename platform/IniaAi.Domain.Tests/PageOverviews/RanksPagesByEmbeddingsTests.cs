using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.PageOverviews;

public class RanksPagesByEmbeddingsTests
{
    private static readonly Dictionary<RagTypes, bool> _ranksByEmbeddings = new()
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
        [RagTypes.RagAgent] = false,
    };

    [Fact]
    public void EveryRetrievalType_SaysWhetherItRanksPagesByEmbeddings()
    {
        foreach (RagTypes type in Enum.GetValues<RagTypes>())
        {
            Assert.True(_ranksByEmbeddings.TryGetValue(type, out bool expected), $"{type} is not classified here.");
            Assert.Equal(expected, new RagSettings { Type = type }.RanksPagesByEmbeddings());
        }
    }

    [Fact]
    public void AKeywordSearch_RanksByEmbeddings_WhenItCarriesFilterExpressions()
    {
        Assert.True(new RagSettings { Type = RagTypes.KeywordBased, SmartFilterExpressions = ["revenue"] }.RanksPagesByEmbeddings());
    }
}
