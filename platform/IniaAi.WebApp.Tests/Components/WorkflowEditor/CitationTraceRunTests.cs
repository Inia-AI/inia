using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Common;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class CitationTraceRunTests
{
    private const string PAGE_ID = CitationTraceTestRun.PAGE_ID;
    private const string REVENUE_SENTENCE = CitationTraceTestRun.REVENUE_SENTENCE;
    private const string CONVERSION_SENTENCE = CitationTraceTestRun.CONVERSION_SENTENCE;

    [Fact]
    public void Chips_CountTheFactsByHowTheyCameAndTheReport()
    {
        (Workflow workflow, _, _, Block draft) = CitationTraceTestRun.Build();

        CitationTraceBlockView view = CitationTraceRun.Of(workflow).ViewOf(draft)!;

        Assert.Equal(["1 fact", "1 cited to the block that worked it out", "1 citation marked", "0 turned down"], view.Chips);
    }

    [Fact]
    public void Facts_AProducerFactNamesTheBlockThatWorkedItOut()
    {
        (Workflow workflow, _, _, Block draft) = CitationTraceTestRun.Build();

        CitationTraceFactRow row = Assert.Single(CitationTraceRun.Of(workflow).ViewOf(draft)!.Facts);

        Assert.Equal("$55.0m", row.Figure);
        Assert.Equal("Worked out by USD conversion", row.How);
        Assert.Equal("4 words in common", row.Detail);
        CitationTraceSourceView source = Assert.Single(row.Sources);
        Assert.Equal(CONVERSION_SENTENCE, source.Excerpt);
    }

    [Fact]
    public void Facts_AnInheritedFactShowsTheSourcesOfTheBlockItCameFrom()
    {
        (Workflow workflow, _, Block conversion, _) = CitationTraceTestRun.Build();

        CitationTraceFactRow row = CitationTraceRun.Of(workflow).ViewOf(conversion)!.Facts[1];

        Assert.Equal("Carried from Statement figures", row.How);
        CitationTraceSourceView source = Assert.Single(row.Sources);
        Assert.Equal("page 1 of annual-report.pdf", source.Label);
        Assert.Equal(REVENUE_SENTENCE, source.Excerpt);
        Assert.Equal(PAGE_ID, source.PageId);
    }

    [Fact]
    public void TurnedDown_GroupsEveryReasonForOneFigure()
    {
        (Workflow workflow, _, Block conversion, _) = CitationTraceTestRun.Build();

        CitationTraceRejectionRow row = Assert.Single(CitationTraceRun.Of(workflow).ViewOf(conversion)!.TurnedDown);

        Assert.Equal("$55.0m", row.Figure);
        Assert.Equal(
            ["No source of its sentence prints it.", "No input block states it either: this block worked it out."],
            row.Reasons);
        Assert.Equal(CONVERSION_SENTENCE, Assert.Single(row.Where).Excerpt);
    }

    [Fact]
    public void Report_SaysWhereEachMarkerSendsTheReaderAndHowItWasPlaced()
    {
        (Workflow workflow, _, _, Block draft) = CitationTraceTestRun.Build();

        CitationTraceReportRow row = Assert.Single(CitationTraceRun.Of(workflow).ViewOf(draft)!.Report!);

        Assert.Equal("c0", row.Marker);
        Assert.Equal("USD conversion", Assert.Single(row.Sources).Label);
        Assert.Equal("No page prints it, so the block stays", row.Settled);
        Assert.Equal("As written", row.Placed);
    }

    [Fact]
    public void Report_IsAbsentWhenTheBlockBuiltNoParagraph()
    {
        (Workflow workflow, _, Block conversion, _) = CitationTraceTestRun.Build();

        Assert.Null(CitationTraceRun.Of(workflow).ViewOf(conversion)!.Report);
    }

    [Fact]
    public void Follow_ListsEveryBlockThatStatesTheFigureInRunOrder()
    {
        (Workflow workflow, _, Block conversion, Block draft) = CitationTraceTestRun.Build();

        IReadOnlyList<CitationTraceFollowedBlock> followed = CitationTraceRun.Of(workflow).Follow(" $55.0M ");

        Assert.Equal([conversion, draft], followed.Select(f => f.Block));
        Assert.Contains("Turned down: no source of its sentence prints it.", followed[0].Lines);
        Assert.Contains("Worked out by USD conversion (4 words in common).", followed[1].Lines);
        Assert.Contains("In the report: marked as written. No page prints it, so the block stays.", followed[1].Lines);
    }

    [Fact]
    public void AnUploadedRunWithNewBlockIds_StillResolvesTheBlocksTheTracesName()
    {
        (Workflow run, _, _, _) = CitationTraceTestRun.Build();
        var clone = new Workflow();
        run.CloneTo(clone);
        run.CarryRunTracesTo(clone);
        Block conversion = clone.Sections.Single().Blocks.Single(b => b.Name == "USD conversion");

        CitationTraceFactRow row = CitationTraceRun.Of(clone).ViewOf(conversion)!.Facts[1];

        Assert.Equal("Carried from Statement figures", row.How);
        Assert.Equal(REVENUE_SENTENCE, Assert.Single(row.Sources).Excerpt);
    }

    [Fact]
    public void AnInheritedFactFromABlockOutsideTheRun_SaysItWasCarriedAndKeepsItsOwnSources()
    {
        var workflow = new Workflow("Single block run");
        Block block = new(BlockTypes.AiQuery, new Section("Body", workflow), "draft") { Name = "Draft" };
        block.CitationTrace = new BlockCitationTrace
        {
            OrganizationId = "org",
            BlockId = block.Id,
            Content = new CitationTraceContent
            {
                Facts = [new TracedFact { Value = "£41.5m", Entry = TracedFactEntry.Inherited, Numerical = true, Sources = [0] }],
                Sources = [new TracedSource { Type = CitationSourceType.Url, Url = "https://harbour.example/results", Text = "Revenue £41.5m" }],
            }.ToJson(),
        };
        CitationTraceRun run = CitationTraceRun.Of(workflow);

        CitationTraceFactRow row = Assert.Single(run.ViewOf(block)!.Facts);

        Assert.Equal("Carried from an earlier block", row.How);
        Assert.Equal("https://harbour.example/results", Assert.Single(row.Sources).Label);
        Assert.Equal("Carried from an earlier block, to https://harbour.example/results.", Assert.Single(Assert.Single(run.Follow("£41.5m")).Lines));
    }

    [Fact]
    public void ACutPassage_ShowsTheRecordedCutAsWritten()
    {
        var workflow = new Workflow("Run");
        Block block = new(BlockTypes.AiQuery, new Section("Body", workflow), "draft") { Name = "Draft" };
        const string CUT = "Harbour Media Group plc is an international screen-based content production business operating through two areas…";
        block.CitationTrace = new BlockCitationTrace
        {
            OrganizationId = "org",
            BlockId = block.Id,
            Content = new CitationTraceContent { Facts = [new TracedFact { Value = CUT, CutFromLength = 412, Entry = TracedFactEntry.Cited }] }.ToJson(),
        };

        Assert.Equal(CUT, Assert.Single(CitationTraceRun.Of(workflow).ViewOf(block)!.Facts).Figure);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void ALinkThatIsNotAWebAddress_IsShownButNeverLinked(string url)
    {
        var workflow = new Workflow("Uploaded run");
        Block block = new(BlockTypes.AiQuery, new Section("Body", workflow), "draft") { Name = "Draft" };
        block.CitationTrace = new BlockCitationTrace
        {
            OrganizationId = "org",
            BlockId = block.Id,
            Content = new CitationTraceContent
            {
                Facts = [new TracedFact { Value = "£41.5m", Entry = TracedFactEntry.Cited, Numerical = true, Sources = [0] }],
                Sources = [new TracedSource { Type = CitationSourceType.Url, Url = url, Text = "Revenue £41.5m" }],
            }.ToJson(),
        };

        CitationTraceSourceView source = Assert.Single(Assert.Single(CitationTraceRun.Of(workflow).ViewOf(block)!.Facts).Sources);

        Assert.Equal(url, source.Label);
        Assert.Null(source.Url);
    }

    [Fact]
    public void ARunWithoutTraces_RecordsNoTraces()
    {
        var workflow = new Workflow("Old run");
        var section = new Section("Body", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "draft") { Name = "Draft" };

        Assert.False(CitationTraceRun.RecordsTraces(workflow));
    }
}
