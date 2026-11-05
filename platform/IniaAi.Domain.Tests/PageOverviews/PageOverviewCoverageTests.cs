using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.PageOverviews;

public class PageOverviewCoverageTests
{
    private static readonly PageOverviewPrompts _prompts = new() { Pages = new PageOverviewPrompt { UserPrompt = "Summarise page {0}." } };

    [Fact]
    public void ABlockThatSearchesOverviews_ForAClassWithoutAPrompt_IsFlagged()
    {
        Workflow workflow = Workflow(out Section section);
        DocumentClass deck = new(workflow, "deck") { DisplayName = "Presentation" };
        Block search = SimilaritySearch(section, "deck");

        PageOverviewCoverage.Gap gap = PageOverviewCoverage.FindGap(workflow)!;

        Assert.Equal([search], gap.Blocks);
        Assert.Equal([deck], gap.Classes);
    }

    [Fact]
    public void TheWorkflowDefault_CoversEveryClass()
    {
        Workflow workflow = Workflow(out Section section);
        workflow.Configuration = new WorkflowConfiguration { PageOverviews = _prompts };
        _ = new DocumentClass(workflow, "deck");
        _ = SimilaritySearch(section, "deck");

        Assert.Null(PageOverviewCoverage.FindGap(workflow));
    }

    [Fact]
    public void AClassWithItsOwnPrompt_IsCovered_AndTheOthersAreNot()
    {
        Workflow workflow = Workflow(out Section section);
        _ = new DocumentClass(workflow, "report") { PageOverviews = _prompts };
        DocumentClass deck = new(workflow, "deck");
        _ = SimilaritySearch(section, DocumentClass.KEY_ANY);

        Assert.Equal([deck], PageOverviewCoverage.FindGap(workflow)!.Classes);
    }

    [Fact]
    public void ABlockReadingOnlyCoveredClasses_IsNotFlagged()
    {
        Workflow workflow = Workflow(out Section section);
        _ = new DocumentClass(workflow, "report") { PageOverviews = _prompts };
        _ = new DocumentClass(workflow, "deck");
        _ = SimilaritySearch(section, "report");

        Assert.Null(PageOverviewCoverage.FindGap(workflow));
    }

    [Fact]
    public void AnOutputClass_IsNeverFlagged()
    {
        Workflow workflow = Workflow(out Section section);
        _ = new DocumentClass(workflow, "report") { PageOverviews = _prompts };
        _ = new DocumentClass(workflow, "spread") { IsOutputDocument = true };
        _ = SimilaritySearch(section, DocumentClass.KEY_ANY);

        Assert.Null(PageOverviewCoverage.FindGap(workflow));
    }

    [Fact]
    public void AOnePageVersionAlone_LeavesOtherDocumentsWithoutOverviews_AndIsFlagged()
    {
        Workflow workflow = Workflow(out Section section);
        workflow.Configuration = new WorkflowConfiguration
        {
            PageOverviews = new PageOverviewPrompts { SinglePage = new PageOverviewPrompt { UserPrompt = "Summarise sheet {0}." } },
        };
        DocumentClass report = new(workflow, "report");
        _ = SimilaritySearch(section, "report");

        Assert.Equal([report], PageOverviewCoverage.FindGap(workflow)!.Classes);
    }

    [Fact]
    public void AWorkflowWithoutClasses_IsFlaggedWhenItHasNoDefault()
    {
        Workflow workflow = Workflow(out Section section);
        Block topics = new(BlockTypes.BuildTopicsForAllDocuments, section, "topics");

        PageOverviewCoverage.Gap gap = PageOverviewCoverage.FindGap(workflow)!;

        Assert.Equal([topics], gap.Blocks);
        Assert.Empty(gap.Classes);
    }

    [Fact]
    public void AKeywordSearchOverOverviews_ForAClassWithoutAPrompt_IsFlagged()
    {
        Workflow workflow = Workflow(out Section section);
        DocumentClass deck = new(workflow, "deck");
        Block search = new(BlockTypes.AiQuery, section, "keywords") { Properties = new AiQueryProperties() };
        _ = new RagSettings(search) { Type = RagTypes.KeywordBased, ShouldSearchOverviews = true };

        Assert.Equal([deck], PageOverviewCoverage.FindGap(workflow)!.Classes);
    }

    [Fact]
    public void TheMessage_NamesTheClassesThatGetNoOverviews()
    {
        Workflow workflow = Workflow(out Section section);
        _ = new DocumentClass(workflow, "deck") { DisplayName = "Presentation" };
        _ = new DocumentClass(workflow, "memo");
        _ = SimilaritySearch(section, "deck");
        _ = SimilaritySearch(section, "memo");

        Assert.Equal(
            "2 blocks read page overviews, but Presentation, memo documents get none. Give each an overview prompt, or set a default.",
            PageOverviewCoverage.FindGap(workflow)!.Message);
    }

    [Fact]
    public void TheMessage_AsksForADefault_WhenTheWorkflowHasNoClasses()
    {
        Workflow workflow = Workflow(out Section section);
        _ = new Block(BlockTypes.BuildTopicsForAllDocuments, section, "topics");

        Assert.Equal(
            "1 block reads page overviews, but documents get none. Set a default overview prompt.",
            PageOverviewCoverage.FindGap(workflow)!.Message);
    }

    [Fact]
    public void SourceLabel_NamesWhereAClassGetsItsPrompt()
    {
        var workflow = new Workflow("root");
        DocumentClass own = new(workflow, "own") { PageOverviews = _prompts };
        DocumentClass inherits = new(workflow, "inherits");

        Assert.Equal("Own", PageOverviewCoverage.SourceLabel(own, workflowDefault: null));
        Assert.Equal("Workflow default", PageOverviewCoverage.SourceLabel(inherits, _prompts));
        Assert.Equal("None", PageOverviewCoverage.SourceLabel(inherits, workflowDefault: null));
    }

    private static Workflow Workflow(out Section section)
    {
        var workflow = new Workflow("root");
        section = new Section("Analysis", workflow);

        return workflow;
    }

    private static Block SimilaritySearch(Section section, string documentClassKey)
    {
        Block block = new(BlockTypes.AiQuery, section, $"search_{section.Blocks.Count}")
        {
            Properties = new AiQueryProperties(),
            SupportedDocumentClasses = [documentClassKey],
        };
        _ = new RagSettings(block) { Type = RagTypes.TopNPagesPerFilterWithTwoNeighbors };

        return block;
    }
}
