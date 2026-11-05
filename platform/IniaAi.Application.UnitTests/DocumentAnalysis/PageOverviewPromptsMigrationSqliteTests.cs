using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.DocumentAnalysis;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Workflows.Diff;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Serialization;
using SqliteCoreDatabase = IniaAi.Testing.DependencyInjection.SqliteCoreDatabase;
using SqliteCoreDbContext = IniaAi.Testing.DependencyInjection.SqliteCoreDbContext;
using TestBlocks = IniaAi.Testing.DependencyInjection.TestBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using IniaAi.Testing.DependencyInjection;

namespace IniaAi.Application.UnitTests.DocumentAnalysis;

/// <summary>
/// The migration on a database that enforces foreign keys and rolls transactions back, which the
/// in-memory provider does not.
/// </summary>
public sealed class PageOverviewPromptsMigrationSqliteTests : IDisposable
{
    private const string ORGANIZATION_ID = "IniaAI";
    private const string USER_ID = "user-1";
    private const string PAGES = "page-overviews-generation";
    private const string EXCEL_LAYOUT_ANALYSIS = "excel-layout-analysis";
    private const string COMPANY_REPORT = "company-report";

    private readonly FailOnVersionInterceptor _interceptor = new();
    private readonly SqliteCoreDatabase _database;

    public PageOverviewPromptsMigrationSqliteTests()
    {
        _database = new SqliteCoreDatabase(_interceptor);

        Context.Set<TemplateCategory>().AddRange(
            new TemplateCategory { Id = PAGES, Name = PAGES },
            new TemplateCategory { Id = EXCEL_LAYOUT_ANALYSIS, Name = EXCEL_LAYOUT_ANALYSIS });
        _ = Context.Set<DocumentCategory>().Add(new DocumentCategory { Id = COMPANY_REPORT, Name = COMPANY_REPORT });
        _ = Context.SaveChanges();
        Context.ChangeTracker.Clear();
    }

    private SqliteCoreDbContext Context => _database.Context;

    private ICoreUnitOfWork UnitOfWork => _database.UnitOfWork;

    [Fact]
    public async Task ADeletedCopyOfALivePrompt_DoesNotStopTheMove()
    {
        string live = Seed(PAGES, "universal");
        _ = Seed(PAGES, "an old copy", parentTemplateId: live, isDeleted: true);

        await RunMigrationAsync();

        Assert.Empty(await Context.BlockTemplates.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AKeptOverviewPrompt_WhoseSourceGoes_DoesNotStopTheMove()
    {
        string layout = Seed(EXCEL_LAYOUT_ANALYSIS, "layout");
        string kept = Seed(PAGES, " ", workflowTypeId: "Blank", documentCategoryId: COMPANY_REPORT, parentTemplateId: layout);
        await AddTemplateAsync("Blank", COMPANY_REPORT);

        await RunMigrationAsync();

        Assert.Equal([kept], await Context.BlockTemplates.Select(t => t.Id).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATemplateWhoseSaveFails_IsRolledBack_AndKeepsTheLibraryForTheNextStart()
    {
        _ = Seed(PAGES, "universal");
        await AddTemplateAsync("Corporates", COMPANY_REPORT);
        int versionsBefore = await Context.WorkflowVersions.CountAsync(TestContext.Current.CancellationToken);
        _interceptor.IsArmed = true;

        await RunMigrationAsync();

        Workflow template = (await UnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync("Corporates"))!;
        Assert.Null(template.Configuration?.PageOverviews);
        Assert.Equal(versionsBefore, await Context.WorkflowVersions.CountAsync(TestContext.Current.CancellationToken));
        Assert.NotEmpty(await Context.BlockTemplates.ToListAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    private string Seed(
        string categoryId,
        string userPrompt,
        string? workflowTypeId = null,
        string? documentCategoryId = null,
        string? parentTemplateId = null,
        bool isDeleted = false)
    {
        string id = Guid.NewGuid().ToString();
        _ = Context.Blocks.Add(TestBlocks.AiQuery(
            id,
            userPrompt,
            $"tag_{id[..8]}",
            DateTime.UtcNow,
            new AiQueryProperties { UserPromptTemplate = userPrompt }));
        _ = Context.BlockTemplates.Add(new BlockTemplate
        {
            Id = id,
            TemplateCode = $"template-{id[..8]}",
            BlockType = BlockTypes.AiQuery,
            OrganizationId = ORGANIZATION_ID,
            CategoryId = categoryId,
            WorkflowTypeId = workflowTypeId,
            DocumentCategoryId = documentCategoryId,
            ParentTemplateId = parentTemplateId,
            IsPublicTemplate = true,
            IsDeleted = isDeleted,
            CreatedByUserId = USER_ID,
        });
        _ = Context.SaveChanges();
        Context.ChangeTracker.Clear();

        return id;
    }

    private async Task AddTemplateAsync(string workflowTypeId, string documentCategoryId)
    {
        _ = Context.Set<WorkflowType>().Add(new WorkflowType(workflowTypeId, workflowTypeId));
        _ = await Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var workflow = new Workflow(workflowTypeId);
        _ = new Block(BlockTypes.AiQuery, new Section("Analysis", workflow), "summary")
        {
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise." },
        };
        _ = new DocumentClass(workflow, "report") { DocumentCategoryId = documentCategoryId };

        ErrorOr<SaveWorkflowResult> saved = await new SaveWorkflowCommandHandler(
                CreatePipeline(),
                Context,
                NullLogger<SaveWorkflowCommandHandler>.Instance)
            .Handle(new SaveWorkflowCommand(ORGANIZATION_ID, USER_ID, workflowTypeId, workflow), TestContext.Current.CancellationToken);
        Assert.False(saved.IsError);
        Context.ChangeTracker.Clear();
    }

    private async Task RunMigrationAsync()
    {
        Context.ChangeTracker.Clear();
        await new PageOverviewPromptsMigration(
                UnitOfWork,
                Context,
                CreatePipeline(),
                NullLogger<PageOverviewPromptsMigration>.Instance)
            .RunAsync(TestContext.Current.CancellationToken);
        Context.ChangeTracker.Clear();
    }

    private WorkflowSavePipeline CreatePipeline()
    {
        return new WorkflowSavePipeline(
            UnitOfWork,
            Context,
            new WorkflowCanonicalizer(),
            new WorkflowDiffService(),
            new WorkflowNarrativeGenerator(),
            Options.Create(new AgentRuntimeSettings()),
            NullLogger<WorkflowSavePipeline>.Instance, SuperAdminLookups.OverEmptyUserData());
    }

    private sealed class FailOnVersionInterceptor : SaveChangesInterceptor
    {
        public bool IsArmed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (IsArmed && eventData.Context!.ChangeTracker.Entries<WorkflowVersion>().Any(entry => entry.State == EntityState.Added))
            {
                throw new DbUpdateException("A version insert failed.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
