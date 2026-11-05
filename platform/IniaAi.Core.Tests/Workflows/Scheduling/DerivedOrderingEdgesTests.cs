using IniaAi.Core.Workflows.Graph;
using IniaAi.Core.Workflows.Scheduling;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Scheduling;

/// <summary>
/// Orderings the runtime depends on but no placeholder names: a block that receives a document
/// class must wait for the block that creates it, and a block that reads a counterparty value must
/// wait for the block that writes it back. Neither ordering may ever force a cycle break.
/// </summary>
public class DerivedOrderingEdgesTests
{
    private const string WEB_CLASS = "web";
    private const string NEWS_CLASS = "isearch_News_MA";
    private const string REPORTING_STYLE = "ReportingStyle";

    [Fact]
    public void RagReaderOfADocumentClass_LandsInALaterLayerThanTheProducer()
    {
        var section = new Section("Section 1", new Workflow { Title = "Documents" });
        Block first = AiQuery(section, "A", 0, "a", "Name a company.");
        Block search = Search(section, "B", 1, "search", "{a}", WEB_CLASS);
        Block reader = RagReader(section, "C", 2, "reader", WEB_CLASS);

        SchedulingView view = Schedule(section, first, search, reader);

        Assert.True(LayerOf(view, "C") > LayerOf(view, "B"));
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void SanitiserOfTwoSearchesOfOneClass_WaitsForBothSearches()
    {
        var section = new Section("iSEARCH - News and M&A", new Workflow { Title = "Corp Part 2" });
        Block company = AiQuery(section, "company", 0, "company_name", "Name the company.");
        Block news = Search(section, "news", 1, "search_news", "{company_name} news", NEWS_CLASS);
        Block deals = Search(section, "deals", 2, "search_ma", "{company_name} M&A", NEWS_CLASS);
        Block sanitiser = RagReader(section, "sanitiser", 3, "sanitised_news", NEWS_CLASS);

        SchedulingView view = Schedule(section, company, news, deals, sanitiser);

        Assert.True(LayerOf(view, "sanitiser") > LayerOf(view, "news"));
        Assert.True(LayerOf(view, "sanitiser") > LayerOf(view, "deals"));
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void Json2XlsxInputDocumentClassReader_LandsInALaterLayerThanTheProducer()
    {
        var section = new Section("Section 1", new Workflow { Title = "Documents" });
        Block first = AiQuery(section, "A", 0, "a", "Name a company.");
        Block search = Search(section, "B", 1, "search", "{a}", WEB_CLASS);
        Block extract = new(BlockTypes.Json2Xlsx, section)
        {
            Id = "C",
            Order = 2,
            ReplacementTag = "extract",
            Properties = new Json2XlsxProperties { InputDocumentClass = WEB_CLASS },
        };

        SchedulingView view = Schedule(section, first, search, extract);

        Assert.True(LayerOf(view, "C") > LayerOf(view, "B"));
    }

    [Theory]
    [InlineData(DocumentClass.KEY_ANY)]
    [InlineData(DocumentClass.KEY_NONE)]
    [InlineData(null)]
    public void AnyNoneOrEmptyReader_GetsNoProducerEdge(string? documentClass)
    {
        var section = new Section("Section 1", new Workflow { Title = "Documents" });
        Block search = Search(section, "B", 0, "search", "news", WEB_CLASS);
        Block reader = RagReader(section, "C", 1, "reader", documentClass);

        (WorkflowGraph graph, SchedulingView view) = Build(section, null, search, reader);

        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == "B" && edge.TargetId == "C");
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void AiQueryReaderWithoutRagSettings_GetsNoProducerEdge()
    {
        var section = new Section("Section 1", new Workflow { Title = "Documents" });
        Block search = Search(section, "B", 0, "search", "news", WEB_CLASS);
        Block reader = AiQuery(section, "C", 1, "reader", "Summarise.");
        reader.SupportedDocumentClasses = [WEB_CLASS];

        (WorkflowGraph graph, SchedulingView view) = Build(section, null, search, reader);

        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == "B" && edge.TargetId == "C");
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void ADocumentEdgeThatWouldCloseACycleThroughAFence_IsDropped()
    {
        var section = new Section("Section 1", new Workflow { Title = "Fenced" });
        Block reader = RagReader(section, "R", 0, "reader", WEB_CLASS);
        Block fence = new(BlockTypes.BuildTopicsForAllDocuments, section) { Id = "F", Order = 1, ReplacementTag = "topics" };
        Block search = Search(section, "P", 2, "search", "news", WEB_CLASS);

        (WorkflowGraph graph, SchedulingView view) = Build(section, new HashSet<string> { "F" }, reader, fence, search);

        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == "P" && edge.TargetId == "R");
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void CounterpartyPropertyReader_LandsInALaterLayerThanTheWriteBack()
    {
        var section = new Section("Section 1", new Workflow { Title = "Counterparty" });
        Block first = AiQuery(section, "A", 0, "a", "Describe the style.");
        Block writer = AiQuery(section, "W", 1, "writer", "Condense {a}.");
        writer.ShouldUpdateCounterpartyWithResult = true;
        writer.CounterpartyResultType = CounterpartyResultType.Property;
        writer.UpdateCounterpartyPropertyKey = REPORTING_STYLE;
        Block reader = AiQuery(section, "R", 2, "reader", "Report in the {Counterparty.reportingstyle} style.");

        SchedulingView view = Schedule(section, first, writer, reader);

        Assert.True(LayerOf(view, "R") > LayerOf(view, "W"));
    }

    [Theory]
    [InlineData(CounterpartyResultType.LastFinancialAnalysisResult)]
    [InlineData(null)]
    public void LastFinancialAnalysisReader_LandsInALaterLayerThanTheWriteBack(CounterpartyResultType? resultType)
    {
        var section = new Section("Section 1", new Workflow { Title = "Counterparty" });
        Block first = AiQuery(section, "A", 0, "a", "Analyse.");
        Block writer = AiQuery(section, "W", 1, "writer", "Condense {a}.");
        writer.ShouldUpdateCounterpartyWithResult = true;
        writer.CounterpartyResultType = resultType;
        Block reader = AiQuery(section, "R", 2, "reader", "Compare with {Counterparty.LastFinancialAnalysisResult.revenue}.");

        SchedulingView view = Schedule(section, first, writer, reader);

        Assert.True(LayerOf(view, "R") > LayerOf(view, "W"));
    }

    [Fact]
    public void ACounterpartyReaderAuthoredBeforeTheWriter_StillWaitsForIt()
    {
        var section = new Section("Section 1", new Workflow { Title = "Counterparty" });
        Block reader = AiQuery(section, "R", 0, "reader", "Last time: {Counterparty.LastFinancialAnalysisResult}");
        Block first = AiQuery(section, "A", 1, "a", "Analyse.");
        Block writer = AiQuery(section, "W", 2, "writer", "Condense {a}.");
        writer.ShouldUpdateCounterpartyWithResult = true;

        SchedulingView view = Schedule(section, reader, first, writer);

        Assert.True(LayerOf(view, "R") > LayerOf(view, "W"));
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Theory]
    [InlineData("{Counterparty.Name}", CounterpartyResultType.LastFinancialAnalysisResult, null)]
    [InlineData("{Counterparty}", CounterpartyResultType.LastFinancialAnalysisResult, null)]
    [InlineData("{Counterparty.Properties.ReportingStyle}", CounterpartyResultType.Property, REPORTING_STYLE)]
    [InlineData("{Counterparty.ReportingStyle}", CounterpartyResultType.Property, "   ")]
    [InlineData("{Counterparty.Tone}", CounterpartyResultType.Property, REPORTING_STYLE)]
    public void AReaderOfAFieldTheWriterDoesNotWrite_GetsNoEdge(
        string placeholder,
        CounterpartyResultType resultType,
        string? propertyKey)
    {
        var section = new Section("Section 1", new Workflow { Title = "Counterparty" });
        Block writer = AiQuery(section, "W", 0, "writer", "Analyse.");
        writer.ShouldUpdateCounterpartyWithResult = true;
        writer.CounterpartyResultType = resultType;
        writer.UpdateCounterpartyPropertyKey = propertyKey;
        Block reader = AiQuery(section, "R", 1, "reader", $"Use {placeholder}.");

        (WorkflowGraph graph, SchedulingView view) = Build(section, null, writer, reader);

        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == "W" && edge.TargetId == "R");
        Assert.Empty(view.ForcedCycleNodes);
    }

    [Fact]
    public void ACounterpartyReaderThatFeedsTheWriter_GetsNoEdge()
    {
        var section = new Section("Section 1", new Workflow { Title = "Counterparty" });
        Block reader = AiQuery(section, "R", 0, "reader", "Last time: {Counterparty.LastFinancialAnalysisResult}");
        Block writer = AiQuery(section, "W", 1, "writer", "Update {reader}.");
        writer.ShouldUpdateCounterpartyWithResult = true;

        (WorkflowGraph graph, SchedulingView view) = Build(section, null, reader, writer);

        Assert.DoesNotContain(graph.Edges, edge => edge.SourceId == "W" && edge.TargetId == "R");
        Assert.Empty(view.ForcedCycleNodes);
        Assert.True(LayerOf(view, "W") > LayerOf(view, "R"));
    }

    private static Block AiQuery(Section section, string id, int order, string tag, string prompt)
    {
        return new Block(BlockTypes.AiQuery, section)
        {
            Id = id,
            Order = order,
            ReplacementTag = tag,
            Properties = new AiQueryProperties { UserPromptTemplate = prompt },
        };
    }

    private static Block RagReader(Section section, string id, int order, string tag, string? documentClass)
    {
        Block reader = AiQuery(section, id, order, tag, "Summarise the documents.");
        reader.SupportedDocumentClasses = documentClass is null ? [] : [documentClass];
        _ = new RagSettings(reader) { Type = RagTypes.WholeDocument };

        return reader;
    }

    private static Block Search(Section section, string id, int order, string tag, string term, string documentClass)
    {
        return new Block(BlockTypes.IntelligentSearch, section)
        {
            Id = id,
            Order = order,
            ReplacementTag = tag,
            Properties = new IntelligentSearchProperties
            {
                SearchTermTemplate = term,
                CreatesDocumentClass = documentClass,
            },
        };
    }

    private static SchedulingView Schedule(Section section, params Block[] blocks)
    {
        return Build(section, null, blocks).View;
    }

    private static (WorkflowGraph Graph, SchedulingView View) Build(
        Section section,
        IReadOnlySet<string>? fenceBlockIds,
        params Block[] blocks)
    {
        WorkflowGraph graph = new WorkflowGraphBuilder().Build(
            new Dictionary<int, List<Block>> { [section.Order] = [.. blocks] },
            [],
            fenceBlockIds);

        return (graph, SchedulingViewBuilder.Build(graph));
    }

    private static int LayerOf(SchedulingView view, string blockId)
    {
        for (int layer = 0; layer < view.Layers.Count; layer++)
        {
            if (view.Layers[layer].BlockIds.Contains(blockId))
            {
                return layer;
            }
        }

        throw new InvalidOperationException($"Block '{blockId}' is not scheduled.");
    }
}
