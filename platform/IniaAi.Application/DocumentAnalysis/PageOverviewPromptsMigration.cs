using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.DocumentAnalysis;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.DocumentAnalysis;

/// <summary>
/// Moves what the retired Prompt Library resolved to the places that replace it. Each template gets
/// the overview prompts the library's four-tier lookup gave its runs, resolved for the template's
/// own organization: the prompt a document without a class got becomes the workflow's default, and
/// a document class whose prompt differs keeps its own. The library's general Excel and topic
/// prompts overwrite the Tools Settings defaults, and their rows go at once, so a retry never
/// overwrites a Tools Settings edit. Each template saves in its own transaction: one the save
/// refuses, or that fails, is rolled back and the overview rows stay, so the next start tries
/// again. Once every template has its prompts the overview rows go too, which makes every later
/// start a no-op. A template already moved, recognised by its version narrative, or one that
/// already has overview prompts is left alone, so an edit made since is never overwritten.
/// </summary>
public sealed class PageOverviewPromptsMigration : IStartupDataMigration
{
    public const string VERSION_NARRATIVE = "Page overview prompts moved here from the Prompt Library.";

    private const string ACTOR = "system";

    private const string PAGES = "page-overviews-generation";
    private const string SINGLE_PAGE = "page-overviews-generation-single-page";
    private const string EXCEL_LAYOUT_ANALYSIS = "excel-layout-analysis";
    private const string EXCEL_HEADER_DATABASE = "excel-header-identification-database";
    private const string EXCEL_HEADER_MULTI_NO_SHARED = "excel-header-identification-multi-no-shared";
    private const string EXCEL_HEADER_MULTI_SHARED = "excel-header-identification-multi-shared";
    private const string TOPICS_ANALYSIS = "topics-analysis";
    private const string GROUPING_BY_TOPICS = "grouping-by-topics";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ICoreDbContext _coreDbContext;
    private readonly WorkflowSavePipeline _savePipeline;
    private readonly ILogger<PageOverviewPromptsMigration> _logger;

