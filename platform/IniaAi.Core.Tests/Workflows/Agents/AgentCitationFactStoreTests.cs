using IniaAi.Common;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Tests.Workflows.Agents;

/// <summary>
/// The durable form of an agent leg's citation facts: a best-attempt restore reads
/// these back off the attempt row, so the round trip must keep a resolved citation's page link
/// and an unverified citation's flag exactly as the run resolved them.
/// </summary>
public class AgentCitationFactStoreTests
{
    private const string STATEMENT_LINE = "Total assets                36       4,027,553,656       3,318,824,587";
    private const string RESOLVED_QUOTE = "Total assets 4,027,553,656 3,318,824,587";
    private const string UNVERIFIABLE_QUOTE = "Impairment of right-of-use assets 19,800,000";

    private static Document CreateDocument(string fileName)
    {
        var metadata = new FileMetadata("org-cite-store-tests", fileName, new byte[] { 1 });
        return new Document(metadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(fileName)));
    }

    private static Page CreatePage(Document document, int pageNumber, int filePageNumber, string text)
    {
        return new Page(document, pageNumber, filePageNumber) { Text = text };
    }

    /// <summary>One resolved citation (its quote is on the page) and one that is found nowhere.</summary>
    private static List<Fact> BuildResolvedAndUnverifiedFacts(Document document, Page cited)
    {
        string output =
            "{\"fields\":{},\"citations\":{"
            + "\"a\":{\"document\":\"ar.pdf\",\"page\":" + cited.FilePageNumber + ",\"as_printed\":\"" + RESOLVED_QUOTE + "\"},"
            + "\"b\":{\"document\":\"ar.pdf\",\"as_printed\":\"" + UNVERIFIABLE_QUOTE + "\"}}}";

        return AgentCitationFacts.Build(
            output, new Dictionary<string, Document> { ["ar.pdf"] = document }, [cited]);
    }

    private static List<DocumentSource> DocumentSourcesOf(IEnumerable<Fact> facts)
    {
        return [.. facts.SelectMany(static fact => fact.Sources).OfType<DocumentSource>()];
    }

    [Fact]
    public void RoundTrip_KeepsThePageLinkOfAResolvedCitation()
    {
        Document document = CreateDocument("ar.pdf");
        Page cited = CreatePage(document, 1, 1, STATEMENT_LINE);
        List<Fact> facts = BuildResolvedAndUnverifiedFacts(document, cited);

        List<Fact> restored = AgentCitationFactStore.Deserialize(AgentCitationFactStore.Serialize(facts));

        // The broad fact and its grounded numerical sub-facts each carry the same source.
        List<DocumentSource> resolved =
            [.. DocumentSourcesOf(restored).Where(source => source.CitedText == RESOLVED_QUOTE)];
        Assert.NotEmpty(resolved);
        Assert.All(resolved, source =>
        {
            Assert.Equal(cited.Id, source.PageId);
            Assert.Equal(document.Id, source.DocumentId);
            Assert.Equal(1, source.FilePageNumber);
            Assert.False(source.Unverified);
        });
    }

    [Fact]
    public void RoundTrip_KeepsAnUnverifiedCitationUnverified()
    {
        Document document = CreateDocument("ar.pdf");
        Page cited = CreatePage(document, 1, 1, STATEMENT_LINE);
        List<Fact> facts = BuildResolvedAndUnverifiedFacts(document, cited);

        List<Fact> restored = AgentCitationFactStore.Deserialize(AgentCitationFactStore.Serialize(facts));

        List<DocumentSource> unverified =
            [.. DocumentSourcesOf(restored).Where(source => source.CitedText == UNVERIFIABLE_QUOTE)];
        Assert.NotEmpty(unverified);
        Assert.All(unverified, source =>
        {
            Assert.True(source.Unverified);
            Assert.Equal(string.Empty, source.PageId);
        });
    }

    [Fact]
    public void RoundTrip_KeepsTheFactSetAndItsNumericalGrounding()
    {
        Document document = CreateDocument("ar.pdf");
        Page cited = CreatePage(document, 1, 1, STATEMENT_LINE);
        List<Fact> facts = BuildResolvedAndUnverifiedFacts(document, cited);

        List<Fact> restored = AgentCitationFactStore.Deserialize(AgentCitationFactStore.Serialize(facts));

        Assert.Equal(facts.Count, restored.Count);
        Assert.Contains(restored, static fact => fact.IsNumerical && fact.Value == "4,027,553,656");
    }

    [Fact]
    public void AddingTheDeserializedFactsTwice_DoesNotDuplicateThem()
    {
        Document document = CreateDocument("ar.pdf");
        Page cited = CreatePage(document, 1, 1, STATEMENT_LINE);
        string json = AgentCitationFactStore.Serialize(BuildResolvedAndUnverifiedFacts(document, cited));

        var annotations = new BlockAnnotations();
        annotations.AddFacts(AgentCitationFactStore.Deserialize(json));
        int afterFirstRestore = annotations.Facts.Count;
        annotations.AddFacts(AgentCitationFactStore.Deserialize(json));

        Assert.Equal(afterFirstRestore, annotations.Facts.Count);
    }
}
