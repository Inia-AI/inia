using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class RestatedFigureCitationTests
{
    private const string BALANCE_SHEET =
        "Consolidated statement of financial position\nAs at 31 December 2025\n\n"
        + "                                   2025      2024\n"
        + "                                  £'000     £'000\n"
        + "Contingent consideration           2,506     1,230\n"
        + "Trade payables                     4,120     3,875\n";

    private const string CONTINGENT_CONSIDERATION_CLAIM = "Contingent consideration of £2.506m is due within a year.";

    [Theory]
    [InlineData("10", "Does the overdraft roll over?\n\n10. The £1.99m injection offset losses; was it cash?")]
    [InlineData("12", "1. First question.\n12) Free cash flow has been volatile.")]
    [InlineData("13", "  13. The reorganisation is expected to save money.")]
    public void ListItemNumber_NeverGetsAFactMarker(string listNumber, string content)
    {
        var fact = new Fact(listNumber) { IsNumerical = true };
        fact.Sources.Add(new BlockSource { BlockId = "block-a", CitedText = $"more than {listNumber}% of Group revenue" });

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(content, [fact], [CitationFor(fact)]);

        Assert.Equal(content, result);
    }

    [Fact]
    public void NumberThatOnlyStartsASentence_StillGetsItsMarker()
    {
        var fact = new Fact("10") { IsNumerical = true };
        fact.Sources.Add(new BlockSource { BlockId = "block-a", CitedText = "10 offices" });

        string result = BlockAnnotationProcessor.ApplyFactsWithIds("10 offices across the UK.", [fact], [CitationFor(fact)]);

        Assert.Equal("<fact id=\"c0\">10</fact> offices across the UK.", result);
    }

    [Fact]
    public void BareYear_SourceSaysSomethingElseAboutIt_IsNotInherited()
    {
        var year = new Fact("2025") { IsNumerical = true };
        year.Sources.Add(new UrlSource { Url = "https://example.org/ad-spend", CitedText = "UK advertising spend is forecast to grow in 2025 across digital channels." });
        var annotations = new BlockAnnotations();

        annotations.AddReferencedFacts([year], "What is the cost exposure under the Employment Rights Act 2025?");

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void BareYear_SourceSaysTheSameThingAboutIt_IsInherited()
    {
        var year = new Fact("2013") { IsNumerical = true };
        year.Sources.Add(new UrlSource { Url = "https://example.org/about", CitedText = "Northwind Capital Partners was founded in 2013 in Wilmington, Delaware." });
        var annotations = new BlockAnnotations();

        annotations.AddReferencedFacts([year], "The manager was founded in 2013.");

        Assert.Same(year, Assert.Single(annotations.Facts));
    }

    [Fact]
    public void SameValueWithTwoMeanings_InheritsTheOneTheClaimIsAbout()
    {
        Fact revenueItem = PageFact("£1.2m", "page-30", "Gross margin was lifted by a one-off high-margin revenue item of £1.2m in H2.");
        Fact overdraft = PageFact("£1.2m", "page-111", "The Group has an overdraft facility of £1.2m which is renewed annually.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(
            annotations,
            [revenueItem, overdraft],
            "Can you confirm whether the £1.2m overdraft facility is committed?");

        Fact inherited = Assert.Single(annotations.Facts);
        Assert.Equal("page-111", Assert.IsType<DocumentSource>(Assert.Single(inherited.Sources)).PageId);
    }

    [Fact]
    public void SameValueWithTwoMeanings_ClaimFavoursNeither_InheritsNeither()
    {
        Fact revenueItem = PageFact("£1.2m", "page-30", "Gross margin was lifted by a one-off high-margin revenue item of £1.2m in H2.");
        Fact overdraft = PageFact("£1.2m", "page-111", "The Group has an overdraft facility of £1.2m which is renewed annually.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [revenueItem, overdraft], "The figure to watch is £1.2m.");

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void SameValueFromTwoBlocks_InheritsTheOneTheClaimIsAbout()
    {
        Fact shareholding = BlockFact("4.7%", "block-overview", "Elena Marsh and The Marsh Family Foundation hold 4.7% of the shares.");
        Fact commissioning = BlockFact("4.7%", "block-industry", "Domestic television commissioning contracted 4.7% in 2025 as broadcasters cut budgets.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(
            annotations,
            [shareholding, commissioning],
            "Demand is divergent, with domestic TV commissioning contracting 4.7% in 2025.");

        Fact inherited = Assert.Single(annotations.Facts);
        Assert.Same(commissioning, inherited);
    }

    [Fact]
    public void SameValueFromOnePageAndTwoBlocks_BlocksTie_InheritsATiedBlockFact()
    {
        Fact shareholding = PageFact("4.7%", "page-40", "Directors hold 4.7% of the shares.");
        Fact fromOverview = BlockFact("4.7%", "block-overview", "Domestic commissioning contracted 4.7% in 2025.");
        Fact fromIndustry = BlockFact("4.7%", "block-industry", "Domestic commissioning contracted 4.7% in 2025.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(
            annotations,
            [shareholding, fromOverview, fromIndustry],
            "Domestic commissioning contracted 4.7% in 2025.");

        Assert.Same(fromOverview, Assert.Single(annotations.Facts));
    }

    [Fact]
    public void SameValueFromAFactWithNoSources_StillInheritsOne()
    {
        var unsourced = new Fact("4.7%") { IsNumerical = true };
        Fact commissioning = BlockFact("4.7%", "block-industry", "Domestic commissioning contracted 4.7% in 2025.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [unsourced, commissioning], "Commissioning contracted 4.7% in 2025.");

        Assert.Same(commissioning, Assert.Single(annotations.Facts));
    }

    [Fact]
    public void SameValueOnPagesThatShareAPage_ClaimFavoursNeither_InheritsTheFirst()
    {
        Fact performance = PageFact("30 June 2026", "page-16", "Unaudited results for the six months ended 30 June 2026.");
        performance.Sources.Add(new DocumentSource { PageId = "page-19", DocumentId = "doc-interim", DocumentName = "Interim.pdf", CitedText = "Balance sheet as at 30 June 2026." });
        Fact position = PageFact("30 June 2026", "page-19", "Balance sheet as at 30 June 2026.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [performance, position], "What is the order book at 30 June 2026?");

        Assert.Same(performance, Assert.Single(annotations.Facts));
    }

    [Fact]
    public void SameValueWithTheSameSource_IsInheritedOnce()
    {
        Fact fromStatement = PageFact("£41.5m", "page-72", "Revenue for the year was £41.5m.");
        Fact fromConversion = PageFact("£41.5m", "page-72", "Revenue for the year was £41.5m.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [fromStatement, fromConversion], "Revenue was £41.5m.");

        Assert.Single(annotations.Facts);
    }

    [Theory]
    [InlineData("-5.27%", "Free cash flow margin was negative 5.27% in FY2025.")]
    [InlineData("-$0.8m", "The segment made a loss of $0.8mn in the year.")]
    [InlineData("-42.38%", "Operating cash flow declined 42.38% year on year.")]
    [InlineData("-42.38%", "Operating cash flow fell by 42.38% year on year.")]
    [InlineData("-$1.7m", "Investing activity was an outflow of $1.7m.")]
    [InlineData("-5.27%", "Free cash flow margin was down by 5.27% in FY2025.")]
    [InlineData("-5.27%", "Free cash flow margin was lower by 5.27% in FY2025.")]
    [InlineData("-$1.7m", "Capital spending was reduced by $1.7m.")]
    public void FigureWithASignWord_MatchesItsSignedSource(string signedValue, string downstream)
    {
        Fact signed = PageFact(signedValue, "page-75", $"Change {signedValue}");
        var annotations = new BlockAnnotations();

        annotations.AddReferencedFacts([signed], downstream);

        Assert.Same(signed, Assert.Single(annotations.Facts));
    }

    [Theory]
    [InlineData("-5.27%", "Free cash flow margin was 5.27% in FY2025.")]
    [InlineData("-5.27%", "The margin declined to 5.27% in FY2025.")]
    [InlineData("5.27%", "Free cash flow margin was -5.27% in FY2025.")]
    public void FigureWithoutASignWord_NeverMatchesASourceOfTheOtherSign(string sourceValue, string downstream)
    {
        Fact source = PageFact(sourceValue, "page-75", $"Margin {sourceValue}");
        var annotations = new BlockAnnotations();

        annotations.AddReferencedFacts([source], downstream);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void PageThatSignsTheFigureInWords_IsInheritedByTheFigureWrittenWithAMinus()
    {
        BlockAnnotations page = PageFacts("Cash flow review\nOperating cash flow declined 42.38% year on year.\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(page.Facts.Where(fact => fact.IsNumerical), "Operating cash flow changed by -42.38% year on year.");

        Assert.Equal("42.38%", Assert.Single(downstream.Facts).Value);
    }

    [Fact]
    public void PageThatStatesTheFigureWithoutASignWord_IsNotInheritedByTheFigureWrittenWithAMinus()
    {
        BlockAnnotations page = PageFacts("Cash flow review\nOperating cash flow reached 42.38% of revenue.\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(page.Facts.Where(fact => fact.IsNumerical), "Operating cash flow changed by -42.38% year on year.");

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void SignWordOnALongerFigure_DoesNotSignTheFigureItEndsWith()
    {
        BlockAnnotations page = PageFacts("Cost review\nCosts declined 105.27% in the year, and the margin was 5.27%.\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(page.Facts.Where(fact => fact.Value == "5.27%"), "The margin moved by -5.27% in the year.");

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void FigureWithASignWord_GetsTheMarkerOfItsSignedFact()
    {
        Fact signed = PageFact("-5.27%", "page-75", "Change -5.27%");

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(
            "The margin was negative 5.27% in FY2025.",
            [signed],
            [CitationFor(signed)]);

        Assert.Equal("The margin was negative <fact id=\"c0\">5.27%</fact> in FY2025.", result);
    }

    [Fact]
    public void TableCellUnderAThousandsHeader_IsInheritedByTheSameFigureInMillions()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET);
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, CONTINGENT_CONSIDERATION_CLAIM);

        Fact inherited = Assert.Single(downstream.Facts);
        Assert.Equal("2,506", inherited.Value);
        Assert.IsType<DocumentSource>(Assert.Single(inherited.Sources));
    }

    [Theory]
    [InlineData("CHF'000", "Contingent consideration of CHF 2.506m is due within a year.")]
    [InlineData("£k", "Contingent consideration of £2.506m is due within a year.")]
    [InlineData("$bn", "Contingent consideration of $2,506bn is due within a year.")]
    public void TableCellUnderAnotherScaleHeader_IsInheritedByTheSameFigure(string header, string claim)
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET.Replace("£'000     £'000", $"{header}     {header}", StringComparison.Ordinal));
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, claim);

        Fact inherited = Assert.Single(downstream.Facts);
        Assert.Equal("2,506", inherited.Value);
    }

    [Fact]
    public void TableCellUnderABillionsHeader_IsNotReadAsMillions()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET.Replace("£'000     £'000", "$bn     $bn", StringComparison.Ordinal));
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, "Contingent consideration of $2,506m is due within a year.");

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void TableCellUnderAThousandsHeader_GetsTheMarkerOnTheFigureInMillions()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET);
        var downstream = new BlockAnnotations();
        downstream.AddReferencedFacts(statement.Facts, CONTINGENT_CONSIDERATION_CLAIM);
        Fact inherited = Assert.Single(downstream.Facts);

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(
            CONTINGENT_CONSIDERATION_CLAIM,
            downstream.Facts,
            [CitationFor(inherited)]);

        Assert.Equal("Contingent consideration of <fact id=\"c0\">£2.506m</fact> is due within a year.", result);
    }

    [Theory]
    [InlineData("Contingent consideration of £2.5m is due within a year.")]
    [InlineData("Contingent consideration of $2.506m is due within a year.")]
    public void TableCellUnderAThousandsHeader_RoundedOrOtherCurrencyFigure_IsNotInherited(string downstreamText)
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET);
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, downstreamText);

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void TableCellOnAPageWithTwoScales_IsNotScaled()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET + "\nSummary (£m)\nNet debt 2.9\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, CONTINGENT_CONSIDERATION_CLAIM);

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void ModelCitesTheTableRow_FigureInMillionsKeepsThePage()
    {
        Page page = StatementPage(BALANCE_SHEET);
        var claim = new Fact("Contingent consideration was £2.506m at the year end.");
        claim.Sources.Add(new DocumentSource { PageId = page.Id, DocumentId = page.DocumentId, CitedText = "Contingent consideration           2,506     1,230" });
        var annotations = new BlockAnnotations();
        annotations.AddFact(claim);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);

        Fact figure = Assert.Single(annotations.Facts, fact => fact.IsNumerical && fact.Value == "£2.506m");
        Assert.Equal(page.Id, Assert.IsType<DocumentSource>(Assert.Single(figure.Sources)).PageId);
    }

    [Fact]
    public void TableCellUnderAThousandsHeader_IsNotInheritedByTheSameDigitsWithoutTheScale()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET);
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, "The company had 2,506 employees and 1,230 customers.");

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void TableCellUnderAThousandsHeader_MarksTheAmountNotTheSameDigits()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET);
        const string CONTENT = "Contingent consideration was £2.506m; the group had 2,506 employees.";
        var downstream = new BlockAnnotations();
        downstream.AddReferencedFacts(statement.Facts, CONTENT);
        Fact inherited = Assert.Single(downstream.Facts);

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(CONTENT, downstream.Facts, [CitationFor(inherited)]);

        Assert.Equal("Contingent consideration was <fact id=\"c0\">£2.506m</fact>; the group had 2,506 employees.", result);
    }

    [Fact]
    public void TableCellUnderAUsDollarMillionsHeader_IsScaled()
    {
        BlockAnnotations statement = PageFacts("Segment results (US$m)\nNorth America revenue         1,234.5\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, "North America revenue was $1,234.5m.");

        Assert.Equal("1,234.5", Assert.Single(downstream.Facts).Value);
    }

    [Fact]
    public void SameValueWithTwoMeanings_RestatedInAnotherForm_KeepsTheFirstAsBefore()
    {
        Fact revenueItem = PageFact("£1.2m", "page-30", "Gross margin was lifted by a one-off high-margin revenue item of £1.2m in H2.");
        Fact overdraft = PageFact("£1.2m", "page-111", "The Group has an overdraft facility of £1.2m which is renewed annually.");
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [revenueItem, overdraft], "The overdraft of £1.2 million is committed.");

        Assert.Same(revenueItem, Assert.Single(annotations.Facts));
    }

    private static Page StatementPage(string pageText)
    {
        var document = new Document(
            new FileMetadata("IniaTest", "annual-report.pdf", pageText.Length, StorageProvider.Database, "annual-report"),
            MediaType.TextPlain);
        return new Page(document, 74, 74) { RawText = pageText };
    }

    [Theory]
    [InlineData("1,250", "Net cash was -1,250 at the year end.", "Net cash was -<fact id=\"c0\">1,250</fact> at the year end.")]
    [InlineData("5.27%", "The margin was \u22125.27% in FY2025.", "The margin was \u2212<fact id=\"c0\">5.27%</fact> in FY2025.")]
    public void FigureWhoseExtractedValueDropsItsMinus_KeepsItsMarker(string extractedValue, string content, string expected)
    {
        Fact fact = PageFact(extractedValue, "page-75", content);

        string result = BlockAnnotationProcessor.ApplyFactsWithIds(content, [fact], [CitationFor(fact)]);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BareYear_WholePageSourceMentionsTheClaimWordsElsewhere_IsNotInherited()
    {
        string page = "Directors' report for the year ended 31 December 2025, approved by the board.\n"
            + "Revenue and margin were broadly stable across the period. "
            + "Employment costs rose because of wage inflation and higher headcount in production.";
        var year = new Fact("2025") { IsNumerical = true };
        year.Sources.Add(new DocumentSource { PageId = "page-12", DocumentId = "doc-annual-report", CitedText = page });
        var annotations = new BlockAnnotations();

        annotations.AddReferencedFacts([year], "What is the cost exposure under the Employment Rights Act 2025?");

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void BareNumberThatIsNoAmount_OnAThousandsPage_IsNotScaled()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET + "Average number of employees            412       398\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, "The adviser's fee was £412k.");

        Assert.Empty(downstream.Facts);
    }

    [Fact]
    public void YearShapedTableCellUnderAThousandsHeader_IsScaled()
    {
        BlockAnnotations statement = PageFacts(BALANCE_SHEET + "Deferred income                    2,000     1,750\n");
        var downstream = new BlockAnnotations();

        downstream.AddReferencedFacts(statement.Facts, "Deferred income was £2.000m at the year end.");

        Assert.Equal("2,000", Assert.Single(downstream.Facts).Value);
    }

    private static BlockAnnotations PageFacts(string pageText)
    {
        Page page = StatementPage(pageText);
        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.CreateFactsFromPages(new Dictionary<int, Page> { [74] = page }, annotations, shouldUseOverviews: false);
        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);
        return annotations;
    }

    private static Fact PageFact(string value, string pageId, string citedText)
    {
        var fact = new Fact(value) { IsNumerical = true };
        fact.Sources.Add(new DocumentSource { PageId = pageId, DocumentId = "doc-annual-report", DocumentName = "Annual Report.pdf", CitedText = citedText });
        return fact;
    }

    private static Fact BlockFact(string value, string blockId, string citedText)
    {
        var fact = new Fact(value) { IsNumerical = true };
        fact.Sources.Add(new BlockSource { BlockId = blockId, CitedText = citedText });
        return fact;
    }

    private static Citation CitationFor(Fact fact)
    {
        return new Citation { Id = "c0", IsNumerical = true, Sources = [.. fact.Sources] };
    }
}
