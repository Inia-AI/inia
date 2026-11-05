using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.PageOverviews;

public class PageOverviewPromptsTests
{
    private const string UNCLOSED_ANSWER = "The answer format opens <answer> but never closes it. End it with </answer>.";
    private const string NO_ANSWER = "The answer format has no <answer> tag. Put the summary between <answer> and </answer>, or pages get no overview.";
    private static readonly PageOverviewPrompt _classPages = new() { UserPrompt = "class pages" };
    private static readonly PageOverviewPrompt _classSinglePage = new() { UserPrompt = "class single page" };
    private static readonly PageOverviewPrompt _defaultPages = new() { UserPrompt = "default pages" };
    private static readonly PageOverviewPrompt _defaultSinglePage = new() { UserPrompt = "default single page" };

    [Fact]
    public void TheClassPrompt_WinsOverTheWorkflowDefault()
    {
        PageOverviewPrompt? resolved = PageOverviewPrompts.Resolve(
            new PageOverviewPrompts { Pages = _classPages },
            new PageOverviewPrompts { Pages = _defaultPages },
            singlePage: false);

        Assert.Same(_classPages, resolved);
    }

    [Fact]
    public void AClassWithoutAPrompt_UsesTheWorkflowDefault()
    {
        PageOverviewPrompt? resolved = PageOverviewPrompts.Resolve(
            classPrompts: null,
            new PageOverviewPrompts { Pages = _defaultPages },
            singlePage: false);

        Assert.Same(_defaultPages, resolved);
    }

    [Fact]
    public void WithNeitherAClassPromptNorADefault_ThereIsNoPrompt()
    {
        Assert.Null(PageOverviewPrompts.Resolve(classPrompts: null, workflowDefault: null, singlePage: false));
        Assert.Null(PageOverviewPrompts.Resolve(classPrompts: null, workflowDefault: null, singlePage: true));
    }

