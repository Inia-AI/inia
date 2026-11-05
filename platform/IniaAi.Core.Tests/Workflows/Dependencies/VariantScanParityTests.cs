using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Dependencies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using static IniaAi.Core.Tests.Workflows.Dependencies.WorkflowDependencyMapTests;

namespace IniaAi.Core.Tests.Workflows.Dependencies;

/// <summary>
/// The editor scans parameterized sections variant by variant instead of expanding them. Folded
/// back onto template blocks, the scan must find exactly the dependencies the run finds after
/// <see cref="ParameterizedSectionExpander"/> really expands the workflow.
/// </summary>
public class VariantScanParityTests
{
    private const string ANNUAL = "AR";
    private const string QUARTERLY = "QR";

    [Fact]
    public void VariantScan_FindsTheSameEdgesAsRealExpansion()
    {
        Workflow workflow = BuildFixture();

        WorkflowDependencyMap scanned = WorkflowDependencyMap.ForTemplate(workflow);
        (WorkflowDependencyMap expanded, Dictionary<string, string> templateIdByExpandedId) = ExpandAndMap(workflow);

        string Fold(string id)
        {
            return templateIdByExpandedId.GetValueOrDefault(id, id);
        }

        List<string> expected = Describe(expanded.Edges.Select(edge => edge with
        {
            SourceId = Fold(edge.SourceId),
            TargetId = Fold(edge.TargetId),
        }));
        List<string> actual = Describe(scanned.Edges.Where(static edge => edge.Kind != DependencyEdgeKind.Gate));

        Assert.True(expected.Count > 10, "The fixture must exercise enough edges to mean something.");
        Assert.Equal(expected, actual);
        Assert.Equal(
            expanded.Issues.Select(static issue => issue.Tag).Order(),
            scanned.Issues.Select(static issue => issue.Tag).Order());
    }

    [Fact]
    public void AVariantThatDoesNotDefineAParameter_LeavesItUnresolved_AsTheRunDoes()
    {
        var workflow = new Workflow { Title = "Params" };
        Section notes = Parameterized(workflow, "Notes", [Variant(ANNUAL, ("PERIOD_ID", ANNUAL)), Variant(QUARTERLY)]);
        _ = AiQuery(notes, "notes", "Find the {PERIOD_ID} notes.");

        WorkflowDependencyMap scanned = WorkflowDependencyMap.ForTemplate(workflow);
        (WorkflowDependencyMap expanded, _) = ExpandAndMap(workflow);

        Assert.Contains(expanded.Issues, static issue => issue.Tag == "PERIOD_ID");
        Assert.Contains(scanned.Issues, static issue => issue.Tag == "PERIOD_ID");
    }

    [Fact]
    public void CrossVariantReadsThatFormNoCycleInTheRun_AreNotACycle()
    {
        var workflow = new Workflow { Title = "Periods" };
        Section periods = Parameterized(workflow, "Periods", [
            Variant(ANNUAL, ("PRIOR", "PAR")),
            Variant("PAR", ("PRIOR", "NONE"))]);
        _ = AiQuery(periods, "x", "Compare with {y_{PRIOR}}.");
        _ = AiQuery(periods, "y", "Compare with {x_{PRIOR}}.");

        WorkflowDependencyMap scanned = WorkflowDependencyMap.ForTemplate(workflow);
        (WorkflowDependencyMap expanded, _) = ExpandAndMap(workflow);

        Assert.DoesNotContain(expanded.Issues, static issue => issue.Kind == DependencyIssueKind.Cycle);
        Assert.DoesNotContain(scanned.Issues, static issue => issue.Kind == DependencyIssueKind.Cycle);
    }

    [Fact]
    public void CrossVariantReadsThatFormACycleInTheRun_AreACycleOnTheTemplateBlocks()
    {
        var workflow = new Workflow { Title = "Periods" };
        Section periods = Parameterized(workflow, "Periods", [
            Variant(ANNUAL, ("PRIOR", "PAR")),
            Variant("PAR", ("PRIOR", ANNUAL))]);
        Block x = AiQuery(periods, "x", "Compare with {y_{PRIOR}}.");
        Block y = AiQuery(periods, "y", "Compare with {x_{PRIOR}}.");

        WorkflowDependencyMap scanned = WorkflowDependencyMap.ForTemplate(workflow);
        (WorkflowDependencyMap expanded, _) = ExpandAndMap(workflow);

        Assert.Contains(expanded.Issues, static issue => issue.Kind == DependencyIssueKind.Cycle);
        DependencyIssue cycle = Assert.Single(scanned.Issues, static issue => issue.Kind == DependencyIssueKind.Cycle);
        Assert.Equal([x.Id, y.Id], cycle.BlockIds);
    }

