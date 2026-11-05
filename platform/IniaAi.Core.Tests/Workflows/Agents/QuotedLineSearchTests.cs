using IniaAi.Core.Workflows.Agents;

namespace IniaAi.Core.Tests.Workflows.Agents;

public class QuotedLineSearchTests
{
    private const string PAGE_TEXT =
        "  CURRENT ASSETS:\n"
        + "    Trade receivables                18       37,397,885       37,575,319\n"
        + "    Cash and cash equivalents        22      204,454,491      326,818,129\n"
        + "  Total current assets                       860,082,705      731,016,239\n"
        + "    Issuance premiums deducted from costs        23     (1,514,705)     (1,514,705)";

    [Fact]
    public void Matches_QuoteOmittingTheNoteColumn_FindsTheLine()
    {
        Assert.True(QuotedLineSearch.Matches(PAGE_TEXT, "Trade receivables 37,397,885 37,575,319"));
    }

    [Fact]
    public void Matches_ThousandsSeparatorsStripped_FindsTheLine()
    {
        Assert.True(QuotedLineSearch.Matches(PAGE_TEXT, "Total current assets 860082705 731016239"));
    }

    [Fact]
    public void Matches_BracketNegativeRenderedAsMinus_FindsTheLine()
    {
        Assert.True(QuotedLineSearch.Matches(
            PAGE_TEXT, "Issuance premiums deducted from costs -1,514,705 -1,514,705"));
    }

    [Fact]
    public void Matches_CurrencySymbolAddedToAFigure_FindsTheLine()
    {
        Assert.True(QuotedLineSearch.Matches(PAGE_TEXT, "Total current assets €860,082,705 €731,016,239"));
    }

    [Fact]
    public void Matches_SignDroppedFromAPrintedNegative_RejectsTheQuote()
    {
        Assert.False(QuotedLineSearch.Matches(
            PAGE_TEXT, "Issuance premiums deducted from costs 1,514,705 1,514,705"));
    }

    [Fact]
    public void Matches_MinusInventedOnAPrintedPositive_RejectsTheQuote()
    {
        Assert.False(QuotedLineSearch.Matches(PAGE_TEXT, "Trade receivables -37,397,885 37,575,319"));
    }

    [Fact]
    public void Matches_QuoteStitchedFromTwoPrintedLines_RejectsTheQuote()
    {
        Assert.False(QuotedLineSearch.Matches(
            PAGE_TEXT, "Trade receivables 37,397,885 Cash and cash equivalents 204,454,491"));
    }

    [Fact]
    public void Matches_FigureNotPrintedOnAnyLine_RejectsTheQuote()
    {
        Assert.False(QuotedLineSearch.Matches(PAGE_TEXT, "Trade receivables 37,397,886"));
    }

    [Fact]
    public void Matches_BareFigureQuotedFromAParenthesisedNegative_RejectsTheSubstringSignFlip()
    {
        Assert.False(QuotedLineSearch.Matches("Row 23 (1,514,705)", "1,514,705"));
    }

    [Fact]
    public void Matches_WrappedLineWhoseFiguresAgreeInSign_StillMatchesAsASubstring()
    {
        Assert.True(QuotedLineSearch.Matches(
            "Total current\nassets     860 082 705", "Total current assets 860 082 705"));
    }

    [Fact]
    public void Matches_EnDashNegative_CarriesTheSign()
    {
        Assert.True(QuotedLineSearch.Matches("Costs –1,514,705", "Costs -1514705"));
        Assert.False(QuotedLineSearch.Matches("Costs –1,514,705", "Costs 1514705"));
        Assert.False(QuotedLineSearch.Matches("Costs 1,514,705", "Costs –1,514,705"));
    }

    [Fact]
    public void Matches_LedgerTrailingMinus_ReadsAsTheFiguresSign()
    {
        Assert.True(QuotedLineSearch.Matches("Costs 2,431-", "Costs -2431"));
        Assert.False(QuotedLineSearch.Matches("Costs 2,431-", "Costs 2431"));
    }

    [Fact]
    public void Matches_MissingPageTextOrBlankQuote_RejectsTheQuote()
    {
        Assert.False(QuotedLineSearch.Matches(null, "Trade receivables 37,397,885"));
        Assert.False(QuotedLineSearch.Matches(PAGE_TEXT, "   "));
    }
}
