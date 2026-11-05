using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Tests.Serialization;

public class CitationTraceExportTests
{
    private const string TRACED_BLOCK_ID = "block-traced";
    private const string PAGE_ID = "page-72";
    private const string PAGE_TEXT = "Group revenue was £27.0m in 2025.";

    private static readonly string _traceJson = new CitationTraceContent
    {
        Facts = [new TracedFact { Value = "£27.0m", Entry = TracedFactEntry.Cited, Numerical = true, Sources = [0] }],
        Sources = [new TracedSource { PageId = PAGE_ID, PageNumber = 72, Start = 18, Length = 6 }],
        Rejections = [new TracedRejection { Reason = CitationTraceReason.NoSourceStatesValue, Value = "$35.8m", Fact = 0 }],
    }.ToJson();

    [Fact]
    public void ExportWithTraces_CarriesEachBlocksCitationTraceAsReadableJson()
    {
        string exported = BuildTracedWorkflow().ToExportJson(includeRunTraces: true);

        JToken content = JObject.Parse(exported).SelectToken("$..CitationTrace.Content")!;

        Assert.Equal(JTokenType.Object, content.Type);
        Assert.Equal("NoSourceStatesValue", (string?)content["rejections"]![0]!["reason"]);
    }

    [Fact]
    public void ExportWithTraces_CarriesTheTextOfTheCitedPagesOnce()
    {
        string exported = BuildTracedWorkflow().ToExportJson(includeRunTraces: true);

        JToken page = Assert.Single(JObject.Parse(exported)["CitationTracePages"]!);

        Assert.Equal(PAGE_ID, (string?)page["Id"]);
        Assert.Equal(PAGE_TEXT, (string?)page["Text"]);
    }

    [Fact]
    public void ExportWithoutTraces_LeavesTheTraceOut()
    {
        string exported = BuildTracedWorkflow().ToExportJson();

        Assert.DoesNotContain("CitationTrace", exported);
    }

    [Fact]
    public void RoundTrip_RestoresTheTraceAndThePages()
    {
        Workflow? restored = Workflow.FromExportJson(BuildTracedWorkflow().ToExportJson(includeRunTraces: true));

        Assert.NotNull(restored);
        BlockCitationTrace trace = Assert.Single(restored.Sections.SelectMany(s => s.Blocks), b => b.CitationTrace is not null).CitationTrace!;
        CitationTraceContent? content = CitationTraceContent.Parse(trace.Content);
        Assert.NotNull(content);
        Assert.Equal(_traceJson, content.ToJson());
        Assert.Equal(PAGE_TEXT, Assert.Single(restored.CitationTracePages).Text);
    }

    [Fact]
    public void CarryRunTracesTo_MovesTheTraceOntoTheClonedBlock()
    {
        Workflow? restored = Workflow.FromExportJson(BuildTracedWorkflow().ToExportJson(includeRunTraces: true));
        Assert.NotNull(restored);
        var clone = new Workflow();
        restored.CloneTo(clone);
        Assert.All(clone.Sections.SelectMany(s => s.Blocks), block => Assert.Null(block.CitationTrace));

        restored.CarryRunTracesTo(clone);

        Block traced = Assert.Single(clone.Sections.SelectMany(s => s.Blocks), b => b.CitationTrace is not null);
        Assert.Equal(traced.Id, traced.CitationTrace!.BlockId);
        Assert.Single(clone.CitationTracePages);
    }

    [Fact]
    public void AnInheritedFact_NamesItsOriginsIndexEvenWhenItIsTheFirst()
    {
        string json = new CitationTraceContent
        {
            Facts = [new TracedFact { Value = "£27.0m", Entry = TracedFactEntry.Inherited, Origin = new TracedFactOrigin("block-a", 0) }],
        }.ToJson();

        Assert.Contains("\"origin\":{\"blockId\":\"block-a\",\"fact\":0}", json);
    }

    [Theory]
    [InlineData("2011-05-01")]
    [InlineData("2026-06-30T00:00:00")]
    [InlineData("2026-06-30T00:00:00.000Z")]
    [InlineData("2026-06-30T10:15:00+02:00")]
    public void RoundTrip_KeepsADateValueAsWritten(string value)
    {
        Workflow workflow = BuildTracedWorkflow();
        string traceJson = new CitationTraceContent
        {
            Facts = [new TracedFact { Value = value, Entry = TracedFactEntry.Cited, Numerical = true }],
        }.ToJson();
        workflow.Sections.Single().Blocks.Single(b => b.CitationTrace is not null).CitationTrace!.Content = traceJson;

        Workflow? restored = Workflow.FromExportJson(workflow.ToExportJson(includeRunTraces: true));

        Assert.Equal(traceJson, restored!.Sections.Single().Blocks.Single(b => b.CitationTrace is not null).CitationTrace!.Content);
    }

    [Fact]
    public void CarryRunTracesTo_MapsEachRunBlockIdToItsClone()
    {
        Workflow? restored = Workflow.FromExportJson(BuildTracedWorkflow().ToExportJson(includeRunTraces: true));
        Assert.NotNull(restored);
        var clone = new Workflow();
        restored.CloneTo(clone);

        restored.CarryRunTracesTo(clone);

        Block traced = Assert.Single(clone.Sections.SelectMany(s => s.Blocks), b => b.CitationTrace is not null);
        Assert.NotEqual(TRACED_BLOCK_ID, traced.Id);
        Assert.Equal(traced.Id, clone.TracedBlockIds[TRACED_BLOCK_ID]);
        Assert.Equal(2, clone.TracedBlockIds.Count);
    }

    private static Workflow BuildTracedWorkflow()
    {
        var workflow = new Workflow("Report") { WorkflowTypeId = "t" };
        var section = new Section("Body", workflow);
        var block = new Block(BlockTypes.AiQuery, section, "results") { Id = TRACED_BLOCK_ID, Name = "Results" };
        _ = new Block(BlockTypes.AiQuery, section, "untraced") { Name = "Untraced" };
        block.CitationTrace = new BlockCitationTrace { OrganizationId = "org", BlockId = TRACED_BLOCK_ID, Content = _traceJson };
        workflow.CitationTracePages.Add(new CitationTracePage(PAGE_ID, PAGE_TEXT));
        return workflow;
    }
}