    [Fact]
    public void ALegacyVariantWithNoClassList_ReadsTheClassesTheRunGivesIt()
    {
        var workflow = new Workflow { Title = "Legacy" };
        Section setup = new("Setup", workflow);
        Block search = new(BlockTypes.IntelligentSearch, setup)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "news", CreatesDocumentClass = "web" },
        };
        Section notes = Parameterized(workflow, "Notes", [
            Variant(ANNUAL) with { SectionSupportedDocumentClasses = ["Annual"] },
            Variant(QUARTERLY)]);
        Block reader = RagReader(notes, "reader", "web");

        WorkflowDependencyMap scanned = WorkflowDependencyMap.ForTemplate(workflow);
        (WorkflowDependencyMap expanded, _) = ExpandAndMap(workflow);

        Assert.DoesNotContain(expanded.Edges, static edge => edge.Kind == DependencyEdgeKind.Document);
        Assert.DoesNotContain(scanned.Edges, edge =>
            edge.Kind == DependencyEdgeKind.Document && edge.SourceId == search.Id && edge.TargetId == reader.Id);
    }

    private static List<string> Describe(IEnumerable<DependencyEdge> edges)
    {
        return [.. edges
            .Where(static edge => edge.SourceId != edge.TargetId)
            .GroupBy(static edge => (edge.SourceId, edge.TargetId, edge.Kind))
            .Select(static group => $"{group.Key.SourceId} -> {group.Key.TargetId} {group.Key.Kind}: "
                + string.Join(", ", group.SelectMany(static edge => edge.Tags).Distinct().Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)];
    }

    private static (WorkflowDependencyMap Map, Dictionary<string, string> TemplateIdByExpandedId) ExpandAndMap(Workflow workflow)
    {
        Dictionary<string, string> templateIdByExpandedId = new(StringComparer.Ordinal);

        foreach (Section section in workflow.Sections
            .Where(static section => !string.IsNullOrWhiteSpace(section.ParameterizedConfig))
            .OrderByDescending(static section => section.Order)
            .ToList())
        {
            ParameterizedSectionConfig config = ParameterizedSectionNormalizer.Normalize(section)!;
            List<Section> expandedSections = ParameterizedSectionExpander.Expand(section, config);
            List<Block> templateBlocks = [.. section.Blocks.OrderBy(static block => block.Order)];

            foreach (Section expandedSection in expandedSections)
            {
                foreach ((Block clone, Block template) in expandedSection.Blocks
                    .OrderBy(static block => block.Order)
                    .Zip(templateBlocks))
                {
                    templateIdByExpandedId[clone.Id] = template.Id;
                }
            }

            workflow.ReplaceSectionWithExpanded(section, expandedSections);
        }

        Dictionary<int, List<Block>> blocksBySectionOrder = workflow.Sections
            .NonSeparator()
            .GroupBy(static section => section.Order)
            .ToDictionary(static group => group.Key, static group => group.SelectMany(static section => section.Blocks).ToList());

        return (WorkflowDependencyMap.ForDispatch(blocksBySectionOrder, [.. workflow.Variables]), templateIdByExpandedId);
    }

    private static Workflow BuildFixture()
    {
        var workflow = new Workflow { Title = "Parity" };
        _ = new WorkflowVariable(workflow, "Region");
        _ = new WorkflowSignatureField(workflow, "SIGrules") { DefaultValue = "rules" };

        Section setup = new("Setup", workflow);
        _ = AiQuery(setup, "company", "Name the company in {Region}.");
        _ = new Block(BlockTypes.IntelligentSearch, setup)
        {
            ReplacementTag = "search",
            Properties = new IntelligentSearchProperties { SearchTermTemplate = "{company.answer}", CreatesDocumentClass = "web" },
        };

        Section notes = Parameterized(workflow, "Notes", [
            Variant(ANNUAL, ("PERIOD_ID", ANNUAL), ("_DESC", "annual")) with { ClassMap = new() { ["Annual"] = ["Annual"] } },
            Variant(QUARTERLY, ("PERIOD_ID", QUARTERLY), ("_DESC", "quarterly")) with { ClassMap = new() { ["Annual"] = ["web"] } }]);
        Block notesBlock = AiQuery(notes, "notes", "Find the {_DESC} notes for {company.answer} applying {SIGrules}.");
        notesBlock.SupportedDocumentClasses = ["Annual"];
        _ = AiQuery(notes, "content", "Expand {notes.answer} for {Counterparty.Name}.");
        Block webReader = AiQuery(notes, "web_reader", "Read the web pages about the {_DESC} report.");
        webReader.SupportedDocumentClasses = ["web"];

        Section content = Parameterized(workflow, "Content", [
            Variant(ANNUAL, ("PERIOD_ID", ANNUAL)),
            Variant(QUARTERLY, ("PERIOD_ID", QUARTERLY))]);
        _ = AiQuery(content, "summary", "Summarise {content_{PERIOD_ID}.answer} and {web_reader_{PERIOD_ID}}.");

        Section final = new("Final", workflow);
        Block combine = AiQuery(final, "combine",
            "Combine {summary_AR.answer}, {summary_QR.answer}, {content_QR.answer}, {notes} and {nowhere}.");
        combine.ShouldUpdateCounterpartyWithResult = true;
        combine.CounterpartyResultType = CounterpartyResultType.Property;
        combine.UpdateCounterpartyPropertyKey = "Style";

        Section styled = new("Styled", workflow);
        _ = AiQuery(styled, "styled", "Write in the {Counterparty.Style} style about {ReportDate}.");

        return workflow;
    }
}
