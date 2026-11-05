using IniaAi.Common.Constants;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows;

/// <summary>
/// Guard for the non-interactive intake skip signal: a SKIPPED_PROTECTED_FILES job input must
/// surface as persisted Skipped log rows on the execution — the durable, operator-visible record
/// that a password-protected file was excluded from the run.
/// </summary>
public class SkippedProtectedFileLogTests : IClassFixture<WorkflowsFixture<SkippedProtectedFileLogTests>>
{
    private const string ORG_ID = "TestOrg";
    private const string PROTECTED_PDF_NAME = "q1-report.pdf";
    private const string PROTECTED_PDF_REASON = "No password available for automatically imported files.";
    private const string DRM_XLSX_NAME = "holdings.xlsx";
    private const string DRM_XLSX_REASON = "Certificate- or DRM-based protection cannot be removed.";

    private readonly IServiceProvider _sp;

    public SkippedProtectedFileLogTests(
        WorkflowsFixture<SkippedProtectedFileLogTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _sp = fixture.BuildTestServiceProvider(testOutputHelper);
    }

    [Fact]
    public async Task MarkerInput_PersistsOneSkippedLogRowPerFile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        WorkflowHandler handler = scope.ServiceProvider.GetRequiredService<WorkflowHandler>();

        Workflow workflow = BuildSingleBlockWorkflow("marker");
        string jobId = Guid.NewGuid().ToString();

        await handler.CreateRootOutputAsync(uow, jobId, workflow, ORG_ID, userId: null);

        List<SkippedProtectedFile> skippedFiles =
        [
            new(PROTECTED_PDF_NAME, PROTECTED_PDF_REASON),
            new(DRM_XLSX_NAME, DRM_XLSX_REASON),
        ];

        Workflow executed = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: jobId,
            uow,
            inputs: new()
            {
                [IntakeInputKeys.SKIPPED_PROTECTED_FILES] = SkippedProtectedFile.Serialize(skippedFiles),
            },
            cancellationToken: ct);

        Assert.NotNull(handler.WorkflowExecutionId);

        await using UserDataDbContext db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(ct);

        List<WorkflowExecutionLog> intakeLogs = await db.WorkflowExecutionLogs
            .AsNoTracking()
            .Where(log => log.WorkflowExecutionId == handler.WorkflowExecutionId
                && log.SectionName == WorkflowHandler.DOCUMENT_INTAKE_SECTION_NAME)
            .ToListAsync(ct);

        Assert.Equal(2, intakeLogs.Count);

        WorkflowExecutionLog pdfLog = Assert.Single(intakeLogs, log => log.BlockName == PROTECTED_PDF_NAME);
        Assert.Equal(BlockExecutionStatus.Skipped, pdfLog.Status);
        Assert.Equal(PROTECTED_PDF_REASON, pdfLog.Error);
        Assert.True(pdfLog.ShouldDisplayInLogs);

        WorkflowExecutionLog xlsxLog = Assert.Single(intakeLogs, log => log.BlockName == DRM_XLSX_NAME);
        Assert.Equal(BlockExecutionStatus.Skipped, xlsxLog.Status);
        Assert.Equal(DRM_XLSX_REASON, xlsxLog.Error);

        // Intake rows are flushed before the executed workflow clone exists in UserData.Workflows,
        // so they must not carry WorkflowId — on relational providers a non-null value violates
        // FK_WorkflowExecutionLogs_Workflows_WorkflowId (invisible to this InMemory fixture).
        Assert.All(intakeLogs, log => Assert.Null(log.WorkflowId));

        Block executedBlock = Assert.Single(executed.Sections.SelectMany(s => s.Blocks));
        Block? persistedBlock = await db.Blocks
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == executedBlock.Id, ct);
        Assert.NotNull(persistedBlock);
        Assert.Equal(ExecutionStatus.Completed, persistedBlock!.ExecutionState?.Status);
    }

    [Fact]
    public async Task NoMarkerInput_WritesNoIntakeLogRows()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using IServiceScope scope = _sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        IUserDataUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        WorkflowHandler handler = scope.ServiceProvider.GetRequiredService<WorkflowHandler>();

        Workflow workflow = BuildSingleBlockWorkflow("no-marker");
        string jobId = Guid.NewGuid().ToString();

        await handler.CreateRootOutputAsync(uow, jobId, workflow, ORG_ID, userId: null);

        _ = await handler.ExecuteWorkflowAsync(
            workflow,
            anonymisationMap: new(),
            organizationId: ORG_ID,
            jobId: jobId,
            uow,
            inputs: [],
            cancellationToken: ct);

        Assert.NotNull(handler.WorkflowExecutionId);

        await using UserDataDbContext db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(ct);

        bool anyIntakeLogs = await db.WorkflowExecutionLogs
            .AsNoTracking()
            .AnyAsync(log => log.WorkflowExecutionId == handler.WorkflowExecutionId
                && log.SectionName == WorkflowHandler.DOCUMENT_INTAKE_SECTION_NAME, ct);

        Assert.False(anyIntakeLogs);
    }

    private static Workflow BuildSingleBlockWorkflow(string idPrefix)
    {
        Workflow root = new() { Id = $"{idPrefix}-root", Title = $"{idPrefix}-root" };

        Section section = new($"{idPrefix}-section", root)
        {
            Id = $"{idPrefix}-section",
            Order = 0,
        };

        _ = new Block(BlockTypes.Merge, section, replacementTag: $"{idPrefix}_block")
        {
            Id = $"{idPrefix}-blk",
            Name = $"{idPrefix} block",
            Properties = new MergeProperties { TextTemplate = "block body" },
        };

        return root;
    }
}
