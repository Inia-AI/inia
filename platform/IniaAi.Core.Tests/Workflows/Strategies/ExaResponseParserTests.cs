using IniaAi.Core.Dto.Exa.Search;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class ExaResponseParserTests
{
    private static ExaResultItem Result(
        string url,
        string? title = null,
        string? text = null,
        object? summary = null,
        string[]? highlights = null,
        ExaResultItem[]? subpages = null)
    {
        return new ExaResultItem
        {
            Id = url,
            Url = url,
            Title = title,
            Text = text,
            Summary = summary,
            Highlights = highlights,
            Subpages = subpages,
        };
    }

    private static IEnumerable<Fact> BroadFacts(IEnumerable<Fact> facts)
    {
        return facts.Where(f => !f.IsNumerical);
    }

    private static IEnumerable<Fact> NumericalFacts(IEnumerable<Fact> facts)
    {
        return facts.Where(f => f.IsNumerical);
    }

    [Fact]
    public void BuildFacts_NullResults_ReturnsEmpty()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(null!);

        Assert.Empty(facts);
    }

    [Fact]
    public void BuildFacts_EmptyResults_ReturnsEmpty()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts([]);

        Assert.Empty(facts);
    }

    [Fact]
    public void BuildFacts_ResultWithText_EmitsBroadFactWithUrlSource()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: "A plain prose claim about something.")]);

        Fact broad = Assert.Single(BroadFacts(facts));
        Assert.Equal("A plain prose claim about something.", broad.Value);
        UrlSource source = Assert.IsType<UrlSource>(Assert.Single(broad.Sources));
        Assert.Equal("https://example.com/a", source.Url);
        Assert.Contains("plain prose claim", source.CitedText);
    }

    [Fact]
    public void BuildFacts_ResultWithEmptyUrl_Skipped()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("", text: "Has text but no url.")]);

        Assert.Empty(facts);
    }

    [Fact]
    public void BuildFacts_ResultWithWhitespaceContent_Skipped()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: "   ")]);

        Assert.Empty(facts);
    }

    [Fact]
    public void BuildFacts_NumericalValueInText_EmitsGroundedNumericalSubFact()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: "The deal was valued at $10.7 billion in cash.")]);

        Fact numerical = Assert.Single(NumericalFacts(facts));
        Assert.Equal("$10.7 billion", numerical.Value);
        Assert.True(numerical.IsNumerical);
        UrlSource source = Assert.IsType<UrlSource>(Assert.Single(numerical.Sources));
        Assert.Equal("https://example.com/a", source.Url);
    }

    [Fact]
    public void BuildFacts_NoNumericalValues_EmitsBroadFactOnly()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: "Prose with no figures at all.")]);

        _ = Assert.Single(BroadFacts(facts));
        Assert.Empty(NumericalFacts(facts));
    }

    [Fact]
    public void BuildFacts_TextEmptyStringSummaryPresent_UsesSummaryAsFallback()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: null, summary: "Revenue reached $5.4 billion this year.")]);

        Fact broad = Assert.Single(BroadFacts(facts));
        Assert.Equal("Revenue reached $5.4 billion this year.", broad.Value);
        Fact numerical = Assert.Single(NumericalFacts(facts));
        Assert.Equal("$5.4 billion", numerical.Value);
        Assert.Equal("https://example.com/a", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public void BuildFacts_TextAndSummaryPresent_SummaryStillCited()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result(
                "https://example.com/a",
                text: "Prose body with no figures.",
                summary: "Summary states revenue of $99 million.")]);

        Fact numerical = Assert.Single(NumericalFacts(facts));
        Assert.Equal("$99 million", numerical.Value);
        Assert.Equal("https://example.com/a", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public void BuildFacts_StructuredSummaryWithFigure_IsCited()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: null, summary: new { revenue = "$7.2 billion" })]);

        Fact numerical = Assert.Single(NumericalFacts(facts));
        Assert.Equal("$7.2 billion", numerical.Value);
        Assert.Equal("https://example.com/a", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public void BuildFacts_HighlightContainsValue_FoldedInAsGrounding()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result(
                "https://example.com/a",
                text: "Prose body without the key figure.",
                highlights: ["The acquisition closed at $42.5 billion."])]);

        Fact numerical = Assert.Single(NumericalFacts(facts));
        Assert.Equal("$42.5 billion", numerical.Value);
        Assert.Equal("https://example.com/a", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public void BuildFacts_HighlightOnlyResult_StillCited()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result(
                "https://example.com/a",
                text: null,
                highlights: ["Total assets of $12.3 billion."])]);

        Fact numerical = Assert.Single(NumericalFacts(facts));
        Assert.Equal("$12.3 billion", numerical.Value);
        Assert.Equal("https://example.com/a", ((UrlSource)numerical.Sources[0]).Url);
    }

    [Fact]
    public void BuildFacts_MultipleResults_EmitsBroadFactPerResult()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
        [
            Result("https://example.com/a", text: "First claim."),
            Result("https://example.com/b", text: "Second claim."),
        ]);

        List<Fact> broad = [.. BroadFacts(facts)];
        Assert.Equal(2, broad.Count);
        Assert.Contains(broad, f => f.Value == "First claim.");
        Assert.Contains(broad, f => f.Value == "Second claim.");
    }

    [Fact]
    public void BuildFacts_TreatsInputAsFlat_DoesNotRecurseIntoSubpages()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result(
                "https://main.com",
                text: "Main page prose without figures.",
                subpages: [Result("https://sub.com", text: "Net income was $3.1 billion.")])]);

        Assert.Empty(NumericalFacts(facts));
        Assert.DoesNotContain(
            facts.SelectMany(f => f.Sources).Cast<UrlSource>(),
            s => s.Url == "https://sub.com");
    }

    [Fact]
    public void BuildFacts_UngroundedValue_NoNumericalSubFact()
    {
        List<Fact> facts = ExaResponseParser.BuildFacts(
            [Result("https://example.com/a", text: "Prose with no numbers.", highlights: ["Also no numbers."])]);

        Assert.Empty(NumericalFacts(facts));
    }
}
