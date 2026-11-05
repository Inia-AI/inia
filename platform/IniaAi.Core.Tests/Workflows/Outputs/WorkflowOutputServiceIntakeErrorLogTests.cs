using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Outputs;

/// <summary>
/// Guards the durable half of the pipeline password defense-in-depth: when a job dies on a
/// password-protected document, the actionable reason must land as a Failed intake log row on
/// the execution — not only in the in-memory job record — and must not duplicate when the
/// job error path handles the same exception twice.
/// </summary>
public class WorkflowOutputServiceIntakeErrorLogTests
    : IClassFixture<WorkflowsFixture<WorkflowOutputServiceIntakeErrorLogTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";
    private const string DOCUMENT_NAME = "q3-statement.pdf";
    private const string ERROR_MESSAGE =
        "Document 'q3-statement.pdf' is password-protected. Re-upload it and provide its password.";

    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IWorkflowOutputService _workflowOutputService;

    public WorkflowOutputServiceIntakeErrorLogTests(
        WorkflowsFixture<WorkflowOutputServiceIntakeErrorLogTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _workflowOutputService = _testServiceProvider.GetRequiredService<IWorkflowOutputService>();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
    }

    [Fact]
    public async Task AppendDocumentIntakeErrorLogAsync_WritesFailedRowOnExecutionAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync();

        await _workflowOutputService.AppendDocumentIntakeErrorLogAsync(execution.Id, DOCUMENT_NAME, ERROR_MESSAGE);

        List<WorkflowExecutionLog> intakeLogs = await ReadIntakeLogsAsync(execution.Id);
        WorkflowExecutionLog row = Assert.Single(intakeLogs);
        Assert.Equal(WorkflowHandler.DOCUMENT_INTAKE_SECTION_NAME, row.SectionName);
        Assert.Equal(DOCUMENT_NAME, row.BlockName);
        Assert.Equal(BlockExecutionStatus.Failed, row.Status);
        Assert.Equal(ERROR_MESSAGE, row.Error);
        Assert.True(row.ShouldDisplayInLogs);
    }

    [Fact]
    public async Task AppendDocumentIntakeErrorLogAsync_CalledTwice_WritesSingleRowAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync();

        await _workflowOutputService.AppendDocumentIntakeErrorLogAsync(execution.Id, DOCUMENT_NAME, ERROR_MESSAGE);
        await _workflowOutputService.AppendDocumentIntakeErrorLogAsync(execution.Id, DOCUMENT_NAME, ERROR_MESSAGE);

        List<WorkflowExecutionLog> intakeLogs = await ReadIntakeLogsAsync(execution.Id);
        _ = Assert.Single(intakeLogs);
    }

    [Fact]
    public async Task AppendDocumentIntakeErrorLogAsync_MissingOrUnknownExecutionId_DoesNotThrowAsync()
    {
        Exception? missingId = await Record.ExceptionAsync(
            () => _workflowOutputService.AppendDocumentIntakeErrorLogAsync(null, DOCUMENT_NAME, ERROR_MESSAGE));
        Exception? unknownId = await Record.ExceptionAsync(
            () => _workflowOutputService.AppendDocumentIntakeErrorLogAsync("does-not-exist", DOCUMENT_NAME, ERROR_MESSAGE));

        Assert.Null(missingId);
        Assert.Null(unknownId);
    }

    [Fact]
    public async Task AppendPageRefusalLogAsync_NamesThePageAndItsCategory_OnceAsync()
    {
        WorkflowExecution execution = await CreateExecutionAsync();

        await _workflowOutputService.AppendPageRefusalLogAsync(execution.Id, "ID.jpg", 1, "written", wholePage: true);
        await _workflowOutputService.AppendPageRefusalLogAsync(execution.Id, "ID.jpg", 1, "written", wholePage: true);

        WorkflowExecutionLog row = Assert.Single(await ReadIntakeLogsAsync(execution.Id));
        Assert.Equal("ID.jpg, page 1", row.BlockName);
        Assert.Equal(BlockExecutionStatus.Failed, row.Status);
        Assert.Equal("written", row.RefusalCategory);
        Assert.Contains("The model refused to read this page", row.Error);
    }

    [Fact]
    public void DescribePageRefusal_ARefusedImageOnAPageWithText_SaysTheTextWasKept()
    {
        string described = WorkflowOutputService.DescribePageRefusal("cyber", wholePage: false);

        Assert.Equal("The model refused to read an image on this page (refusal category: cyber). The image's content is not in the page's text.", described);
    }

    private async Task<WorkflowExecution> CreateExecutionAsync()
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            Title = "Intake error log test",
            WorkflowState = WorkflowState.Running,
        };

        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return execution;
    }

    private async Task<List<WorkflowExecutionLog>> ReadIntakeLogsAsync(string executionId)
    {
        await using UserDataDbContext db = await _testServiceProvider
            .GetRequiredService<IDbContextFactory<UserDataDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        return await db.WorkflowExecutionLogs
            .AsNoTracking()
            .Where(log => log.WorkflowExecutionId == executionId
                && log.SectionName == WorkflowHandler.DOCUMENT_INTAKE_SECTION_NAME)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
