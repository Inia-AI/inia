using ErrorOr;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Common.Enums;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Repositories;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

/// <summary>
/// The report poll against a real database: a document a stage took out of the run leaves the
/// input list and is shown in its own list with the reason, so a consumer can tell a document
/// that was never sent from one that was sent and dropped, and a refused run carries its kind and
/// detail on the same response.
/// </summary>
public class GetWorkflowExecutionQueryHandlerExcludedDocumentsTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string USER_ID = "user-test";
    private const string JOB_ID = "job-excluded-documents";
    private const string REASON = "NotExpectedDocument";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetWorkflowExecutionQueryHandler _handler;

    public GetWorkflowExecutionQueryHandlerExcludedDocumentsTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _ = services.AddScoped<ICoreWorkflowTypeRepository, CoreWorkflowTypeRepository>();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _handler = new GetWorkflowExecutionQueryHandler(
            NullLogger<GetWorkflowExecutionQueryHandler>.Instance,
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new NoBaselineReader(),
            new SilentAgentRunActivityFeed());
    }

    [Fact]
    public async Task Handle_ListsADroppedDocumentApart_WithItsReason_AndNotAsInput()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        Document kept = await SeedDocumentInfoAsync("statement.pdf", excluded: false);
        Document dropped = await SeedDocumentInfoAsync("holiday-photo.pdf", excluded: true);

        WorkflowExecutionDto result = await HandleAsync(execution.Id);

        WorkflowExecutionDocumentDto input = Assert.Single(result.WorkflowExecutionDocuments!);
        Assert.Equal(kept.Id, input.FileId);
        Assert.Null(input.ExclusionReason);
        Assert.Null(input.ExcludedAt);

        WorkflowExecutionDocumentDto excluded = Assert.Single(result.ExcludedDocuments!);
        Assert.Equal(dropped.Id, excluded.FileId);
        Assert.Equal("holiday-photo", excluded.Name);
        Assert.Equal(REASON, excluded.ExclusionReason);
        Assert.NotNull(excluded.ExcludedAt);
    }

    [Fact]
    public async Task Handle_ARefusedRun_CarriesItsKindAndDetail_BesideTheDocumentItDropped()
    {
        const string DETAIL = "Missing document: Custody statement. The report cannot run without it, and none of the documents you sent is one.";
        WorkflowExecution execution = await SeedExecutionAsync(failureKind: "RejectedAtIntake", failureDetail: DETAIL);
        Document dropped = await SeedDocumentInfoAsync("holiday-photo.pdf", excluded: true);

        WorkflowExecutionDto result = await HandleAsync(execution.Id);

        Assert.Equal(WebApi.Contracts.Enums.WorkflowState.Failed, result.WorkflowState);
        Assert.Equal("RejectedAtIntake", result.FailureKind);
        Assert.Equal(DETAIL, result.FailureDetail);
        Assert.Equal(dropped.Id, Assert.Single(result.ExcludedDocuments!).FileId);
    }

    [Fact]
    public async Task Handle_WhenNothingWasDropped_ReturnsAnEmptyExcludedList()
    {
        WorkflowExecution execution = await SeedExecutionAsync();
        _ = await SeedDocumentInfoAsync("statement.pdf", excluded: false);

        WorkflowExecutionDto result = await HandleAsync(execution.Id);

        _ = Assert.Single(result.WorkflowExecutionDocuments!);
        Assert.Empty(result.ExcludedDocuments!);
    }

    private async Task<WorkflowExecutionDto> HandleAsync(string executionId)
    {
        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, executionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        return result.Value;
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(string? failureKind = null, string? failureDetail = null)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = "workflow-type-1",
            JobId = JOB_ID,
        };

        if (failureKind is not null)
        {
            execution.WorkflowState = WorkflowState.Failed;
            execution.FailureKind = failureKind;
            execution.FailureDetail = failureDetail;
        }

        _ = await _context.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return execution;
    }

    private async Task<Document> SeedDocumentInfoAsync(string fileName, bool excluded)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(fileName);
        var fileMetadata = new FileMetadata(ORG_ID, fileName, bytes);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);
        var documentInfo = new DocumentInfo(document.Id) { Document = document, JobId = JOB_ID };

        if (excluded)
        {
            documentInfo.ExcludeFromRun(REASON, DateTime.UtcNow);
        }

        _ = await _context.Set<DocumentInfo>().AddAsync(documentInfo, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return document;
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
