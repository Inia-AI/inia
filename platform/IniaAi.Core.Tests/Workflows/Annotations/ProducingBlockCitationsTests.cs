using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class ProducingBlockCitationsTests
{
    private const string STATEMENT_BLOCK_ID = "block-statement";
    private const string CONVERSION_BLOCK_ID = "block-usd-conversion";
    private const string DRAFT_BLOCK_ID = "block-draft";
    private const string ANNUAL_REPORT = "Annual Report FY2025.pdf";
    private const string ANNUAL_REPORT_PAGE_ID = "page-annual-report-72";
    private const string USD_CONVERSION = "USD Conversion";
    private const string CONVERSION_OUTPUT = "Revenue was £41.5m, or $55.1m at the 1.3270 rate.";
    private const string REVENUE_CLAIM = "Revenue reached $55.1m.";
    private const string DOLLAR_REVENUE_OUTPUT = "Revenue was $55.1m.";

    [Theory]
    [InlineData("$55.1m", CONVERSION_OUTPUT, "Revenue reached $55.1m in FY2025, the conversion shows.")]
    [InlineData("23.0%", "The largest customer accounted for 23.0% of revenue.", "One customer accounted for 23.0% of Group revenue.")]
    [InlineData("0.96x", "Interest cover fell to 0.96x on total finance costs.", "Interest cover fell to 0.96x in FY2025.")]
    [InlineData("November 2025", "The Chief Financial Officer joined in November 2025.", "A new Chief Financial Officer joined in November 2025.")]
    [InlineData("2024-07", "- Event Date 2024-07: the Group wound down its video marketing division.", "Event Date 2024-07: Harbour wound down its video marketing division.")]
    public void FigureWithNoFact_InputBlockStatesIt_CitesThatBlock(string figure, string inputOutput, string output)
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, inputOutput);
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, output, [conversion]);

        Fact fact = Assert.Single(annotations.Facts);
        Assert.Equal(figure, fact.Value);
        Assert.True(fact.IsNumerical);
        BlockSource source = Assert.IsType<BlockSource>(Assert.Single(fact.Sources));
        Assert.Equal(CONVERSION_BLOCK_ID, source.BlockId);
        Assert.Equal(USD_CONVERSION, source.BlockTitle);
        Assert.Contains(figure, source.CitedText);
    }

    [Fact]
    public void ProducerWithABlankOutputTitle_IsCitedByItsName()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, CONVERSION_OUTPUT);
        conversion.ExecutionState = new BlockExecutionState(conversion) { OutputTitle = string.Empty };
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, REVENUE_CLAIM, [conversion]);

        BlockSource source = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(annotations.Facts).Sources));
        Assert.Equal(USD_CONVERSION, source.BlockTitle);
    }

    [Fact]
    public void FigureWhoseOnlyFactIsARoundedValue_IsCitedToItsProducer()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, "Total revenues declined 42.38% to $17.5 million.");
        var annotations = new BlockAnnotations();
        var rounded = new Fact("42.4%") { IsNumerical = true };
        rounded.Sources.Add(Page("Revenue fell 42.4% in the half."));
        annotations.AddFact(rounded);

        ProducingBlockCitations.CiteProducingBlocks(annotations, "Revenue fell 42.38% in the half-year.", [conversion]);

        Fact cited = Assert.Single(annotations.Facts, fact => fact.Value == "42.38%");
        Assert.Equal(CONVERSION_BLOCK_ID, Assert.IsType<BlockSource>(Assert.Single(cited.Sources)).BlockId);
    }

    [Fact]
    public void FigureWhoseFactCarriesTheSignItsWordsState_GetsNoSecondFact()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, "Total revenues declined 42.38% to $17.5 million.");
        var annotations = new BlockAnnotations();
        var signed = new Fact("-42.38%") { IsNumerical = true };
        signed.Sources.Add(Page("Revenue -42.38%"));
        annotations.AddFact(signed);

        ProducingBlockCitations.CiteProducingBlocks(annotations, "Revenue fell 42.38% in the half-year.", [conversion]);

        Assert.Equal("-42.38%", Assert.Single(annotations.Facts).Value);
    }

    [Fact]
    public void FigureThatAlreadyHasAFact_KeepsOnlyItsOwnSources()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, "Revenue was £41.5m, or $55.1m.");
        var annotations = new BlockAnnotations();
        var pageFact = new Fact("$55.1m") { IsNumerical = true };
        pageFact.Sources.Add(Page("Revenue $55.1m"));
        annotations.AddFact(pageFact);

        ProducingBlockCitations.CiteProducingBlocks(annotations, REVENUE_CLAIM, [conversion]);

        Fact fact = Assert.Single(annotations.Facts);
        Assert.IsType<DocumentSource>(Assert.Single(fact.Sources));
    }

    [Fact]
    public void FigureWhoseFactIsWrittenInAnotherForm_GetsNoSecondFact()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, DOLLAR_REVENUE_OUTPUT);
        var annotations = new BlockAnnotations();
        var pageFact = new Fact("$55.1 million") { IsNumerical = true };
        pageFact.Sources.Add(Page("Revenue $55.1 million"));
        annotations.AddFact(pageFact);

        ProducingBlockCitations.CiteProducingBlocks(annotations, REVENUE_CLAIM, [conversion]);

        Assert.Equal("$55.1 million", Assert.Single(annotations.Facts).Value);
    }

    [Theory]
    [InlineData("The group has 14 offices.", "The group has 14 offices across six countries.")]
    [InlineData("In 2025 the group was restructured.", "The group was restructured in 2025.")]
    [InlineData("FY2025 results were weaker.", "Results for FY2025 were weaker.")]
    [InlineData("12. What drove the decline in margin?", "12. What drove the decline in margin?")]
    public void BareNumbersYearsAndPeriodLabels_AreNeverCited(string output, string inputOutput)
    {
        Block input = InputBlock(CONVERSION_BLOCK_ID, "Plan", inputOutput);
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, output, [input]);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void FigureAnInputStatesOnlyRounded_AboutSomethingElse_GetsNoFact()
    {
        Block spread = InputBlock("block-spread", "Spread", "| |Tangible Net Worth|--|(7.44)|(5.63)|52.38%|13.59%|");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(
            annotations,
            "Free operating cash flow to debt reversed from +13.6% in the prior year.",
            [spread]);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void FigureAnInputStatesOnlyRounded_AboutTheSameThing_IsCitedToThatBlock()
    {
        Block segments = InputBlock("block-segments", "Revenue Growth & Segments", "Revenue grew 28% to £41.5m (FY24: £32.3m) on a reported basis.");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, "Revenue grew from £32.31 million in FY24.", [segments]);

        Assert.Equal("block-segments", Assert.IsType<BlockSource>(Assert.Single(Assert.Single(annotations.Facts).Sources)).BlockId);
    }

    [Fact]
    public void FigureWithAMinus_IsNotTheSameFigureAsAPositiveFact()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, "EBITDA was -$3.3mn after adjusting items of $3.3mn.");
        var annotations = new BlockAnnotations();
        annotations.AddFact(new Fact("$3.3mn") { IsNumerical = true });

        ProducingBlockCitations.CiteProducingBlocks(annotations, "Adjusting items of $3.3mn drove EBITDA to -$3.3mn.", [conversion]);

        Fact signed = Assert.Single(annotations.Facts, fact => fact.Value == "-$3.3mn");
        Assert.Equal(CONVERSION_BLOCK_ID, Assert.IsType<BlockSource>(Assert.Single(signed.Sources)).BlockId);
    }

    [Fact]
    public void FigureNoInputBlockStates_GetsNoFact()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, "Revenue was £41.5m.");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, REVENUE_CLAIM, [conversion]);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void SameValueInTwoInputBlocks_CitesTheOneWhoseContextMatchesTheClaim()
    {
        Block revenue = InputBlock("block-revenue", "Revenue Notes", "A one-off revenue item of £1.2m was recognised in H2 from a format sale.");
        Block bankLines = InputBlock("block-bank-lines", "Bank Lines", "The overdraft facility of £1.2m remains undrawn at the year end.");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(
            annotations,
            "The £1.2m overdraft facility remains undrawn and is the only spare liquidity.",
            [revenue, bankLines]);

        BlockSource source = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(annotations.Facts).Sources));
        Assert.Equal("block-bank-lines", source.BlockId);
    }

    [Fact]
    public void FigureWhoseDigitsAnInputStatesAsABareNumber_GetsNoFact()
    {
        Block notes = InputBlock("block-notes", "Lease Notes", "See Note 12 for the lease commitments.");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, "Margin improved by 12% this year.", [notes]);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void FigureInsideAnHtmlTag_GetsNoFact()
    {
        Block layout = InputBlock("block-layout", "Layout", "<table width=\"100%\"><tr><td>Revenue</td></tr></table>");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, "<table width=\"100%\"><tr><td>Revenue</td></tr></table>", [layout]);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void ValueWhoseMeaningTheClaimLeftUndecided_IsNotCitedToEitherProducer()
    {
        Block revenue = InputBlock("block-revenue", "Revenue Notes", "A one-off revenue item of £1.2m was recognised in H2.");
        Block bankLines = InputBlock("block-bank-lines", "Bank Lines", "The overdraft facility of £1.2m remains undrawn.");
        var revenueFact = new Fact("£1.2m") { IsNumerical = true };
        revenueFact.Sources.Add(Page("A one-off revenue item of £1.2m was recognised in H2."));
        var overdraftFact = new Fact("£1.2m") { IsNumerical = true };
        overdraftFact.Sources.Add(new DocumentSource { PageId = "page-111", DocumentId = "doc-annual-report", CitedText = "The overdraft facility of £1.2m remains undrawn." });
        const string CLAIM = "The figure to watch is £1.2m.";
        var annotations = new BlockAnnotations();

        BlockAnnotationProcessor.InheritReferencedFacts(annotations, [revenueFact, overdraftFact], CLAIM);
        ProducingBlockCitations.CiteProducingBlocks(annotations, CLAIM, [revenue, bankLines]);

        Assert.Empty(annotations.Facts);
    }

    [Fact]
    public void LongInputOutput_ExcerptIsTheSentenceThatStatesTheFigure()
    {
        string filler = string.Concat(Enumerable.Repeat("Margins were broadly stable across the period. ", 20));
        Block conversion = InputBlock(
            CONVERSION_BLOCK_ID,
            USD_CONVERSION,
            filler + CONVERSION_OUTPUT + " " + filler);
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, REVENUE_CLAIM, [conversion]);

        BlockSource source = Assert.IsType<BlockSource>(Assert.Single(Assert.Single(annotations.Facts).Sources));
        Assert.Equal(CONVERSION_OUTPUT, source.CitedText);
    }

    [Fact]
    public async Task ConvertedFigureRestatedByADraft_ParagraphCitesTheConversionBlockWithThePageBehindItAsync()
    {
        var context = new WorkflowAnnotationsContext();

        var statementFact = new Fact("£41.5m") { IsNumerical = true };
        statementFact.Sources.Add(Page("Revenue 41,462 £'000 ... £41.5m for the year"));
        var statement = new BlockAnnotations();
        statement.AddFact(statementFact);
        context.SetAnnotationsForBlock(STATEMENT_BLOCK_ID, statement);

        var conversionBroadFact = new Fact(CONVERSION_OUTPUT);
        conversionBroadFact.Sources.Add(new BlockSource { BlockId = STATEMENT_BLOCK_ID, BlockTitle = "Statement Figures", CitedText = "Revenue £41.5m" });
        var conversion = new BlockAnnotations();
        conversion.AddFact(conversionBroadFact);
        BlockAnnotationProcessor.InheritReferencedFacts(conversion, statement.Facts, CONVERSION_OUTPUT);
        BlockAnnotationProcessor.SplitNumericalFacts(conversion, []);
        context.SetAnnotationsForBlock(CONVERSION_BLOCK_ID, conversion);
        Assert.DoesNotContain(conversion.Facts, f => f.Value == "$55.1m");

        const string DRAFT_OUTPUT = "Revenue reached $55.1m in FY2025.";
        Block draftBlock = InputBlock(DRAFT_BLOCK_ID, "Operating Performance Draft", DRAFT_OUTPUT);
        var draft = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(draft, conversion.Facts, DRAFT_OUTPUT);
        BlockAnnotationProcessor.SplitNumericalFacts(draft, []);
        ProducingBlockCitations.CiteProducingBlocks(draft, DRAFT_OUTPUT, [InputBlock(CONVERSION_BLOCK_ID, USD_CONVERSION, CONVERSION_OUTPUT)]);
        context.SetAnnotationsForBlock(DRAFT_BLOCK_ID, draft);

        ReportParagraph? paragraph = await new WorkflowOutputBuilder().BuildParagraphAsync(draftBlock, new WorkflowOutputContext(), draft, context);

        Assert.NotNull(paragraph);
        Assert.Contains("<fact id=\"c0\">$55.1m</fact>", paragraph.Content);
        Citation citation = Assert.Single(paragraph.Citations);
        BlockSource cited = Assert.IsType<BlockSource>(Assert.Single(citation.Sources));
        Assert.Equal(CONVERSION_BLOCK_ID, cited.BlockId);
        Assert.Empty(cited.MatchingSources);
        BlockCitedSources drawnFrom = Assert.Single(paragraph.CitedSourcesByBlock, list => list.BlockId == CONVERSION_BLOCK_ID);
        DocumentSource page = Assert.IsType<DocumentSource>(Assert.Single(drawnFrom.Sources));
        Assert.Equal(ANNUAL_REPORT_PAGE_ID, page.PageId);
    }

    private static Block InputBlock(string id, string name, string output)
    {
        var block = new Block(BlockTypes.AiQuery, new Section("inputs", new Workflow())) { Id = id, Name = name };
        _ = new BlockResponse(block) { RawResponse = output };
        return block;
    }

    private static DocumentSource Page(string citedText)
    {
        return new DocumentSource
        {
            CitedText = citedText,
            DocumentId = "doc-annual-report",
            DocumentName = ANNUAL_REPORT,
            PageId = ANNUAL_REPORT_PAGE_ID,
            PageNumber = 72,
        };
    }
}
