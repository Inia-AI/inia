using IniaAi.Common.Enums;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using FileMetadata = IniaAi.Domain.Entities.Documents.FileMetadata;
using TraceRecording = IniaAi.Core.Workflows.Annotations.CitationTraces;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class CitationTraceRecorderTests
{
    private const string BLOCK_ID = "block-under-trace";
    private const string UPSTREAM_BLOCK_ID = "block-upstream";
    private const string CONVERSION_BLOCK_ID = "block-usd-conversion";
    private const string CONVERSION_TITLE = "USD conversion";
    private const string PAGE_TEXT = "Group revenue was £27.0m in 2025, up from £24.1m. Cash at the year end was £3.2m.";
    private const string REVENUE_EXCERPT = "Group revenue was £27.0m in 2025";

    [Fact]
    public void FigureNoSourceStates_IsRecordedAsRejected_AndTheGroundedFigureAsSplitFromItsClaim()
    {
        Page page = AnnualReportPage(PAGE_TEXT);
        var annotations = new BlockAnnotations();
        var claim = new Fact("Revenue: £27.0m ($35.8m)");
        claim.Sources.Add(SourceOn(page, REVENUE_EXCERPT));
        annotations.AddFact(claim);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);
        CitationTraceContent trace = Finish(annotations, page);

        Assert.Equal(TracedFactEntry.Cited, trace.Facts[0].Entry);
        TracedFact revenue = Assert.Single(trace.Facts, fact => fact.Value == "£27.0m");
        Assert.Equal(TracedFactEntry.Split, revenue.Entry);
        Assert.Equal(0, revenue.Parent);
        TracedRejection dollars = Assert.Single(trace.Rejections, r => r.Value == "$35.8m");
        Assert.Equal(CitationTraceReason.NoSourceStatesValue, dollars.Reason);
        Assert.Equal(0, dollars.Fact);
    }

    [Fact]
    public void SourceWhosePageDoesNotStateTheFigure_IsRecordedAsRemoved()
    {
        Page page = AnnualReportPage(PAGE_TEXT);
        var annotations = new BlockAnnotations();
        var cash = new Fact("£7.4m");
        cash.Sources.Add(SourceOn(page, "Cash at the year end was £7.4m."));
        annotations.AddFact(cash);

        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);
        CitationTraceContent trace = Finish(annotations, page);

        Assert.Empty(trace.Facts);
        TracedRejection removed = Assert.Single(trace.Rejections);
        Assert.Equal(CitationTraceReason.SourceDoesNotStateValue, removed.Reason);
        Assert.Equal("£7.4m", removed.Value);
        TracedSource source = trace.Sources[removed.Source!.Value];
        Assert.Equal(page.Id, source.PageId);
    }

    [Fact]
    public void PageSource_IsNamedByItsSpanOnThePage_AndAnExcerptThePageLacksIsCopied()
    {
        Page page = AnnualReportPage(PAGE_TEXT);
        var annotations = new BlockAnnotations();
        var revenue = new Fact("£27.0m");
        revenue.Sources.Add(SourceOn(page, REVENUE_EXCERPT));
        revenue.Sources.Add(new UrlSource { Url = "https://example.com/results", CitedText = "Revenue £27.0m" });
        annotations.AddFact(revenue);

        CitationTraceContent trace = Finish(annotations, page);

        TracedSource onPage = Assert.Single(trace.Sources, source => source.Type == CitationSourceType.Document);
        Assert.Equal(PAGE_TEXT.IndexOf(REVENUE_EXCERPT, StringComparison.Ordinal), onPage.Start);
        Assert.Equal(REVENUE_EXCERPT.Length, onPage.Length);
        Assert.Null(onPage.Text);
        TracedSource link = Assert.Single(trace.Sources, source => source.Type == CitationSourceType.Url);
        Assert.Equal("Revenue £27.0m", link.Text);
    }

    [Fact]
    public void InheritedFact_NamesTheFactOfTheBlockThatCitedIt_WithoutRepeatingItsSources()
    {
        Page page = AnnualReportPage(PAGE_TEXT);
        var origins = new FactOrigins();
        var upstream = new BlockAnnotations();
        var revenue = new Fact("£27.0m") { IsNumerical = true };
        revenue.Sources.Add(SourceOn(page, REVENUE_EXCERPT));
        upstream.AddFact(revenue);
        _ = upstream.Trace.Finish(upstream, new PageIndex([page]), origins, UPSTREAM_BLOCK_ID);

        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(annotations, upstream.Facts, "Revenue rose to £27.0m.");
        CitationTraceContent trace = annotations.Trace.Finish(annotations, new PageIndex([page]), origins, BLOCK_ID);

        TracedFact inherited = Assert.Single(trace.Facts);
        Assert.Equal(TracedFactEntry.Inherited, inherited.Entry);
        Assert.Equal(new TracedFactOrigin(UPSTREAM_BLOCK_ID, 0), inherited.Origin);
        Assert.Empty(inherited.Sources);
        Assert.Empty(trace.Sources);
    }

    [Theory]
    [InlineData("712", "The register lists 712 holders.", CitationTraceReason.BareNumberNotInherited)]
    [InlineData("2025", "The Employment Rights Act 2025 changes notice periods.", CitationTraceReason.YearContextDiffers)]
    public void UpstreamFactTheOutputStatesButTheGateTurnsDown_IsRecordedWithItsOrigin(
        string value,
        string output,
        CitationTraceReason reason)
    {
        Page page = AnnualReportPage($"UK advertising spend will grow 4% in {value}, the forecast says.");
        var origins = new FactOrigins();
        var upstream = new BlockAnnotations();
        var fact = new Fact(value) { IsNumerical = true };
        fact.Sources.Add(SourceOn(page, page.Text!));
        upstream.AddFact(fact);
        _ = upstream.Trace.Finish(upstream, new PageIndex([page]), origins, UPSTREAM_BLOCK_ID);

        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(annotations, upstream.Facts, output);
        CitationTraceContent trace = annotations.Trace.Finish(annotations, new PageIndex([page]), origins, BLOCK_ID);

        Assert.Empty(trace.Facts);
        TracedRejection rejection = Assert.Single(trace.Rejections);
        Assert.Equal(reason, rejection.Reason);
        Assert.Equal(value, rejection.Value);
        Assert.Equal([new TracedFactOrigin(UPSTREAM_BLOCK_ID, 0)], rejection.Upstream);
    }

    [Fact]
    public void UpstreamBareNumberTheOutputDoesNotState_IsNotRecorded()
    {
        var upstream = new BlockAnnotations();
        var fact = new Fact("712") { IsNumerical = true };
        fact.Sources.Add(new UrlSource { Url = "https://example.com", CitedText = "712 holders" });
        upstream.AddFact(fact);

        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(annotations, upstream.Facts, "The register is long.");
        CitationTraceContent trace = annotations.Trace.Finish(annotations, new PageIndex([]), new FactOrigins(), BLOCK_ID);

        Assert.Empty(trace.Rejections);
    }

    [Fact]
    public void SameValueOnTwoPagesWithNoWordToChooseBy_IsRecordedAsUndecided()
    {
        Page overdraft = AnnualReportPage("The overdraft facility of £1.2m was undrawn.");
        Page grant = AnnualReportPage("A government grant of £1.2m was received.");
        var origins = new FactOrigins();
        var upstream = new BlockAnnotations();
        foreach (Page page in new[] { overdraft, grant })
        {
            var fact = new Fact("£1.2m") { IsNumerical = true };
            fact.Sources.Add(SourceOn(page, page.Text!));
            upstream.Facts.Add(fact);
        }

        _ = upstream.Trace.Finish(upstream, new PageIndex([overdraft, grant]), origins, UPSTREAM_BLOCK_ID);

        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(annotations, upstream.Facts, "It reported £1.2m.");
        CitationTraceContent trace = annotations.Trace.Finish(annotations, new PageIndex([overdraft, grant]), origins, BLOCK_ID);

        TracedRejection undecided = Assert.Single(trace.Rejections);
        Assert.Equal(CitationTraceReason.SameValueDifferentMeanings, undecided.Reason);
        Assert.Equal([new TracedFactOrigin(UPSTREAM_BLOCK_ID, 0), new TracedFactOrigin(UPSTREAM_BLOCK_ID, 1)], undecided.Upstream);
    }

    [Fact]
    public void SameValueOnTwoPages_TheLoserIsRecordedAsAnotherMeaning()
    {
        Page overdraft = AnnualReportPage("The overdraft facility of £1.2m was undrawn.");
        Page grant = AnnualReportPage("A government grant of £1.2m was received.");
        var origins = new FactOrigins();
        var upstream = new BlockAnnotations();
        foreach (Page page in new[] { overdraft, grant })
        {
            var fact = new Fact("£1.2m") { IsNumerical = true };
            fact.Sources.Add(SourceOn(page, page.Text!));
            upstream.Facts.Add(fact);
        }

        _ = upstream.Trace.Finish(upstream, new PageIndex([overdraft, grant]), origins, UPSTREAM_BLOCK_ID);

        var annotations = new BlockAnnotations();
        BlockAnnotationProcessor.InheritReferencedFacts(annotations, upstream.Facts, "The overdraft facility of £1.2m stayed undrawn.");
        CitationTraceContent trace = annotations.Trace.Finish(annotations, new PageIndex([overdraft, grant]), origins, BLOCK_ID);

        Assert.Equal(new TracedFactOrigin(UPSTREAM_BLOCK_ID, 0), Assert.Single(trace.Facts).Origin);
        TracedRejection loser = Assert.Single(trace.Rejections);
        Assert.Equal(CitationTraceReason.OtherMeaningChosen, loser.Reason);
        Assert.Equal([new TracedFactOrigin(UPSTREAM_BLOCK_ID, 1)], loser.Upstream);
    }

    [Fact]
    public void FigureCitedToItsProducer_IsRecordedWithTheWordsItShares_AndAFigureNoInputStatesIsRejected()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, CONVERSION_TITLE, "Revenue: £27.0m ($35.8m)");
        var annotations = new BlockAnnotations();

        ProducingBlockCitations.CiteProducingBlocks(annotations, "Revenue reached $35.8m while costs were $12.0m.", [conversion]);
        CitationTraceContent trace = Finish(annotations);

        TracedFact revenue = Assert.Single(trace.Facts);
        Assert.Equal(TracedFactEntry.Producer, revenue.Entry);
        Assert.Equal(1, revenue.SharedWords);
        Assert.Equal(CONVERSION_BLOCK_ID, trace.Sources[Assert.Single(revenue.Sources)].BlockId);
        TracedRejection costs = Assert.Single(trace.Rejections);
        Assert.Equal(CitationTraceReason.NoProducerStatesFigure, costs.Reason);
        Assert.Equal("$12.0m", costs.Value);
    }

    [Fact]
    public void FigureUndecidedBetweenMeanings_IsRecordedAsNotCitedToAProducer()
    {
        Block conversion = InputBlock(CONVERSION_BLOCK_ID, CONVERSION_TITLE, "Debt: £1.2m");
        var annotations = new BlockAnnotations();
        annotations.UndecidedFacts.Add(new Fact("£1.2m") { IsNumerical = true });

        ProducingBlockCitations.CiteProducingBlocks(annotations, "It reported £1.2m.", [conversion]);
        CitationTraceContent trace = Finish(annotations);

        Assert.Empty(trace.Facts);
        Assert.Equal(CitationTraceReason.UndecidedMeaning, Assert.Single(trace.Rejections).Reason);
    }

    [Fact]
    public void ABlockThatBuiltNoParagraph_KeepsATraceWithNoReport()
    {
        var annotations = new BlockAnnotations();
        annotations.AddFact(new Fact("£27.0m") { IsNumerical = true });
        Block block = InputBlock(BLOCK_ID, "Notes", "Revenue was £27.0m.");
        TraceRecording.Record(block, annotations, new PageIndex([]), new WorkflowAnnotationsContext(), "org");

        TraceRecording.RecordReport(block, paragraph: null, annotations, "org");

        CitationTraceContent trace = CitationTraceContent.Parse(block.CitationTrace!.Content)!;
        Assert.Single(trace.Facts);
        Assert.Null(trace.Report);
    }

    [Fact]
    public async Task ReportParagraph_RecordsEachCitationsFlattening_ItsMarker_AndTheOccurrencesItSkipped()
    {
        Page page = AnnualReportPage(PAGE_TEXT);
        var annotations = new BlockAnnotations();
        foreach ((string value, string excerpt) in new[] { ("£27.0m", REVENUE_EXCERPT), ("£3.2m", "Cash at the year end was £3.2m."), ("£24.1m", "up from £24.1m") })
        {
            var fact = new Fact(value) { IsNumerical = true };
            fact.Sources.Add(SourceOn(page, excerpt));
            annotations.AddFact(fact);
        }

        _ = Finish(annotations, page);
        Block block = InputBlock(BLOCK_ID, "Results", "Revenue was <span title=\"£24.1m\">£27.0m</span> and cash £3.2m.");

        ReportParagraph? paragraph = await new WorkflowOutputBuilder().BuildParagraphAsync(block, null, annotations, new WorkflowAnnotationsContext());
        CitationTraceContent trace = annotations.Trace.FinishReport()!;
        TracedReport report = trace.Report!;

        Assert.NotNull(paragraph);
        TracedCitation revenue = Assert.Single(report.Citations, c => c.Value == "£27.0m");
        Assert.Equal(TracedFlattening.Direct, revenue.Flattening);
        Assert.Equal(TracedMarker.Literal, revenue.Marker);
        Assert.Equal([page.Id], revenue.Sources.Select(index => trace.Sources[index].PageId));
        Assert.Null(Assert.Single(report.Citations, c => c.Value == "£24.1m").Marker);
        Assert.Contains(report.Rejections, r => r.Reason == CitationTraceReason.InsideHtmlTag && r.Value == "£24.1m");
        Assert.Contains(report.Rejections, r => r.Reason == CitationTraceReason.NoMarkerPlaced && r.Value == "£24.1m");
    }

    [Fact]
    public void ListNumberAndNegatedOccurrences_AreRecordedAsSkipped()
    {
        var annotations = new BlockAnnotations();
        var count = new Fact("12") { IsNumerical = true };
        count.Sources.Add(new UrlSource { Url = "https://example.com", CitedText = "12 sites" });
        var margin = new Fact("5.27%") { IsNumerical = true };
        margin.Sources.Add(new UrlSource { Url = "https://example.com", CitedText = "margin 5.27%" });
        annotations.AddFact(count);
        annotations.AddFact(margin);
        _ = Finish(annotations);

        _ = BlockAnnotationProcessor.ApplyFactsWithIds(
            "12. The margin was -5.27% this year.",
            annotations.Facts,
            [new() { Id = "c0", IsNumerical = true }, new() { Id = "c1", IsNumerical = true }],
            annotations.Trace);
        TracedReport report = annotations.Trace.FinishReport()!.Report!;

        Assert.Contains(report.Rejections, r => r.Reason == CitationTraceReason.ListItemNumber && r.Value == "12");
        Assert.Contains(report.Rejections, r => r.Reason == CitationTraceReason.NegatedByMinus && r.Value == "5.27%");
    }

    [Fact]
    public void Trace_ReadsBackFromItsJson()
    {
        Page page = AnnualReportPage(PAGE_TEXT);
        var annotations = new BlockAnnotations();
        var claim = new Fact("Revenue: £27.0m ($35.8m)");
        claim.Sources.Add(SourceOn(page, REVENUE_EXCERPT));
        annotations.AddFact(claim);
        BlockAnnotationProcessor.SplitNumericalFacts(annotations, [page]);
        CitationTraceContent trace = Finish(annotations, page);

        CitationTraceContent? read = CitationTraceContent.Parse(trace.ToJson());

        Assert.NotNull(read);
        Assert.Equal(trace.ToJson(), read.ToJson());
        Assert.Contains("\"entry\":\"Cited\"", trace.ToJson());
    }

    private static CitationTraceContent Finish(BlockAnnotations annotations, params Page[] pages)
    {
        return annotations.Trace.Finish(annotations, new PageIndex(pages), new FactOrigins(), BLOCK_ID);
    }

    private static Page AnnualReportPage(string text)
    {
        var document = new Document(
            new FileMetadata("IniaTest", "annual-report.pdf", text.Length, StorageProvider.Database, "annual-report"),
            MediaType.TextPlain);
        return new Page(document, 12, 12) { RawText = text };
    }

    private static DocumentSource SourceOn(Page page, string citedText)
    {
        return new DocumentSource
        {
            CitedText = citedText,
            DocumentId = page.DocumentId,
            DocumentName = "annual-report.pdf",
            PageId = page.Id,
            PageNumber = page.PageNumber,
        };
    }

    private static Block InputBlock(string id, string name, string output)
    {
        var block = new Block(BlockTypes.AiQuery, new Section("inputs", new Workflow())) { Id = id, Name = name };
        _ = new BlockResponse(block) { RawResponse = output };
        return block;
    }
}
