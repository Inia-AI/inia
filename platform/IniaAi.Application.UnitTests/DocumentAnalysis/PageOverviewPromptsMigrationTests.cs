using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.DocumentAnalysis;
using IniaAi.Application.UnitTests.Common.SharedComponents;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.DocumentAnalysis;

public sealed class PageOverviewPromptsMigrationTests : IDisposable
{
    private const string ORGANIZATION_ID = "IniaAI";
    private const string USER_ID = "user-1";
    private const string PAGES = "page-overviews-generation";
    private const string SINGLE_PAGE = "page-overviews-generation-single-page";
    private const string COMPANY_REPORT = "company-report";
    private const string FINANCIAL_MODEL = "financial-model";
    private const string LEGAL_AGREEMENT = "legal-agreement";
    private const string PRESENTATION = "presentation";

    private readonly SharedComponentsTestHarness _harness = new();

    [Fact]
    public async Task EveryClass_ResolvesTheSamePrompt_AsTheLibraryGaveIt()
    {
        SeedLibrary();
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"), (FINANCIAL_MODEL, "model"), (PRESENTATION, "deck"), (null, "notes"));
        await AddTemplateAsync("Funds", (COMPANY_REPORT, "report"), (LEGAL_AGREEMENT, "contract"));
        await AddTemplateAsync("Generic", (COMPANY_REPORT, "report"), (LEGAL_AGREEMENT, "contract"));

        await RunMigrationAsync();

        Assert.Equal(
            [
                ("Corporates", "deck", "universal", "universal single"),
                ("Corporates", "model", "universal", "corporates model single"),
                ("Corporates", "notes", "universal", "universal single"),
                ("Corporates", "report", "corporates report", "universal single"),
                ("Corporates", "", "universal", "universal single"),
                ("Funds", "contract", "funds", "universal single"),
                ("Funds", "report", "funds", "universal single"),
                ("Funds", "", "funds", "universal single"),
                ("Generic", "contract", "legal", "universal single"),
                ("Generic", "report", "universal", "universal single"),
                ("Generic", "", "universal", "universal single"),
            ],
            await ResolvedPromptsAsync());
    }

    [Fact]
    public async Task AClassThatResolvesLikeTheDefault_CarriesNoPromptOfItsOwn()
    {
        SeedLibrary();
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"), (PRESENTATION, "deck"));

        await RunMigrationAsync();

        Workflow template = await LoadAsync("Corporates");
        Assert.Equal("universal", template.Configuration!.PageOverviews!.Pages!.UserPrompt);
        Assert.Equal("corporates report", Class(template, "report").PageOverviews!.Pages!.UserPrompt);
        Assert.Null(Class(template, "report").PageOverviews!.SinglePage);
        Assert.Null(Class(template, "deck").PageOverviews);
    }

    [Fact]
    public async Task ThePromptKeepsItsSystemPromptAndModelTier()
    {
        Seed(PAGES, "universal", systemPrompt: "You are an analyst.", shouldUseAdvancedModel: true);
        await AddTemplateAsync("Generic");

        await RunMigrationAsync();

        PageOverviewPrompt pages = (await LoadAsync("Generic")).Configuration!.PageOverviews!.Pages!;
        Assert.Equal(("You are an analyst.", "universal", true), (pages.SystemPrompt, pages.UserPrompt, pages.ShouldUseAdvancedModel));
    }

    [Fact]
    public async Task AnotherOrganizationsPrivatePrompt_IsNotCarried()
    {
        SeedLibrary();
        Seed(PAGES, "other organization", workflowTypeId: "Generic", organizationId: "OtherOrg", isPublic: false);
        await AddTemplateAsync("Generic", (COMPANY_REPORT, "report"));

        await RunMigrationAsync();

        Assert.Equal("universal", (await LoadAsync("Generic")).Configuration!.PageOverviews!.Pages!.UserPrompt);
    }

    [Fact]
    public async Task EachMigratedTemplate_GetsAHeadVersionThatMatchesItsLiveContent()
    {
        SeedLibrary();
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"));
        int versionsBefore = await VersionCountAsync("Corporates");

        await RunMigrationAsync();

        Workflow live = await LoadAsync("Corporates");
        WorkflowVersion head = await _harness.Context.WorkflowVersions
            .Where(v => v.WorkflowTypeId == "Corporates")
            .OrderByDescending(v => v.CreatedAt)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(versionsBefore + 1, await VersionCountAsync("Corporates"));
        Assert.Equal(_harness.Canonicalizer.ComputeContentHash(live), head.ContentHash);
        Assert.Equal(PageOverviewPromptsMigration.VERSION_NARRATIVE, head.AutoNarrativeSummary);
    }