    public PageOverviewPromptsMigration(
        ICoreUnitOfWork coreUnitOfWork,
        ICoreDbContext coreDbContext,
        WorkflowSavePipeline savePipeline,
        ILogger<PageOverviewPromptsMigration> logger)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _coreDbContext = coreDbContext;
        _savePipeline = savePipeline;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken cancellationToken)
    {
        return _coreDbContext.CreateExecutionStrategy().ExecuteAsync(MigrateAsync, cancellationToken);
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();

        List<BlockTemplate> library = await _coreUnitOfWork.Repository<BlockTemplate>()
            .GetAll()
            .AsNoTracking()
            .Include(template => template.Block)
            .ToListAsync(cancellationToken);
        if (library.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Moving {Count} Prompt Library prompts into workflows and Tools Settings.",
            library.Count);

        List<BlockTemplate> overviewPrompts = [.. library.Where(IsOverviewPrompt)];

        await InTransactionAsync(
            async () =>
            {
                await CarryToolsSettingsPromptsAsync(library, cancellationToken);
                await RemoveFromLibraryAsync(template => !IsOverviewPrompt(template), cancellationToken);
            },
            cancellationToken);

        HashSet<BlockTemplate> used = [];
        if (!await CarryOverviewPromptsAsync(overviewPrompts, used, cancellationToken))
        {
            _logger.LogError(
                "The Prompt Library keeps its overview prompts until every template takes them; the next start tries again.");
            return;
        }

        foreach (BlockTemplate unused in overviewPrompts.Except(used))
        {
            _logger.LogWarning(
                "Prompt Library overview prompt {TemplateCode} (workflow type {WorkflowTypeId}, document category {DocumentCategoryId}, organization {OrganizationId}) went to no template in this start and is deleted.",
                unused.TemplateCode,
                unused.WorkflowTypeId ?? "any",
                unused.DocumentCategoryId ?? "any",
                unused.IsPublicTemplate ? "any" : unused.OrganizationId);
        }

        await InTransactionAsync(() => RemoveFromLibraryAsync(_ => true, cancellationToken), cancellationToken);
    }

    private static bool IsOverviewPrompt(BlockTemplate template)
    {
        return template.CategoryId is PAGES or SINGLE_PAGE;
    }

    private async Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        _coreDbContext.ClearChangeTracker();
        await using IDbContextTransaction transaction = await _coreDbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            await work();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            _coreDbContext.ClearChangeTracker();
        }
    }

    /// <summary>
    /// Deletes the library rows <paramref name="isRemoved"/> picks, soft-deleted ones included. The
    /// parent links go first: a soft-deleted copy the lookup never read may point at a live row, and
    /// a kept row may point at a removed one.
    /// </summary>
    private async Task RemoveFromLibraryAsync(Func<BlockTemplate, bool> isRemoved, CancellationToken cancellationToken)
    {
        List<BlockTemplate> rows = await _coreUnitOfWork.Repository<BlockTemplate>()
            .GetAll()
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        foreach (BlockTemplate row in rows.Where(row => row.ParentTemplateId is not null))
        {
            row.ParentTemplateId = null;
        }

        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        List<BlockTemplate> removed = [.. rows.Where(isRemoved)];
        List<string> blockIds = [.. removed.Select(template => template.Id)];
        List<Block> blocks = await _coreUnitOfWork.Repository<Block>()
            .GetAll()
            .IgnoreQueryFilters()
            .Where(block => blockIds.Contains(block.Id))
            .ToListAsync(cancellationToken);
        _coreUnitOfWork.Repository<BlockTemplate>().HardRemoveRange(removed);
        _coreUnitOfWork.Repository<Block>().HardRemoveRange(blocks);
        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
    }

    private async Task CarryToolsSettingsPromptsAsync(List<BlockTemplate> library, CancellationToken cancellationToken)
    {
        ExcelAnalysisPrompts excel = await _coreUnitOfWork.Repository<ExcelAnalysisPrompts>()
                .GetFirstOrDefaultAsync(new LatestExcelAnalysisPromptsSpec(tracked: true), cancellationToken)
            ?? Added(ExcelAnalysisPromptsDefaults.Create());
        TopicsPrompts topics = await _coreUnitOfWork.Repository<TopicsPrompts>()
                .GetFirstOrDefaultAsync(new LatestTopicsPromptsSpec(tracked: true), cancellationToken)
            ?? Added(TopicsPromptsDefaults.Create());

        BlockTemplate?[] carried =
        [
            Carry(library, EXCEL_LAYOUT_ANALYSIS, (system, user) => (excel.LayoutAnalysisSystemPrompt, excel.LayoutAnalysisUserPrompt) = (system, user)),
            Carry(library, EXCEL_HEADER_DATABASE, (system, user) => (excel.HeaderDatabaseSystemPrompt, excel.HeaderDatabaseUserPrompt) = (system, user)),
            Carry(library, EXCEL_HEADER_MULTI_NO_SHARED, (system, user) => (excel.HeaderMultiNoSharedSystemPrompt, excel.HeaderMultiNoSharedUserPrompt) = (system, user)),
            Carry(library, EXCEL_HEADER_MULTI_SHARED, (system, user) => (excel.HeaderMultiSharedSystemPrompt, excel.HeaderMultiSharedUserPrompt) = (system, user)),
            Carry(library, TOPICS_ANALYSIS, (system, user) => (topics.TopicsAnalysisSystemPrompt, topics.TopicsAnalysisUserPrompt) = (system, user)),
            Carry(library, GROUPING_BY_TOPICS, (system, user) => (topics.TopicGroupingSystemPrompt, topics.TopicGroupingUserPrompt) = (system, user)),
        ];

        foreach (BlockTemplate dropped in library
            .Where(template => template.CategoryId is not (PAGES or SINGLE_PAGE))
            .Except(carried.OfType<BlockTemplate>()))
        {
            _logger.LogWarning(
                "Prompt Library prompt {TemplateCode} ({CategoryId}, workflow type {WorkflowTypeId}, document category {DocumentCategoryId}) is not carried: Tools Settings holds one prompt per use.",
                dropped.TemplateCode,
                dropped.CategoryId,
                dropped.WorkflowTypeId ?? "any",
                dropped.DocumentCategoryId ?? "any");
        }
    }

    private T Added<T>(T prompts)
        where T : class
    {
        _ = _coreDbContext.Add(prompts);

        return prompts;
    }

    private static BlockTemplate? Carry(List<BlockTemplate> library, string categoryId, Action<string?, string> apply)
    {
        BlockTemplate? general = Preferred(
            library.Where(template => template.CategoryId == categoryId
                && template.WorkflowTypeId is null
                && template.DocumentCategoryId is null),
            organizationId: null);

        if (general?.Block?.Properties is not AiQueryProperties { UserPromptTemplate: { Length: > 0 } userPrompt } properties)
        {
            return null;
        }

        apply(properties.SystemPromptTemplate, userPrompt);

        return general;
    }

    private async Task<bool> CarryOverviewPromptsAsync(
        List<BlockTemplate> overviewPrompts,
        HashSet<BlockTemplate> used,
        CancellationToken cancellationToken)
    {
        if (overviewPrompts.Count == 0)
        {
            return true;
        }

        List<string> workflowTypeIds = await _coreDbContext.Workflows
            .AsNoTracking()
            .Where(workflow => workflow.WorkflowTypeId != null)
            .Select(workflow => workflow.WorkflowTypeId!)
            .Distinct()
            .ToListAsync(cancellationToken);
        HashSet<string> alreadyMoved = [.. await _coreDbContext.WorkflowVersions
            .AsNoTracking()
            .Where(version => version.AutoNarrativeSummary == VERSION_NARRATIVE)
            .Select(version => version.WorkflowTypeId)
            .ToListAsync(cancellationToken)];

        bool everyTemplateSaved = true;
        foreach (string workflowTypeId in workflowTypeIds.Where(id => !alreadyMoved.Contains(id)).Order(StringComparer.Ordinal))
        {
            _coreDbContext.ClearChangeTracker();
            Workflow? template = await _coreUnitOfWork.Workflows.GetByTypeIdAsNoTrackingAsync(workflowTypeId);
            if (template is null || HasOverviewPrompts(template))
            {
                continue;
            }

            used.UnionWith(RowsUsedBy(template, overviewPrompts));
            if (!AssignOverviewPrompts(template, overviewPrompts))
            {
                continue;
            }

            if (FirstChangedClass(template, overviewPrompts) is { } changedClass)
            {
                everyTemplateSaved = false;
                _logger.LogError(
                    "Workflow {WorkflowTypeId} keeps the Prompt Library: moving its prompts would change the overview prompt of document class {DocumentClass}.",
                    workflowTypeId,
                    changedClass);
                continue;
            }

            everyTemplateSaved &= await SaveAsync(template, workflowTypeId, cancellationToken);
        }

        _coreDbContext.ClearChangeTracker();

        return everyTemplateSaved;
    }

    private async Task<bool> SaveAsync(Workflow template, string workflowTypeId, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await _coreDbContext.BeginTransactionAsync(cancellationToken);
        try
        {
            ErrorOr<SaveWorkflowResult> save = await _savePipeline.SaveCoreAsync(
                new SaveWorkflowCommand(
                    template.OrganizationId ?? string.Empty,
                    ACTOR,
                    workflowTypeId,
                    template,
                    ConfirmSignatureFieldDeletions: true,
                    SkipAuthorGates: true),
                VERSION_NARRATIVE,
                cancellationToken);
            if (save.IsError)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(
                    "Workflow {WorkflowTypeId} did not take its overview prompts from the Prompt Library: {Errors}",
                    workflowTypeId,
                    string.Join("; ", save.Errors.Select(error => error.Description)));

                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Workflow {WorkflowTypeId} took its overview prompts from the Prompt Library.", workflowTypeId);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                exception,
                "Workflow {WorkflowTypeId} did not take its overview prompts from the Prompt Library; the next start tries again.",
                workflowTypeId);

            return false;
        }
    }

    private static bool HasOverviewPrompts(Workflow template)
    {
        return template.Configuration?.PageOverviews is { IsEmpty: false }
            || template.DocumentClasses.Any(documentClass => documentClass.PageOverviews is { IsEmpty: false });
    }

    private static IEnumerable<DocumentClass> InputClassesWithACategory(Workflow template)
    {
        return template.DocumentClasses.Where(documentClass => !documentClass.IsOutputDocument
            && !string.IsNullOrEmpty(documentClass.DocumentCategoryId));
    }

    private static IEnumerable<BlockTemplate> RowsUsedBy(Workflow template, List<BlockTemplate> overviewPrompts)
    {
        IEnumerable<string?> documentCategoryIds = InputClassesWithACategory(template)
            .Select(documentClass => documentClass.DocumentCategoryId)
            .Prepend(null);
        foreach (string? documentCategoryId in documentCategoryIds)
        {
            foreach (string categoryId in new[] { PAGES, SINGLE_PAGE })
            {
                if (Match(overviewPrompts, categoryId, template.WorkflowTypeId, documentCategoryId, template.OrganizationId) is { } row)
                {
                    yield return row;
                }
            }
        }
    }

    private static bool AssignOverviewPrompts(Workflow template, List<BlockTemplate> overviewPrompts)
    {
        string? organizationId = template.OrganizationId;
        string? workflowTypeId = template.WorkflowTypeId;

        PageOverviewPrompt? defaultPages = Lookup(overviewPrompts, PAGES, workflowTypeId, null, organizationId);
        PageOverviewPrompt? defaultSinglePage = LookupSinglePage(overviewPrompts, workflowTypeId, null, organizationId);
        var workflowDefault = new PageOverviewPrompts
        {
            Pages = defaultPages,
            SinglePage = PageOverviewPrompt.AreSame(defaultSinglePage, defaultPages) ? null : defaultSinglePage,
        };

        bool assigned = !workflowDefault.IsEmpty;
        if (assigned)
        {
            template.Configuration ??= new WorkflowConfiguration();
            template.Configuration.PageOverviews = workflowDefault;
        }

        foreach (DocumentClass documentClass in InputClassesWithACategory(template))
        {
            PageOverviewPrompt? pages = Lookup(overviewPrompts, PAGES, workflowTypeId, documentClass.DocumentCategoryId, organizationId);
            PageOverviewPrompt? singlePage = LookupSinglePage(overviewPrompts, workflowTypeId, documentClass.DocumentCategoryId, organizationId);

            PageOverviewPrompt? classPages = PageOverviewPrompt.AreSame(pages, defaultPages) ? null : pages;
            PageOverviewPrompt? singlePageWithoutItsOwn = workflowDefault.SinglePage ?? classPages ?? defaultPages;
            PageOverviewPrompt? classSinglePage = PageOverviewPrompt.AreSame(singlePage, singlePageWithoutItsOwn) ? null : singlePage;

            if (classPages is not null || classSinglePage is not null)
            {
                documentClass.PageOverviews = new PageOverviewPrompts { Pages = classPages, SinglePage = classSinglePage };
                assigned = true;
            }
        }

        return assigned;
    }

    /// <summary>
    /// The first document class (or "(none)" for a document without one) whose prompt after the move
    /// differs from what the library gave it; null when every prompt stays the same.
    /// </summary>
    private static string? FirstChangedClass(Workflow template, List<BlockTemplate> overviewPrompts)
    {
        PageOverviewPrompts? workflowDefault = template.Configuration?.PageOverviews;
        foreach (DocumentClass? documentClass in template.DocumentClasses.Where(c => !c.IsOutputDocument).Prepend(null))
        {
            string? documentCategoryId = string.IsNullOrEmpty(documentClass?.DocumentCategoryId) ? null : documentClass.DocumentCategoryId;
            foreach (bool singlePage in new[] { false, true })
            {
                PageOverviewPrompt? before = singlePage
                    ? LookupSinglePage(overviewPrompts, template.WorkflowTypeId, documentCategoryId, template.OrganizationId)
                    : Lookup(overviewPrompts, PAGES, template.WorkflowTypeId, documentCategoryId, template.OrganizationId);
                PageOverviewPrompt? after = PageOverviewPrompts.Resolve(documentClass?.PageOverviews, workflowDefault, singlePage);
                if (!PageOverviewPrompt.AreSame(before, after))
                {
                    return documentClass?.Key ?? "(none)";
                }
            }
        }

        return null;
    }

    private static PageOverviewPrompt? LookupSinglePage(
        List<BlockTemplate> overviewPrompts,
        string? workflowTypeId,
        string? documentCategoryId,
        string? organizationId)
    {
        return Lookup(overviewPrompts, SINGLE_PAGE, workflowTypeId, documentCategoryId, organizationId)
            ?? Lookup(overviewPrompts, PAGES, workflowTypeId, documentCategoryId, organizationId);
    }

    private static PageOverviewPrompt? Lookup(
        List<BlockTemplate> overviewPrompts,
        string categoryId,
        string? workflowTypeId,
        string? documentCategoryId,
        string? organizationId)
    {
        return Match(overviewPrompts, categoryId, workflowTypeId, documentCategoryId, organizationId)?.Block?.Properties is AiQueryProperties properties
            ? new PageOverviewPrompt
            {
                SystemPrompt = properties.SystemPromptTemplate,
                UserPrompt = properties.UserPromptTemplate ?? string.Empty,
                ShouldUseAdvancedModel = properties.ShouldUseAdvancedModel,
            }
            : null;
    }

    /// <summary>
    /// The library's four tiers, most specific first: workflow type and document category, workflow
    /// type alone, document category alone, then the general prompt.
    /// </summary>
    private static BlockTemplate? Match(
        List<BlockTemplate> overviewPrompts,
        string categoryId,
        string? workflowTypeId,
        string? documentCategoryId,
        string? organizationId)
    {
        List<BlockTemplate> visible = [.. overviewPrompts.Where(template => template.CategoryId == categoryId
            && (template.IsPublicTemplate || template.OrganizationId == organizationId))];

        return Preferred(visible.Where(t => workflowTypeId is not null && documentCategoryId is not null && t.WorkflowTypeId == workflowTypeId && t.DocumentCategoryId == documentCategoryId), organizationId)
            ?? Preferred(visible.Where(t => workflowTypeId is not null && t.WorkflowTypeId == workflowTypeId && t.DocumentCategoryId is null), organizationId)
            ?? Preferred(visible.Where(t => documentCategoryId is not null && t.WorkflowTypeId is null && t.DocumentCategoryId == documentCategoryId), organizationId)
            ?? Preferred(visible.Where(t => t.WorkflowTypeId is null && t.DocumentCategoryId is null), organizationId);
    }

    private static BlockTemplate? Preferred(IEnumerable<BlockTemplate> matches, string? organizationId)
    {
        return matches
            .OrderByDescending(t => organizationId is not null && t.OrganizationId == organizationId)
            .ThenByDescending(t => t.IsSystemTemplate)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
