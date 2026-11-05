using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class CitationFactBuilderTests
{
    private const string VALUE_PHRASE = "$183,866 million";
    private const int LONG_FILLER_LENGTH = BlockAnnotationProcessor.MAX_CITED_TEXT_LENGTH * 5;

    private static (string Claim, IReadOnlyList<UrlSource> Sources) Claim(string claim, string citedText)
    {
        return (claim, [new UrlSource { Url = "https://example.com", CitedText = citedText }]);
    }

    [Fact]
    public void BuildFacts_LongCitedText_BroadFactKeepsFullText()
    {
        string filler = new('x', LONG_FILLER_LENGTH);
        string citedText = $"{filler} fund net assets were {VALUE_PHRASE} as reported {filler}";

        List<Fact> facts = CitationFactBuilder.BuildFacts(
            [Claim($"Fund net assets were {VALUE_PHRASE}.", citedText)]);

        Fact broad = Assert.Single(facts, f => !f.IsNumerical);
        BaseSource source = Assert.Single(broad.Sources);

        Assert.Equal(citedText, source.CitedText);
    }

    [Fact]
    public void BuildFacts_NumericalSubFact_MirrorsParentCitedText()
    {
        string citedText = $"The fund had net assets of {VALUE_PHRASE} as of April 30, 2025";

        List<Fact> facts = CitationFactBuilder.BuildFacts(
            [Claim($"Fund net assets were {VALUE_PHRASE}.", citedText)]);

        Fact numerical = Assert.Single(facts, f => f.IsNumerical);
        BaseSource source = Assert.Single(numerical.Sources);

        Assert.Equal(citedText, source.CitedText);
    }

    [Fact]
    public void BuildFacts_LongCitedText_NumericalSubFactKeepsFullText()
    {
        string filler = new('x', LONG_FILLER_LENGTH);
        string citedText = $"{filler} fund net assets were {VALUE_PHRASE} as reported {filler}";

        List<Fact> facts = CitationFactBuilder.BuildFacts(
            [Claim($"Fund net assets were {VALUE_PHRASE}.", citedText)]);

        Fact numerical = Assert.Single(facts, f => f.IsNumerical);
        BaseSource source = Assert.Single(numerical.Sources);

        Assert.Equal(citedText, source.CitedText);
    }

    [Fact]
    public void BuildFacts_DateStatedInProse_GroundedByIsoDateInExcerpt()
    {
        const string LINKEDIN_EXCERPT =
            "company name: Lakeview Investment Group & Trading Company LLC start date: 2010-01-01 " +
            "start date year: 2010 start date month: 1 end date: 2011-05-01 end date year: 2011 " +
            "end date month: 5 Title: Vice President";

        List<Fact> facts = CitationFactBuilder.BuildFacts(
            [Claim("Vice President from January 2010 through May 2011.", LINKEDIN_EXCERPT)]);

        Fact promotion = Assert.Single(facts, f => f.IsNumerical && f.Value == "May 2011");
        Fact start = Assert.Single(facts, f => f.IsNumerical && f.Value == "January 2010");

        Assert.Equal(LINKEDIN_EXCERPT, Assert.Single(promotion.Sources).CitedText);
        Assert.Equal(LINKEDIN_EXCERPT, Assert.Single(start.Sources).CitedText);
    }

    [Fact]
    public void BuildFacts_DateStatedInProse_NotGroundedByAnotherMonth()
    {
        List<Fact> facts = CitationFactBuilder.BuildFacts(
            [Claim("Partner from May 2011 onward.", "start date: 2011-06-01 Title: Partner")]);

        Assert.DoesNotContain(facts, f => f.IsNumerical && f.Value == "May 2011");
    }

    [Fact]
    public void BuildFacts_ValueStatedOnlyInTheFirstOfSeveralExcerptsOfOneUrl_IsGrounded()
    {
        const string URL = "https://linkedin.com/in/example";
        const string ANALYST_EXCERPT = "start date: 2005-06-01 end date: 2009-12-01 Title: Analyst";
        const string PARTNER_EXCERPT = "start date: 2011-05-01 Title: Partner / Managing Director";
        List<UrlSource> sources =
        [
            new UrlSource { Url = URL, CitedText = ANALYST_EXCERPT },
            new UrlSource { Url = URL, CitedText = PARTNER_EXCERPT },
        ];

        List<Fact> facts = CitationFactBuilder.BuildFacts(
            [("Analyst from June 2005; Partner from May 2011.", (IReadOnlyList<UrlSource>)sources)]);

        Fact analyst = Assert.Single(facts, f => f.IsNumerical && f.Value == "June 2005");
        Fact partner = Assert.Single(facts, f => f.IsNumerical && f.Value == "May 2011");

        Assert.Equal(ANALYST_EXCERPT, Assert.Single(analyst.Sources).CitedText);
        Assert.Equal(PARTNER_EXCERPT, Assert.Single(partner.Sources).CitedText);
        Assert.Equal(ANALYST_EXCERPT, sources[0].CitedText);
    }
}