    [Fact]
    public async Task TheLibraryIsEmptied_SoASecondStartChangesNothing()
    {
        SeedLibrary();
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"));

        await RunMigrationAsync();
        int versionsAfterFirstRun = await VersionCountAsync("Corporates");
        await RunMigrationAsync();

        Assert.Empty(await _harness.Context.BlockTemplates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(versionsAfterFirstRun, await VersionCountAsync("Corporates"));
    }

    [Fact]
    public async Task EditedExcelAndTopicPrompts_MoveToToolsSettings()
    {
        _harness.Context.ExcelAnalysisPrompts.Add(ExcelAnalysisPromptsDefaults.Create());
        _harness.Context.TopicsPrompts.Add(TopicsPromptsDefaults.Create());
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        Seed("excel-layout-analysis", "edited layout", systemPrompt: "edited layout system");
        Seed("excel-header-identification-database", "edited database headers");
        Seed("grouping-by-topics", "edited grouping");
        Seed("grouping-by-topics", "fund grouping", workflowTypeId: "HedgeFundsMonitoring");

        await RunMigrationAsync();

        ExcelAnalysisPrompts excel = await _harness.Context.ExcelAnalysisPrompts.OrderByDescending(p => p.CreatedAt).FirstAsync(TestContext.Current.CancellationToken);
        TopicsPrompts topics = await _harness.Context.TopicsPrompts.OrderByDescending(p => p.CreatedAt).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("edited layout system", "edited layout"), (excel.LayoutAnalysisSystemPrompt, excel.LayoutAnalysisUserPrompt));
        Assert.Equal("edited database headers", excel.HeaderDatabaseUserPrompt);
        Assert.Equal(ExcelAnalysisPromptsDefaults.EXCEL_HEADER_MULTI_SHARED_USER_PROMPT, excel.HeaderMultiSharedUserPrompt);
        Assert.Equal("edited grouping", topics.TopicGroupingUserPrompt);
        Assert.Equal(TopicsPromptsDefaults.TOPICS_ANALYSIS_USER_PROMPT, topics.TopicsAnalysisUserPrompt);
    }

