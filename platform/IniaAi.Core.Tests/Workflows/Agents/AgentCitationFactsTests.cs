using IniaAi.Common;
using IniaAi.Core.Workflows.Agents;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;

namespace IniaAi.Core.Tests.Workflows.Agents;

public class AgentCitationFactsTests
{
    private const string STATEMENT_LINE = "Total assets                36       4,027,553,656       3,318,824,587";
    private const string OTHER_LINE = "Property, plant and equipment        2,000,148,330       1,533,863,702";

    private static Document CreateDocument(string fileName)
    {
        var metadata = new FileMetadata("org-cite-tests", fileName, new byte[] { 1 });
        return new Document(metadata, Utils.DetermineMediaTypeFromExtension(Path.GetExtension(fileName)));
    }

    private static Page CreatePage(Document document, int pageNumber, int filePageNumber, string text)
    {
        return new Page(document, pageNumber, filePageNumber) { Text = text };
    }

    private static string OutputCiting(string document, int? page, string asPrinted)
    {
        string pageFragment = page is null ? string.Empty : $"\"page\":{page},";
        return "{\"fields\":{},\"citations\":{\"f\":{\"document\":\"" + document + "\","
            + pageFragment + "\"as_printed\":\"" + asPrinted + "\"}}}";
    }

    [Fact]
    public void Build_QuoteOnTheClaimedFilePage_ResolvesToThatPage()
    {
        Document document = CreateDocument("ar.pdf");
        Page other = CreatePage(document, 1, 1, OTHER_LINE);
        Page cited = CreatePage(document, 2, 2, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("ar.pdf", 2, "Total assets 4,027,553,656 3,318,824,587"),
            staged,
            [other, cited]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.Equal(cited.Id, source.PageId);
        Assert.Equal(document.Id, source.DocumentId);
        Assert.Equal(2, source.FilePageNumber);
        Assert.False(source.Unverified);
    }

    [Fact]
    public void Build_GroundsNumericalSubFactsInTheQuote()
    {
        Document document = CreateDocument("ar.pdf");
        Page cited = CreatePage(document, 1, 1, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("ar.pdf", 1, "Total assets 4,027,553,656 3,318,824,587"),
            staged,
            [cited]);

        Assert.Contains(facts, static fact => fact.IsNumerical && fact.Value == "4,027,553,656");
        Assert.Contains(facts, static fact => fact.IsNumerical && fact.Value == "3,318,824,587");
    }

    [Fact]
    public void Build_QuoteOnADifferentPageThanClaimed_ResolvesToThePageThatCarriesIt()
    {
        Document document = CreateDocument("ar.pdf");
        Page other = CreatePage(document, 1, 1, OTHER_LINE);
        Page carrying = CreatePage(document, 2, 2, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("ar.pdf", 1, "Total assets 4,027,553,656 3,318,824,587"),
            staged,
            [other, carrying]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.Equal(carrying.Id, source.PageId);
        Assert.False(source.Unverified);
    }

    [Fact]
    public void Build_QuoteOnSeveralPages_PrefersTheClaimedFilePage()
    {
        Document document = CreateDocument("ar.pdf");
        Page first = CreatePage(document, 1, 1, STATEMENT_LINE);
        Page claimed = CreatePage(document, 3, 3, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("ar.pdf", 3, "Total assets 4,027,553,656 3,318,824,587"),
            staged,
            [first, claimed]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.Equal(claimed.Id, source.PageId);
    }

    [Fact]
    public void Build_ClaimWithoutAPage_ResolvesBySearchAcrossThePages()
    {
        Document document = CreateDocument("statements.docx");
        Page other = CreatePage(document, 1, 1, OTHER_LINE);
        Page carrying = CreatePage(document, 2, 2, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["statements.docx"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("statements.docx", null, "Total assets 4,027,553,656 3,318,824,587"),
            staged,
            [other, carrying]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.Equal(carrying.Id, source.PageId);
        Assert.False(source.Unverified);
    }

    [Fact]
    public void Build_QuoteFoundNowhere_KeepsTheSourceAsUnverified()
    {
        Document document = CreateDocument("ar.pdf");
        Page page = CreatePage(document, 1, 1, OTHER_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("ar.pdf", 5, "Impairment of right-of-use assets 19,800,000"),
            staged,
            [page]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.True(source.Unverified);
        Assert.Equal(string.Empty, source.PageId);
        Assert.Equal(document.Id, source.DocumentId);
        Assert.Equal(5, source.FilePageNumber);
        Assert.Contains(facts, static fact => fact.IsNumerical && fact.Value == "19,800,000");
    }

    [Fact]
    public void Build_UnknownDocumentName_KeepsTheClaimAsUnverifiedUnderTheClaimedName()
    {
        Document document = CreateDocument("ar.pdf");
        Page page = CreatePage(document, 1, 1, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };

        List<Fact> facts = AgentCitationFacts.Build(
            OutputCiting("never-staged.pdf", 1, "Total assets 4,027,553,656 3,318,824,587"),
            staged,
            [page]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.True(source.Unverified);
        Assert.Equal(string.Empty, source.DocumentId);
        Assert.Equal("never-staged.pdf", source.DocumentName);
    }

    [Fact]
    public void Build_SingleStagedDocument_AcceptsAClaimThatNamesNoDocument()
    {
        Document document = CreateDocument("ar.pdf");
        Page page = CreatePage(document, 1, 1, STATEMENT_LINE);
        var staged = new Dictionary<string, Document> { ["ar.pdf"] = document };
        const string output = """
            {"citations":{"f":{"page":1,"as_printed":"Total assets 4,027,553,656 3,318,824,587"}}}
            """;

        List<Fact> facts = AgentCitationFacts.Build(output, staged, [page]);

        Fact broad = Assert.Single(facts, static fact => !fact.IsNumerical);
        DocumentSource source = Assert.IsType<DocumentSource>(Assert.Single(broad.Sources));
        Assert.Equal(page.Id, source.PageId);
    }

    [Fact]
    public void UnverifiedSources_FromDistinctUnknownDocuments_DoNotCollapseWhenAddedToOneFact()
    {
        var fact = new Fact("shared value") { IsNumerical = false };
        fact.AddSource(new DocumentSource
        {
            CitedText = "Same quote 1,000",
            DocumentName = "first-unknown.pdf",
            Unverified = true,
        });
        fact.AddSource(new DocumentSource
        {
            CitedText = "Same quote 1,000",
            DocumentName = "second-unknown.pdf",
            Unverified = true,
        });

        Assert.Equal(2, fact.Sources.Count);
    }

    [Fact]
    public void UnverifiedSources_WithDistinctQuotes_DoNotCollapseWhenAddedToOneFact()
    {
        var fact = new Fact("shared value") { IsNumerical = false };
        fact.AddSource(new DocumentSource
        {
            CitedText = "First quote 1,000",
            DocumentId = "doc-1",
            Unverified = true,
        });
        fact.AddSource(new DocumentSource
        {
            CitedText = "Second quote 2,000",
            DocumentId = "doc-1",
            Unverified = true,
        });

        Assert.Equal(2, fact.Sources.Count);
    }
}
