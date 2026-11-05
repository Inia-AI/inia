using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Core.Tests.Workflows.Outputs;

/// <summary>
/// Covers the report-tree write path of <see cref="WorkflowOutputService"/>:
/// executing a workflow materialises a <see cref="WorkflowReport"/> root, and section/
/// block processing appends <see cref="ReportSection"/> and <see cref="ReportParagraph"/>
/// nodes under it.
/// </summary>
public class WorkflowOutputServiceReportTreeTests : IDisposable
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;
    private readonly IUserDataUnitOfWork _uow;
    private readonly WorkflowOutputService _sut;

    public WorkflowOutputServiceReportTreeTests()
    {
        var services = new ServiceCollection();
        string databaseName = $"WorkflowOutputServiceReportTreeTests-{Guid.NewGuid()}";

        _ = services.AddDbContext<UserDataDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        _ = services.AddDbContextFactory<UserDataDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        _ = services.AddTestDatabaseServicesWithoutDbContext();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        _uow = _scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        var builder = new WorkflowOutputBuilder();
        var validator = new WorkflowOutputValidator();

        _sut = new WorkflowOutputService(
            builder,
            validator,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkflowOutputService>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_PersistsWorkflowReport_WithSharedId()
    {
        var workflow = new Workflow
        {
            Title = "Report Root",
            WorkflowTypeId = "wf-type-1",
        };

        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, null, _uow);

        WorkflowReport? report = await _uow.Repository<WorkflowReport>()
            .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal(execution.Id, report.Id);
        Assert.Equal(execution.Id, report.WorkflowExecutionId);
        Assert.Equal("Report Root", report.Title);
        Assert.Equal("wf-type-1", report.WorkflowTypeId);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_PopulatesWorkflowReportFromContext()
    {
        var workflow = new Workflow { Title = "Ctx Root", WorkflowTypeId = "wt-2" };
        var context = new WorkflowOutputContext
        {
            CurrentUserId = "user-9",
            CurrentOrganizationId = "org-9",
            CurrentCounterparty = new Counterparty { Id = "cp-9" },
        };

        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            workflow,
            context,
            _uow,
            reviewDate: new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            workflowState: WorkflowState.Running,
            jobId: "job-9");

        WorkflowReport? report = await _uow.Repository<WorkflowReport>()
            .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal("user-9", report.UserId);
        Assert.Equal("org-9", report.OrganizationId);
        Assert.Equal("cp-9", report.CounterpartyId);
        Assert.Equal("job-9", report.JobId);
        Assert.Equal(WorkflowState.Running, report.WorkflowState);
        Assert.Equal(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc), report.ReviewDate);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_SetsCurrentWorkflowReportOnContext()
    {
        var workflow = new Workflow();
        var context = new WorkflowOutputContext();

        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, context, _uow);

        Assert.NotNull(context.CurrentWorkflowReport);
        Assert.Equal(execution.Id, context.CurrentWorkflowReport.Id);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_LinksWorkflowReportToCounterparty()
    {
        var workflow = new Workflow();
        var context = new WorkflowOutputContext
        {
            CurrentCounterparty = new Counterparty { Id = "cp-link" },
        };

        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, context, _uow);

        WorkflowReport? report = await _uow.Repository<WorkflowReport>()
            .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal("cp-link", report.CounterpartyId);
    }

    [Fact]
    public async Task CreateExecutionFromWorkflowAsync_DefaultsWorkflowReportStateToUndefined()
    {
        var workflow = new Workflow();

        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            workflow, null, _uow);

        WorkflowReport? report = await _uow.Repository<WorkflowReport>()
            .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal(WorkflowState.Undefined, report.WorkflowState);
    }

    [Fact]
    public async Task CreateReportSectionAsync_PersistsReportSection()
    {
        var root = new Workflow();
        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            root, new WorkflowOutputContext(), _uow);

        var section = new Section("Section A", root)
        {
            Title = "Section A",
        };

        var context = new WorkflowOutputContext
        {
            CurrentWorkflowOutputRoot = execution,
            RootElementId = execution.Id,
            CurrentWorkflowReport = await _uow.Repository<WorkflowReport>()
                .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken),
        };

        ReportSection? returned = await _sut.CreateReportSectionAsync(
            section, context, _uow);

        Assert.NotNull(returned);

        ReportSection? reportSection = await _uow.Repository<ReportSection>()
            .GetByIdAsync(returned.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reportSection);
        Assert.Equal(execution.Id, reportSection.WorkflowReportId);
        Assert.Equal("Section A", reportSection.Title);
    }

    [Fact]
    public async Task CreateReportSectionAsync_SetsCurrentReportSectionOnContext()
    {
        var root = new Workflow();
        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            root, new WorkflowOutputContext(), _uow);

        var section = new Section("Section B", root);

        var context = new WorkflowOutputContext
        {
            CurrentWorkflowOutputRoot = execution,
            RootElementId = execution.Id,
            CurrentWorkflowReport = await _uow.Repository<WorkflowReport>()
                .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken),
        };

        ReportSection? returned = await _sut.CreateReportSectionAsync(
            section, context, _uow);

        Assert.NotNull(returned);
        Assert.NotNull(context.CurrentReportSection);
        Assert.Equal(returned.Id, context.CurrentReportSection.Id);
    }

    [Fact]
    public async Task CreateReportSectionAsync_CopiesGridLayoutToReportSection()
    {
        var root = new Workflow();
        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            root, new WorkflowOutputContext(), _uow);

        var section = new Section("Grid Section", root)
        {
            Configuration = new SectionConfiguration
            {
                GridLayout = new GridLayoutSettings
                {
                    Mode = "grid",
                    Columns = 3,
                    Elements = [],
                },
            },
        };

        var context = new WorkflowOutputContext
        {
            CurrentWorkflowOutputRoot = execution,
            RootElementId = execution.Id,
            CurrentWorkflowReport = await _uow.Repository<WorkflowReport>()
                .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken),
        };

        ReportSection? returned = await _sut.CreateReportSectionAsync(
            section, context, _uow);

        Assert.NotNull(returned);

        ReportSection? reportSection = await _uow.Repository<ReportSection>()
            .GetByIdAsync(returned.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reportSection);
        Assert.NotNull(reportSection.Layout);
        Assert.Equal("grid", reportSection.Layout.Mode);
        Assert.Equal(3, reportSection.Layout.Columns);
    }

    [Fact]
    public async Task CreateReportSectionAsync_SectionLeftOutOfWordExport_ReportSectionKeepsTheSetting()
    {
        ReportSection created = await SetupSectionContextAsync(
            new SectionProperties { ExcludeFromWordExport = true });
        _scope.ServiceProvider.GetRequiredService<UserDataDbContext>().ChangeTracker.Clear();

        ReportSection? reportSection = await _uow.Repository<ReportSection>()
            .GetByIdAsync(created.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reportSection);
        Assert.True(reportSection.ExcludeFromWordExport);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_ValidBlock_ReturnsParagraph()
    {
        ReportSection section = await SetupSectionContextAsync();

        var (block, context) = BuildValidParagraphBlock(section, "Paragraph A", "raw content");

        (ReportParagraph? paragraph, _, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, context, null);

        Assert.True(isValidForOutput);
        Assert.NotNull(paragraph);
        Assert.Equal(section.Id, paragraph.SectionId);
        Assert.Equal(block.Id, paragraph.BlockId);
        Assert.Equal(ReportParagraphKind.Paragraph, paragraph.Kind);
        Assert.Equal("Paragraph A", paragraph.Title);
        Assert.Equal("raw content", paragraph.Content);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_DynamicChartBlock_ProducesChartKindParagraphWithPayload()
    {
        ReportSection section = await SetupSectionContextAsync();

        var (block, context) = BuildValidParagraphBlock(
            section,
            "Chart Block",
            "{\"title\":\"Revenue\",\"chartType\":\"bar\",\"data\":[]}");
        block.Type = BlockTypes.DynamicChart;

        (ReportParagraph? paragraph, _, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, context, null);

        Assert.True(isValidForOutput);
        Assert.NotNull(paragraph);
        Assert.Equal(ReportParagraphKind.DynamicChart, paragraph.Kind);
        Assert.NotNull(paragraph.ChartPayload);
        Assert.Equal("Revenue", paragraph.ChartPayload.Name);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_InvalidBlock_ParagraphIsNull()
    {
        var workflow = new Workflow();
        var section = new Section("Default", workflow);
        var block = new Block(BlockTypes.AiQuery, section);

        (ReportParagraph? paragraph, _, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, null, null);

        Assert.False(isValidForOutput);
        Assert.Null(paragraph);
    }

    [Fact]
    public async Task CreateReportParagraphAsync_WithLogAttachedToParagraph_PersistsReportParagraphIdShadowFk()
    {
        ReportSection section = await SetupSectionContextAsync();

        var paragraph = new ReportParagraph
        {
            SectionId = section.Id,
            Kind = ReportParagraphKind.Paragraph,
        };
        var log = new WorkflowExecutionLog
        {
            BlockName = "b",
            SectionName = "s",
            WorkflowExecutionId = section.WorkflowReportId,
        };
        paragraph.WorkflowExecutionLogs = [log];

        _ = await _sut.CreateReportParagraphAsync(paragraph, _uow);

        UserDataDbContext db = _scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        WorkflowExecutionLog reloaded = await db.WorkflowExecutionLogs
            .SingleAsync(l => l.Id == log.Id, TestContext.Current.CancellationToken);
        string? reportParagraphId = db.Entry(reloaded)
            .Property<string?>("ReportParagraphId").CurrentValue;

        Assert.Equal(paragraph.Id, reportParagraphId);
    }

    [Fact]
    public async Task CreateReportParagraphAsync_PersistsParagraph()
    {
        ReportSection section = await SetupSectionContextAsync();

        var paragraph = new ReportParagraph
        {
            SectionId = section.Id,
            Kind = ReportParagraphKind.Paragraph,
            Title = "leg",
        };

        _ = await _sut.CreateReportParagraphAsync(paragraph, _uow);

        ReportParagraph? persisted = await _uow.Repository<ReportParagraph>()
            .GetByIdAsync(paragraph.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(persisted);
        Assert.Equal(section.Id, persisted.SectionId);
    }

    [Fact]
    public async Task CreateErrorOutputAsync_WritesReportSectionAndParagraph()
    {
        var root = new Workflow();
        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            root, new WorkflowOutputContext(), _uow);

        await _sut.CreateErrorOutputAsync(execution.Id, "boom", _uow);

        IEnumerable<ReportSection> sections = await _uow.Repository<ReportSection>()
            .FindAsync(s => s.WorkflowReportId == execution.Id);
        ReportSection reportSection = Assert.Single(sections);

        IEnumerable<ReportParagraph> paragraphs = await _uow.Repository<ReportParagraph>()
            .FindAsync(p => p.SectionId == reportSection.Id);
        ReportParagraph reportParagraph = Assert.Single(paragraphs);

        Assert.Equal(execution.Id, reportSection.WorkflowReportId);
        Assert.Equal(reportSection.Id, reportParagraph.SectionId);
    }

    [Fact]
    public async Task CreateErrorOutputAsync_ErrorReportParagraphContainsErrorMessage()
    {
        var root = new Workflow();
        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            root, new WorkflowOutputContext(), _uow);

        await _sut.CreateErrorOutputAsync(execution.Id, "Counterparty not found", _uow);

        IEnumerable<ReportSection> sections = await _uow.Repository<ReportSection>()
            .FindAsync(s => s.WorkflowReportId == execution.Id);
        ReportSection reportSection = Assert.Single(sections);

        IEnumerable<ReportParagraph> paragraphs = await _uow.Repository<ReportParagraph>()
            .FindAsync(p => p.SectionId == reportSection.Id);
        ReportParagraph reportParagraph = Assert.Single(paragraphs);

        Assert.Equal("Counterparty not found", reportParagraph.Content);
        Assert.Equal("ERROR", reportParagraph.Title);
    }

    [Fact]
    public async Task BuildReportParagraphFromBlock_DynamicChartParseFailure_ReturnsNullParagraph()
    {
        ReportSection section = await SetupSectionContextAsync();

        var (block, context) = BuildValidParagraphBlock(section, "Bad Chart", "this is not valid json");
        block.Type = BlockTypes.DynamicChart;

        (ReportParagraph? paragraph, _, bool isValidForOutput) =
            await _sut.BuildReportParagraphFromBlock(block, context, null);

        // Valid output source, but the chart payload fails to parse → no paragraph
        // (mirrors the old None-type element that produced no report paragraph).
        Assert.True(isValidForOutput);
        Assert.Null(paragraph);
    }

    private async Task<ReportSection> SetupSectionContextAsync(SectionProperties? properties = null)
    {
        var root = new Workflow();
        WorkflowExecution execution = await _sut.CreateExecutionFromWorkflowAsync(
            root, new WorkflowOutputContext(), _uow);

        var section = new Section("Section", root) { Properties = properties ?? new() };
        var ctx = new WorkflowOutputContext
        {
            CurrentWorkflowOutputRoot = execution,
            RootElementId = execution.Id,
            CurrentWorkflowReport = await _uow.Repository<WorkflowReport>()
                .GetByIdAsync(execution.Id, TestContext.Current.CancellationToken),
        };

        _ = await _sut.CreateReportSectionAsync(section, ctx, _uow);

        return ctx.CurrentReportSection!;
    }

    private static (Block, WorkflowOutputContext) BuildValidParagraphBlock(
        ReportSection section,
        string name,
        string rawResponse)
    {
        var workflow = new Workflow();
        var workflowSection = new Section("Default", workflow);
        var block = new Block(BlockTypes.AiQuery, workflowSection)
        {
            Name = name,
            IsOutputSource = true,
        };
        _ = new BlockResponse(block) { RawResponse = rawResponse };

        var context = new WorkflowOutputContext
        {
            CurrentSectionId = section.Id,
            CurrentReportSection = section,
        };
        return (block, context);
    }
}