    [Fact]
    public async Task ATemplateAnAuthorCouldNotSave_StillTakesItsPrompts()
    {
        SeedLibrary();
        await AddAgentTemplateWhileTheRuntimeIsOffAsync();

        await RunMigrationAsync();

        Assert.Equal("universal", (await LoadAsync("Agentic")).Configuration!.PageOverviews!.Pages!.UserPrompt);
        Assert.Empty(await _harness.Context.BlockTemplates.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATemplateWhosePromptWouldChange_KeepsTheLibraryForTheNextStart()
    {
        SeedLibrary();
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"));
        await AddTemplateWithABlankLibraryPromptAsync();

        await RunMigrationAsync();

        Assert.NotEmpty(await _harness.Context.BlockTemplates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null((await LoadAsync("Blank")).Configuration?.PageOverviews);
        Assert.Equal("corporates report", Class(await LoadAsync("Corporates"), "report").PageOverviews!.Pages!.UserPrompt);
    }

    [Fact]
    public async Task AToolsSettingsEdit_SurvivesTheNextStart_WhileATemplateKeepsTheLibrary()
    {
        _harness.Context.ExcelAnalysisPrompts.Add(ExcelAnalysisPromptsDefaults.Create());
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        SeedLibrary();
        Seed("excel-layout-analysis", "library layout");
        await AddTemplateWithABlankLibraryPromptAsync();
        await RunMigrationAsync();
        ExcelAnalysisPrompts excel = await _harness.Context.ExcelAnalysisPrompts.OrderByDescending(p => p.CreatedAt).FirstAsync(TestContext.Current.CancellationToken);
        excel.LayoutAnalysisUserPrompt = "edited in Tools Settings";
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await RunMigrationAsync();

        Assert.Equal(
            "edited in Tools Settings",
            (await _harness.Context.ExcelAnalysisPrompts.OrderByDescending(p => p.CreatedAt).FirstAsync(TestContext.Current.CancellationToken)).LayoutAnalysisUserPrompt);
        Assert.All(
            await _harness.Context.BlockTemplates.ToListAsync(TestContext.Current.CancellationToken),
            template => Assert.Contains(template.CategoryId, new[] { PAGES, SINGLE_PAGE }));
    }

    [Fact]
    public async Task AnOutputClass_GetsNoPrompt_AndDoesNotHoldTheMoveBack()
    {
        SeedLibrary();
        await AddTemplateAsync(
            "Corporates",
            configure: workflow => _ = new DocumentClass(workflow, "spread") { DocumentCategoryId = FINANCIAL_MODEL, IsOutputDocument = true },
            (COMPANY_REPORT, "report"));

        await RunMigrationAsync();

        Assert.Null(Class(await LoadAsync("Corporates"), "spread").PageOverviews);
        Assert.Empty(await _harness.Context.BlockTemplates.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATemplateThatAlreadyHasPrompts_IsLeftAlone_WhileATemplateKeepsTheLibrary()
    {
        SeedLibrary();
        await AddTemplateWithABlankLibraryPromptAsync();
        await RunMigrationAsync();
        await AddTemplateAsync("Funds", configure: workflow => workflow.Configuration = new WorkflowConfiguration
        {
            PageOverviews = new PageOverviewPrompts { Pages = new PageOverviewPrompt { UserPrompt = "written by an author" } },
        });
        int versionsBefore = await VersionCountAsync("Funds");

        await RunMigrationAsync();

        Assert.Equal("written by an author", (await LoadAsync("Funds")).Configuration!.PageOverviews!.Pages!.UserPrompt);
        Assert.Equal(versionsBefore, await VersionCountAsync("Funds"));
    }

    [Fact]
    public async Task ATemplateAlreadyMoved_KeepsAnEditMadeBeforeTheNextStart()
    {
        SeedLibrary();
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"));
        await AddTemplateWithABlankLibraryPromptAsync();
        await RunMigrationAsync();
        Workflow edited = await LoadAsync("Corporates");
        edited.Configuration!.PageOverviews!.Pages!.UserPrompt = "edited in the editor";
        (_, SaveWorkflowCommandHandler saveHandler) = _harness.CreateRealSaveSender();
        _ = await saveHandler.Handle(new SaveWorkflowCommand(ORGANIZATION_ID, USER_ID, "Corporates", edited), TestContext.Current.CancellationToken);

        await RunMigrationAsync();

        Assert.Equal("edited in the editor", (await LoadAsync("Corporates")).Configuration!.PageOverviews!.Pages!.UserPrompt);
    }

    [Fact]
    public async Task WithoutALibrary_NoTemplateChanges()
    {
        await AddTemplateAsync("Corporates", (COMPANY_REPORT, "report"));
        int versionsBefore = await VersionCountAsync("Corporates");

        await RunMigrationAsync();

        Assert.Null((await LoadAsync("Corporates")).Configuration?.PageOverviews);
        Assert.Equal(versionsBefore, await VersionCountAsync("Corporates"));
    }

    public void Dispose()
    {
        _harness.Dispose();
    }

    private void SeedLibrary()
    {
        Seed(PAGES, "universal");
        Seed(SINGLE_PAGE, "universal single");
        Seed(PAGES, "corporates report", workflowTypeId: "Corporates", documentCategoryId: COMPANY_REPORT);
        Seed(SINGLE_PAGE, "corporates model single", workflowTypeId: "Corporates", documentCategoryId: FINANCIAL_MODEL);
        Seed(PAGES, "funds", workflowTypeId: "Funds");
        Seed(PAGES, "legal", documentCategoryId: LEGAL_AGREEMENT);
    }

    private void Seed(
        string categoryId,
        string userPrompt,
        string? workflowTypeId = null,
        string? documentCategoryId = null,
        string? organizationId = ORGANIZATION_ID,
        bool isPublic = true,
        string? systemPrompt = null,
        bool shouldUseAdvancedModel = false)
    {
        string id = Guid.NewGuid().ToString();
        _ = _harness.Context.Blocks.Add(TestBlocks.AiQuery(
            id,
            userPrompt,
            $"tag_{id[..8]}",
            DateTime.UtcNow,
            new AiQueryProperties
            {
                SystemPromptTemplate = systemPrompt,
                UserPromptTemplate = userPrompt,
                ShouldUseAdvancedModel = shouldUseAdvancedModel,
            }));
        _ = _harness.Context.BlockTemplates.Add(new BlockTemplate
        {
            Id = id,
            TemplateCode = $"template-{id[..8]}",
            BlockType = BlockTypes.AiQuery,
            OrganizationId = organizationId,
            CategoryId = categoryId,
            WorkflowTypeId = workflowTypeId,
            DocumentCategoryId = documentCategoryId,
            IsPublicTemplate = isPublic,
            CreatedByUserId = USER_ID,
        });
        _ = _harness.Context.SaveChanges();
        _harness.Context.ChangeTracker.Clear();
    }

    private Task AddTemplateAsync(string workflowTypeId, params (string? DocumentCategoryId, string Key)[] classes)
    {
        return AddTemplateAsync(workflowTypeId, configure: null, classes);
    }

    private async Task AddTemplateAsync(string workflowTypeId, Action<Workflow>? configure, params (string? DocumentCategoryId, string Key)[] classes)
    {
        var workflow = new Workflow(workflowTypeId);
        configure?.Invoke(workflow);
        var section = new Section("Analysis", workflow);
        _ = new Block(BlockTypes.AiQuery, section, "summary")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise." },
        };
        foreach ((string? documentCategoryId, string key) in classes)
        {
            _ = new DocumentClass(workflow, key) { DocumentCategoryId = documentCategoryId };
        }

        (_, SaveWorkflowCommandHandler saveHandler) = _harness.CreateRealSaveSender();
        _ = await _harness.SaveTemplateAsync(saveHandler, ORGANIZATION_ID, USER_ID, workflowTypeId, workflow);
        _harness.Context.ChangeTracker.Clear();
    }

    private async Task AddAgentTemplateWhileTheRuntimeIsOffAsync()
    {
        var agentic = new Workflow("Agentic");
        _ = new Block(BlockTypes.Agent, new Section("Research", agentic), "research") { Properties = new AgentProperties() };
        agentic.WorkflowTypeId = "Agentic";
        agentic.OrganizationId = ORGANIZATION_ID;
        agentic.UserId = USER_ID;
        _ = _harness.Context.Workflows.Add(agentic);
        _ = await _harness.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _harness.Context.ChangeTracker.Clear();
    }

    private async Task AddTemplateWithABlankLibraryPromptAsync()
    {
        Seed(PAGES, " ", workflowTypeId: "Blank", documentCategoryId: COMPANY_REPORT);
        await AddTemplateAsync("Blank", (COMPANY_REPORT, "report"));
    }

    private async Task RunMigrationAsync()
    {
        _harness.Context.ChangeTracker.Clear();
        var migration = new PageOverviewPromptsMigration(
            _harness.UnitOfWork,
            _harness.Context,
            _harness.CreatePipeline(),
            NullLogger<PageOverviewPromptsMigration>.Instance);

        await migration.RunAsync(TestContext.Current.CancellationToken);
        _harness.Context.ChangeTracker.Clear();
    }

    private async Task<Workflow> LoadAsync(string workflowTypeId)
    {
        _harness.Context.ChangeTracker.Clear();

        return (await _harness.UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId))!;
    }

    private async Task<int> VersionCountAsync(string workflowTypeId)
    {
        return await _harness.Context.WorkflowVersions.CountAsync(v => v.WorkflowTypeId == workflowTypeId, TestContext.Current.CancellationToken);
    }

    private async Task<List<(string, string, string?, string?)>> ResolvedPromptsAsync()
    {
        List<(string, string, string?, string?)> resolved = [];
        foreach (string workflowTypeId in new[] { "Corporates", "Funds", "Generic" })
        {
            Workflow template = await LoadAsync(workflowTypeId);
            PageOverviewPrompts? workflowDefault = template.Configuration?.PageOverviews;
            foreach (DocumentClass documentClass in template.DocumentClasses.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                resolved.Add((
                    workflowTypeId,
                    documentClass.Key,
                    PageOverviewPrompts.Resolve(documentClass.PageOverviews, workflowDefault, singlePage: false)?.UserPrompt,
                    PageOverviewPrompts.Resolve(documentClass.PageOverviews, workflowDefault, singlePage: true)?.UserPrompt));
            }

            resolved.Add((
                workflowTypeId,
                string.Empty,
                PageOverviewPrompts.Resolve(null, workflowDefault, singlePage: false)?.UserPrompt,
                PageOverviewPrompts.Resolve(null, workflowDefault, singlePage: true)?.UserPrompt));
        }

        return resolved;
    }

    private static DocumentClass Class(Workflow template, string key)
    {
        return template.DocumentClasses.Single(c => c.Key == key);
    }
}
