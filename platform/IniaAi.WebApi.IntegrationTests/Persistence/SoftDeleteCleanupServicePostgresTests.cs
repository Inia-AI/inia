using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Runtime proof that <see cref="SoftDeleteCleanupService.CleanupAsync"/> respects the report-tree
/// foreign keys on a real PostgreSQL database — coverage the InMemory provider cannot give because
/// it enforces no FKs. The WorkflowExecutionLog -> ReportParagraph link is a nullable shadow FK
/// (SetNull); deleting a referenced paragraph without nulling that FK first throws PG 23503. This is
/// the exact failure class that previously reached production.
/// </summary>
public class SoftDeleteCleanupServicePostgresTests : IClassFixture<PostgresUserDataDatabaseFixture>
{
    private readonly PostgresUserDataDatabaseFixture _fixture;

    public SoftDeleteCleanupServicePostgresTests(PostgresUserDataDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CleanupAsync_DirectlySoftDeletedParagraph_NullsSurvivingLogShadowFk_WithoutFkViolation()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        var ct = TestContext.Current.CancellationToken;
        const string org = "cleanup-fk-setnull";
        string paragraphId;
        string logId;

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            var execution = new WorkflowExecution { OrganizationId = org, WorkflowState = WorkflowState.Confirmed };
            var report = new WorkflowReport { OrganizationId = org, WorkflowExecutionId = execution.Id };
            var section = new ReportSection { WorkflowReportId = report.Id };
            var paragraph = new ReportParagraph
            {
                SectionId = section.Id,
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddDays(-365),
            };
            var log = new WorkflowExecutionLog { WorkflowExecutionId = execution.Id };

            seed.Add(execution);
            seed.Add(report);
            seed.Add(section);
            seed.Add(paragraph);
            seed.Add(log);
            seed.Entry(log).Property("ReportParagraphId").CurrentValue = paragraph.Id;
            await seed.SaveChangesAsync(ct);

            paragraphId = paragraph.Id;
            logId = log.Id;
        }

        await using (UserDataDbContext run = _fixture.CreateContext())
        {
            CleanupResult result = await CleanupServiceTestHarness.CreateService(run).CleanupAsync(org, retentionDays: 30, ct);
            Assert.Empty(result.Errors);
        }

        await using UserDataDbContext verify = _fixture.CreateContext();

        Assert.False(await verify.ReportParagraphs.IgnoreQueryFilters().AnyAsync(p => p.Id == paragraphId, ct));

        WorkflowExecutionLog? survivingLog = await verify.WorkflowExecutionLogs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == logId, ct);
        Assert.NotNull(survivingLog);

        string? danglingForeignKey = (string?)verify.Entry(survivingLog)
            .Property("ReportParagraphId").CurrentValue;
        Assert.Null(danglingForeignKey);
    }

    [Fact]
    public async Task CleanupAsync_DeletedExecution_PurgesReportTreeAndItsLogs_WithoutFkViolation()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        var ct = TestContext.Current.CancellationToken;
        const string org = "cleanup-fk-cascade";
        string reportId;
        string sectionId;
        string paragraphId;
        string logId;

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            var execution = new WorkflowExecution
            {
                OrganizationId = org,
                WorkflowState = WorkflowState.Confirmed,
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddDays(-365),
            };
            var report = new WorkflowReport { OrganizationId = org, WorkflowExecutionId = execution.Id };
            var section = new ReportSection { WorkflowReportId = report.Id };
            var paragraph = new ReportParagraph { SectionId = section.Id };
            var log = new WorkflowExecutionLog { WorkflowExecutionId = execution.Id };

            seed.Add(execution);
            seed.Add(report);
            seed.Add(section);
            seed.Add(paragraph);
            seed.Add(log);
            seed.Entry(log).Property("ReportParagraphId").CurrentValue = paragraph.Id;
            await seed.SaveChangesAsync(ct);

            reportId = report.Id;
            sectionId = section.Id;
            paragraphId = paragraph.Id;
            logId = log.Id;
        }

        await using (UserDataDbContext run = _fixture.CreateContext())
        {
            CleanupResult result = await CleanupServiceTestHarness.CreateService(run).CleanupAsync(org, retentionDays: 30, ct);
            Assert.Empty(result.Errors);
        }

        await using UserDataDbContext verify = _fixture.CreateContext();

        Assert.False(await verify.WorkflowReports.IgnoreQueryFilters().AnyAsync(r => r.Id == reportId, ct));
        Assert.False(await verify.ReportSections.IgnoreQueryFilters().AnyAsync(s => s.Id == sectionId, ct));
        Assert.False(await verify.ReportParagraphs.IgnoreQueryFilters().AnyAsync(p => p.Id == paragraphId, ct));
        Assert.False(await verify.WorkflowExecutionLogs.IgnoreQueryFilters().AnyAsync(l => l.Id == logId, ct));
    }

    [Fact]
    public async Task CleanupAsync_DeletedWorkflowBlock_NullsSurvivingParagraphBlockId_WithoutNoActionFkViolation()
    {
        if (!_fixture.DatabaseAvailable)
        {
            Assert.Skip(CleanupServiceTestHarness.SKIP_REASON);
        }

        var ct = TestContext.Current.CancellationToken;
        const string org = "cleanup-fk-blockid";
        string blockId;
        string paragraphId;

        await using (UserDataDbContext seed = _fixture.CreateContext())
        {
            var organization = new Organization("Cleanup FK Test", org);
            var workflow = new Workflow("cleanup-fk-template")
            {
                OrganizationId = org,
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddDays(-365),
            };
            var templateSection = new Section("section", workflow);
            var block = new Block(BlockTypes.AiQuery, templateSection);

            var report = new WorkflowReport { OrganizationId = org };
            var reportSection = new ReportSection { WorkflowReportId = report.Id };
            var paragraph = new ReportParagraph { SectionId = reportSection.Id, BlockId = block.Id };

            seed.Add(organization);
            seed.Add(workflow);
            seed.Add(templateSection);
            seed.Add(block);
            seed.Add(report);
            seed.Add(reportSection);
            seed.Add(paragraph);
            await seed.SaveChangesAsync(ct);

            blockId = block.Id;
            paragraphId = paragraph.Id;
        }

        await using (UserDataDbContext run = _fixture.CreateContext())
        {
            CleanupResult result = await CleanupServiceTestHarness.CreateService(run).CleanupAsync(org, retentionDays: 30, ct);
            Assert.Empty(result.Errors);
        }

        await using UserDataDbContext verify = _fixture.CreateContext();

        Assert.False(await verify.Set<Block>().IgnoreQueryFilters().AnyAsync(b => b.Id == blockId, ct));

        ReportParagraph? survivingParagraph = await verify.ReportParagraphs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == paragraphId, ct);
        Assert.NotNull(survivingParagraph);
        Assert.Null(survivingParagraph.BlockId);
    }
}
