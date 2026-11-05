using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A run of three blocks, as in the regression run: a statement block cites a page, a USD
/// conversion block restates its sterling figure and works out a dollar figure no source prints,
/// and a draft repeats the dollar figure, cited to the conversion block.
/// </summary>
internal static class CitationTraceTestRun
{
    internal const string STATEMENT_ID = "run-statement";
    internal const string CONVERSION_ID = "run-conversion";
    internal const string DRAFT_ID = "run-draft";
    internal const string PAGE_ID = "page-1";
    internal const string PAGE_TEXT = "Highlights. Revenue for the year was £41.5m, up 8%.";
    internal const string REVENUE_SENTENCE = "Revenue for the year was £41.5m";
    internal const string CONVERSION_SENTENCE = "Revenue: £41.5m ($55.0m)";

    private static readonly string _statementTrace = new CitationTraceContent
    {
        Facts =
        [
            new TracedFact { Value = REVENUE_SENTENCE, Entry = TracedFactEntry.Cited, Sources = [0] },
            new TracedFact { Value = "£41.5m", Entry = TracedFactEntry.Split, Numerical = true, Parent = 0, Sources = [0] },
        ],
        Sources =
        [
            new TracedSource
            {
                Type = CitationSourceType.Document,
                PageId = PAGE_ID,
                PageNumber = 1,
                DocumentName = "annual-report.pdf",
                Start = PAGE_TEXT.IndexOf(REVENUE_SENTENCE, StringComparison.Ordinal),
                Length = REVENUE_SENTENCE.Length,
            },
        ],
    }.ToJson();

    private static readonly string _conversionTrace = new CitationTraceContent
    {
        Facts =
        [
            new TracedFact { Value = CONVERSION_SENTENCE, Entry = TracedFactEntry.Cited, Sources = [0] },
            new TracedFact { Value = "£41.5m", Entry = TracedFactEntry.Inherited, Numerical = true, Origin = new TracedFactOrigin(STATEMENT_ID, 1) },
        ],
        Sources =
        [
            new TracedSource { Type = CitationSourceType.Block, BlockId = STATEMENT_ID, BlockTitle = "Statement figures", Text = REVENUE_SENTENCE },
        ],
        Rejections =
        [
            new TracedRejection { Reason = CitationTraceReason.NoSourceStatesValue, Value = "$55.0m", Fact = 0 },
            new TracedRejection { Reason = CitationTraceReason.NoProducerStatesFigure, Value = "$55.0m" },
        ],
    }.ToJson();

    private static readonly string _draftTrace = new CitationTraceContent
    {
        Facts =
        [
            new TracedFact { Value = "$55.0m", Entry = TracedFactEntry.Producer, Numerical = true, Sources = [0], SharedWords = 4 },
        ],
        Sources =
        [
            new TracedSource { Type = CitationSourceType.Block, BlockId = CONVERSION_ID, BlockTitle = "USD conversion", Text = CONVERSION_SENTENCE },
        ],
        Report = new TracedReport
        {
            Citations =
            [
                new TracedCitation { Id = "c0", Value = "$55.0m", Flattening = TracedFlattening.BlockFallback, Sources = [0], Marker = TracedMarker.Literal },
            ],
        },
    }.ToJson();

    internal static (Workflow Workflow, Block Statement, Block Conversion, Block Draft) Build()
    {
        var workflow = new Workflow("Report");
        var section = new Section("Body", workflow);
        Block statement = Traced(new Block(BlockTypes.AiQuery, section, "statement") { Id = STATEMENT_ID, Name = "Statement figures" }, _statementTrace);
        Block conversion = Traced(new Block(BlockTypes.AiQuery, section, "conversion") { Id = CONVERSION_ID, Name = "USD conversion" }, _conversionTrace);
        Block draft = Traced(new Block(BlockTypes.AiQuery, section, "draft") { Id = DRAFT_ID, Name = "Performance draft" }, _draftTrace);
        workflow.CitationTracePages.Add(new CitationTracePage(PAGE_ID, PAGE_TEXT));

        return (workflow, statement, conversion, draft);
    }

    private static Block Traced(Block block, string content)
    {
        block.CitationTrace = new BlockCitationTrace { OrganizationId = "org", BlockId = block.Id, Content = content };
        return block;
    }
}