    [Fact]
    public void ABlankPrompt_CountsAsNoPrompt()
    {
        var blank = new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = " ", UserPrompt = "\n" } };

        Assert.Same(_defaultPages, PageOverviewPrompts.Resolve(blank, new PageOverviewPrompts { Pages = _defaultPages }, singlePage: false));
        Assert.Null(PageOverviewPrompts.Resolve(blank, workflowDefault: null, singlePage: false));
    }

    [Fact]
    public void APromptWithoutAUserPrompt_CountsAsNoPrompt()
    {
        var systemOnly = new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = "You summarise pages.", UserPrompt = " " } };

        Assert.True(systemOnly.IsEmpty);
        Assert.Null(PageOverviewPrompts.Resolve(systemOnly, workflowDefault: null, singlePage: false));
    }

    [Fact]
    public void APageReadAlone_UsesTheOnePageVersions_BeforeThePagePrompts()
    {
        var classPrompts = new PageOverviewPrompts { Pages = _classPages, SinglePage = _classSinglePage };
        var workflowDefault = new PageOverviewPrompts { Pages = _defaultPages, SinglePage = _defaultSinglePage };

        Assert.Same(_classSinglePage, PageOverviewPrompts.Resolve(classPrompts, workflowDefault, singlePage: true));
        Assert.Same(_defaultSinglePage, PageOverviewPrompts.Resolve(new PageOverviewPrompts { Pages = _classPages }, workflowDefault, singlePage: true));
        Assert.Same(_classPages, PageOverviewPrompts.Resolve(new PageOverviewPrompts { Pages = _classPages }, new PageOverviewPrompts { Pages = _defaultPages }, singlePage: true));
        Assert.Same(_defaultPages, PageOverviewPrompts.Resolve(classPrompts: null, new PageOverviewPrompts { Pages = _defaultPages }, singlePage: true));
    }

    [Fact]
    public void WithoutBlankPrompts_KeepsOnlyThePromptsThatHaveText()
    {
        var prompts = new PageOverviewPrompts { Pages = _classPages, SinglePage = new PageOverviewPrompt { UserPrompt = " " } };

        PageOverviewPrompts kept = prompts.WithoutBlankPrompts()!;

        Assert.Equal("class pages", kept.Pages!.UserPrompt);
        Assert.NotSame(_classPages, kept.Pages);
        Assert.Null(kept.SinglePage);
        Assert.Null(new PageOverviewPrompts { Pages = new PageOverviewPrompt() }.WithoutBlankPrompts());
    }

    [Fact]
    public void ADocumentClassWithoutPrompts_SerializesExactlyAsBefore()
    {
        var workflow = new Workflow();
        var withoutPrompts = new DocumentClass(workflow, "report") { DisplayName = "Report" };
        var withEmptyPrompts = new DocumentClass(workflow, "deck") { DisplayName = "Deck", PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt() } };

        Assert.DoesNotContain(nameof(DocumentClass.PageOverviews), JsonConvert.SerializeObject(withoutPrompts));
        Assert.DoesNotContain(nameof(DocumentClass.PageOverviews), JsonConvert.SerializeObject(withEmptyPrompts));
    }

    [Fact]
    public void AWorkflowConfigurationWithoutPrompts_SerializesExactlyAsBefore()
    {
        string json = JsonConvert.SerializeObject(new WorkflowConfiguration { Version = "1.0", PageOverviews = new PageOverviewPrompts() });

        Assert.Equal("""{"Version":"1.0"}""", json);
    }

    [Fact]
    public void Prompts_RoundTripThroughTheWorkflowJson()
    {
        var workflow = new Workflow
        {
            Configuration = new WorkflowConfiguration
            {
                PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { SystemPrompt = "sys", UserPrompt = "default", ShouldUseAdvancedModel = true } },
            },
        };
        _ = new DocumentClass(workflow, "report")
        {
            PageOverviews = new PageOverviewPrompts { SinglePage = new PageOverviewPrompt { UserPrompt = "sheet" } },
        };

        Workflow roundTripped = Workflow.FromJson(workflow.ToJson(pretty: false))!;

        PageOverviewPrompt defaultPages = roundTripped.Configuration!.PageOverviews!.Pages!;
        Assert.Equal(("sys", "default", true), (defaultPages.SystemPrompt, defaultPages.UserPrompt, defaultPages.ShouldUseAdvancedModel));
        Assert.Equal("sheet", Assert.Single(roundTripped.DocumentClasses).PageOverviews!.SinglePage!.UserPrompt);
    }

    [Fact]
    public void TheTemplatePayload_CarriesThePrompts_AndTheContentHashCountsThem()
    {
        var canonicalizer = new WorkflowCanonicalizer();
        var workflow = new Workflow("root")
        {
            Configuration = new WorkflowConfiguration { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "default" } } },
        };
        DocumentClass report = new(workflow, "report") { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "class" } } };
        string before = canonicalizer.ComputeContentHash(workflow);

        Workflow fromPayload = Workflow.FromJson(canonicalizer.ToCanonicalJson(workflow))!;
        report.PageOverviews.Pages.UserPrompt = "edited";

        Assert.Equal("default", fromPayload.Configuration!.PageOverviews!.Pages!.UserPrompt);
        Assert.Equal("class", Assert.Single(fromPayload.DocumentClasses).PageOverviews!.Pages!.UserPrompt);
        Assert.NotEqual(before, canonicalizer.ComputeContentHash(workflow));
    }

    [Fact]
    public void TheDatabaseJson_HoldsOnlyThePrompts()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new PageOverviewPrompts { Pages = _classPages });

        Assert.DoesNotContain(nameof(PageOverviewPrompt.IsBlank), json);
        Assert.DoesNotContain(nameof(PageOverviewPrompts.IsEmpty), json);
    }

    [Fact]
    public void Clones_CarryThePrompts_AsTheirOwnInstances()
    {
        var source = new Workflow
        {
            Configuration = new WorkflowConfiguration { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "default" } } },
        };
        var sourceClass = new DocumentClass(source, "report") { PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "class" } } };

        WorkflowConfiguration configurationClone = source.Configuration.Clone();
        DocumentClass classClone = sourceClass.CloneToWorkflow(new Workflow());

        Assert.NotSame(source.Configuration.PageOverviews, configurationClone.PageOverviews);
        Assert.NotSame(source.Configuration.PageOverviews.Pages, configurationClone.PageOverviews!.Pages);
        Assert.Equal("default", configurationClone.PageOverviews.Pages!.UserPrompt);
        Assert.NotSame(sourceClass.PageOverviews, classClone.PageOverviews);
        Assert.NotSame(sourceClass.PageOverviews.Pages, classClone.PageOverviews!.Pages);
        Assert.Equal("class", classClone.PageOverviews.Pages!.UserPrompt);
    }

    [Theory]
    [InlineData("Summarise page {0}:\n\n<answer>\nPage {0}:\n- **Page Description:** [1-2 sentences]\n<answer>")]
    [InlineData("Summarise page {0}:\n\n<answer>Page {0}: [summary]")]
    public void AnAnswerFormatThatNeverClosesTheTag_IsAProblem(string userPrompt)
    {
        Assert.Equal(UNCLOSED_ANSWER, new PageOverviewPrompt { UserPrompt = userPrompt }.AnswerFormatProblem);
    }

    [Fact]
    public void AnAnswerFormatWithoutTheTag_IsAProblem()
    {
        Assert.Equal(NO_ANSWER, new PageOverviewPrompt { UserPrompt = "Summarise page {0}." }.AnswerFormatProblem);
    }

    [Fact]
    public void AClosedAnswerFormat_IsNoProblem_InTheUserPromptOrTheSystemPrompt()
    {
        Assert.Null(new PageOverviewPrompt { UserPrompt = "Summarise page {0}: <ANSWER>Page {0}: [summary]</Answer>" }.AnswerFormatProblem);
        Assert.Null(new PageOverviewPrompt { SystemPrompt = "Reply as <answer>[summary]</answer>.", UserPrompt = "Summarise page {0}." }.AnswerFormatProblem);
    }

    [Fact]
    public void APromptWithoutAUserPrompt_HasNoAnswerFormatProblem()
    {
        Assert.Null(new PageOverviewPrompt { SystemPrompt = "You summarise pages." }.AnswerFormatProblem);
    }

    [Fact]
    public void TheStandardPrompts_HaveNoAnswerFormatProblem()
    {
        PageOverviewPrompts standard = PageOverviewPromptDefaults.Create();

        Assert.Null(standard.Pages!.AnswerFormatProblem);
        Assert.Null(standard.SinglePage!.AnswerFormatProblem);
    }
}
