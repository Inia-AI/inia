using System.Globalization;
using System.Text;
using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution;
using IniaAi.Application.Commands.WorkflowExecutions.ExportWorkflowExecution.Models;
using IniaAi.Application.Common.Services;
using IniaAi.Common.Interfaces.Services.Reports;
using IniaAi.Configuration;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.FileFormats.Markdown;
using IniaAi.Services.Export;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.ExportWorkflowExecution;

public sealed class ExportWorkflowExecutionCommandHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-id";
    private const string EXECUTION_ID = "3f1c9a4e-5d2b-4f7a-9c1e-8b6d2a0f4e11";
    private const string WORKFLOW_TYPE_ID = "credit-review";
    private const string COUNTERPARTY_ID = "counterparty-1";
    private const string MARKDOWN_CONTENT_TYPE = "text/markdown; charset=utf-8";
    private const string SECTION_TITLE = "Credit Summary";

    private readonly CoreDbContext _core;
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly UserDataDbContext _userData;
    private readonly ExportWorkflowExecutionCommandHandler _handler;

    public ExportWorkflowExecutionCommandHandlerTests()
    {
        string databaseName = $"export-execution-{Guid.NewGuid()}";
        _core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"{databaseName}-core")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        var services = new ServiceCollection();
        _ = services.AddDbContext<UserDataDbContext>(o => o.UseInMemoryDatabase($"{databaseName}-userdata"));
        _ = services.AddScoped(_ => _core);
        _ = services.AddTestDatabaseServicesWithoutDbContext();
        _provider = services.BuildServiceProvider();
        _userData = _provider.GetRequiredService<UserDataDbContext>();

        IUserDataUnitOfWork userData = _provider.GetRequiredService<IUserDataUnitOfWork>();
        var exportService = new ExportService(
            NullLogger<ExportService>.Instance,
            userData,
            new UserDisplayNameResolver(userData),
            [new MarkdownReportWriter()],
            new ChartImageRenderService(Options.Create(new PlatformSettings()), NullLogger<ChartImageRenderService>.Instance),
            new NoReportNotice());
        _handler = new ExportWorkflowExecutionCommandHandler(
            NullLogger<ExportWorkflowExecutionCommandHandler>.Instance,
            userData,
            _provider.GetRequiredService<ICoreUnitOfWork>(),
            exportService);
    }

    public void Dispose()
    {
        _core.Dispose();
        _provider.Dispose();
    }

    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Handle_WhenWorkflowExecutionNotFound_ShouldReturnNotFoundError()
    {
        ErrorOr<WorkflowExecutionExportResult> result = await _handler.Handle(
            new ExportWorkflowExecutionCommand("non-existent-id", ExportFormat.Word, "user-id", ORGANIZATION_ID),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Workflow execution not found.", result.FirstError.Description);
    }

    [Fact]
    public async Task Handle_WhenWorkflowExecutionBelongsToDifferentOrganization_AnswersAsARunThatDoesNotExist()
    {
        await ArrangeExecutionAsync(new WorkflowExecution { Id = EXECUTION_ID, OrganizationId = "different-org-id" });

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(ExportFormat.Markdown);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal("Workflow execution not found.", result.FirstError.Description);
    }

    [Fact]
    public async Task Handle_WhenExportSuccessful_ShouldReturnTheFile()
    {
        await ArrangeReportAsync(new WorkflowExecution { Id = EXECUTION_ID, OrganizationId = ORGANIZATION_ID });

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(ExportFormat.Markdown);

        Assert.False(result.IsError);
        Assert.Contains($"## {SECTION_TITLE}", Encoding.UTF8.GetString(result.Value.Content.BinaryData.ToArray()));
        Assert.Equal(MARKDOWN_CONTENT_TYPE, result.Value.ContentType);
        Assert.Equal(EXECUTION_ID, result.Value.WorkflowExecutionId);
    }

    [Fact]
    public async Task Handle_UntitledReport_NamesTheFileAfterTheWorkflowTypeAndTheCounterparty()
    {
        await ArrangeReportAsync(new WorkflowExecution
        {
            Id = EXECUTION_ID,
            OrganizationId = ORGANIZATION_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            CounterpartyId = COUNTERPARTY_ID,
        });
        await ArrangeWorkflowTypeAsync("Credit Review");
        await ArrangeCounterpartyAsync("Acme Holdings");

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(ExportFormat.Markdown);

        Assert.Equal($"Credit Review - Acme Holdings - {Today}.md", result.Value.FileName);
        Assert.DoesNotContain(EXECUTION_ID, result.Value.FileName);
    }

    [Fact]
    public async Task Handle_TitledReportWithoutCounterparty_NamesTheFileAfterTheTitle()
    {
        await ArrangeReportAsync(new WorkflowExecution
        {
            Id = EXECUTION_ID,
            OrganizationId = ORGANIZATION_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            Title = "Hedge Fund Monitoring",
        });
        await ArrangeWorkflowTypeAsync("Hedge Funds Monitoring");

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(ExportFormat.Markdown);

        Assert.Equal($"Hedge Fund Monitoring - {Today}.md", result.Value.FileName);
    }

    [Fact]
    public async Task Handle_NameWithCharactersAFileSystemRefuses_ReplacesThem()
    {
        await ArrangeReportAsync(new WorkflowExecution
        {
            Id = EXECUTION_ID,
            OrganizationId = ORGANIZATION_ID,
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            CounterpartyId = COUNTERPARTY_ID,
        });
        await ArrangeWorkflowTypeAsync("Q3/Q4: Review?");
        await ArrangeCounterpartyAsync("A<B>  \"C\" | D.");

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(ExportFormat.Markdown);

        Assert.Equal($"Q3 Q4 Review - A B C D. - {Today}.md", result.Value.FileName);
    }

    [Fact]
    public async Task Handle_WhenTheRunHasNoReport_ShouldReturnNotFoundError()
    {
        await ArrangeExecutionAsync(new WorkflowExecution { Id = EXECUTION_ID, OrganizationId = ORGANIZATION_ID });

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(ExportFormat.Markdown);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Equal($"WorkflowReport for execution {EXECUTION_ID} not found", result.FirstError.Description);
    }

    [Theory]
    [InlineData(ExportFormat.Pdf, "There is no Pdf export.")]
    [InlineData(ExportFormat.Word, "There is no Word export.")]
    public async Task Handle_WhenNoWriterExportsTheFormat_ShouldReturnFailureNamingIt(ExportFormat format, string message)
    {
        await ArrangeReportAsync(new WorkflowExecution { Id = EXECUTION_ID, OrganizationId = ORGANIZATION_ID });

        ErrorOr<WorkflowExecutionExportResult> result = await ExportAsync(format);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
        Assert.Equal(message, result.FirstError.Description);
    }

    private Task<ErrorOr<WorkflowExecutionExportResult>> ExportAsync(ExportFormat format)
    {
        return _handler.Handle(
            new ExportWorkflowExecutionCommand(EXECUTION_ID, format, "user-id", ORGANIZATION_ID),
            TestContext.Current.CancellationToken);
    }

    private async Task ArrangeExecutionAsync(WorkflowExecution execution)
    {
        _ = _userData.WorkflowExecutions.Add(execution);
        _ = await _userData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ArrangeReportAsync(WorkflowExecution execution)
    {
        await ArrangeExecutionAsync(execution);
        var report = new WorkflowReport { WorkflowExecutionId = execution.Id, Title = execution.Title };
        var section = new ReportSection { Title = SECTION_TITLE };
        report.Add(section);
        section.Add(new ReportParagraph { Kind = ReportParagraphKind.Paragraph, Content = "Leverage stays inside the covenant." });
        _ = _userData.WorkflowReports.Add(report);
        _ = await _userData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ArrangeWorkflowTypeAsync(string name)
    {
        _ = _core.WorkflowTypes.Add(new WorkflowType(WORKFLOW_TYPE_ID, name));
        _ = await _core.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ArrangeCounterpartyAsync(string name)
    {
        _ = _userData.Counterparties.Add(new Counterparty(ORGANIZATION_ID, name) { Id = COUNTERPARTY_ID });
        _ = await _userData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
