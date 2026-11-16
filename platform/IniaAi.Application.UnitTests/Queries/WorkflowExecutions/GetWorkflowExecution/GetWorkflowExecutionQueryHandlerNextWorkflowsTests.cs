using ErrorOr;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.Services;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Common.Enums;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Specifications.Documents;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

/// <summary>
/// The read of a finished run names the workflows it leads to and the documents each takes: the run's output and the
/// inputs both workflows share. A run that is not finished names none, and a deleted output leads nowhere.
/// </summary>
public sealed class GetWorkflowExecutionQueryHandlerNextWorkflowsTests : IDisposable
{
    private const string ORG_ID = "org-test";
    private const string USER_ID = "user-test";
    private const string JOB_ID = "job-next-workflows";
    private const string PART_1 = "CorpPart1TypeA";
    private const string PART_2 = "CorpPart2TypeA";
    private const string SPREAD_OUTPUT = "Financial Spread Output";
    private const string SPREAD_INPUT = "CorporateFinancialModel";
    private const string ANNUAL_REPORT = "Annual Report";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetWorkflowExecutionQueryHandler _handler;

    public GetWorkflowExecutionQueryHandlerNextWorkflowsTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();

        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();

        _handler = new GetWorkflowExecutionQueryHandler(
            NullLogger<GetWorkflowExecutionQueryHandler>.Instance,
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _serviceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new NoBaselineReader(),
            new NextWorkflows(_serviceProvider.GetRequiredService<ICoreUnitOfWork>(), new EveryWorkflowAvailable(), new NoCreditCost()),
            new SilentAgentRunActivityFeed());

        SeedTypes();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task AFinishedRun_NamesTheNextWorkflow_WithItsWorkbookAndTheSharedInput()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Draft);
        Document spread = await SeedDocumentAsync("Spread.xlsm", MediaType.ApplicationVndMsExcelSheetMacroEnabled12, SPREAD_OUTPUT);
        _ = await SeedDocumentAsync("Spread.docx", MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument, SPREAD_OUTPUT);
        Document annualReport = await SeedDocumentAsync("Annual report.pdf", MediaType.ApplicationPdf, ANNUAL_REPORT);
        _ = await SeedDocumentAsync("Photo.pdf", MediaType.ApplicationPdf, ANNUAL_REPORT, excluded: true);

        WorkflowExecutionDto result = await HandleAsync(execution.Id);

        NextWorkflowDto next = Assert.Single(result.NextWorkflows!);
        Assert.Equal(PART_2, next.WorkflowTypeId);
        Assert.Equal("INIA Corporate Analysis - Part 2", next.WorkflowTypeName);
        Assert.Equal(
            [(spread.Id, "Spread", ".xlsm", SPREAD_INPUT), (annualReport.Id, "Annual report", ".pdf", ANNUAL_REPORT)],
            next.Documents.Select(document => (document.FileId, document.Name, document.Extension, document.DocumentClass)));
    }

    [Fact]
    public async Task ARunThatIsNotFinished_NamesNoNextWorkflows()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Running);
        _ = await SeedDocumentAsync("Spread.xlsm", MediaType.ApplicationVndMsExcelSheetMacroEnabled12, SPREAD_OUTPUT);

        Assert.Null((await HandleAsync(execution.Id)).NextWorkflows);
    }

    [Fact]
    public async Task AFinishedRunWhoseOutputWasDeleted_LeadsNowhere()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Draft);
        Document spread = await SeedDocumentAsync("Spread.xlsm", MediaType.ApplicationVndMsExcelSheetMacroEnabled12, SPREAD_OUTPUT);
        _ = await SeedDocumentAsync("Annual report.pdf", MediaType.ApplicationPdf, ANNUAL_REPORT);
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();
        unitOfWork.Documents.Remove((await unitOfWork.Documents.GetFirstOrDefaultAsync(new DocumentByIdTrackingSpec(spread.Id), TestContext.Current.CancellationToken))!);
        _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        Assert.Empty((await HandleAsync(execution.Id)).NextWorkflows!);
    }

    [Fact]
    public async Task AUserWhoChoseOtherReportTypes_IsNotLedToATypeTheirListHides()
    {
        WorkflowExecution execution = await SeedExecutionAsync(WorkflowState.Draft);
        _ = await SeedDocumentAsync("Spread.xlsm", MediaType.ApplicationVndMsExcelSheetMacroEnabled12, SPREAD_OUTPUT);
        _ = _context.Users.Add(new User { Id = USER_ID, UserName = "analyst@example.com", Email = "analyst@example.com", OrganizationId = ORG_ID });
        _ = _context.Set<UserWorkflowTypeVisibility>().Add(new UserWorkflowTypeVisibility { UserId = USER_ID, WorkflowTypeId = PART_1 });
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        Assert.Empty((await HandleAsync(execution.Id)).NextWorkflows!);
    }

    private async Task<WorkflowExecutionDto> HandleAsync(string executionId)
    {
        ErrorOr<WorkflowExecutionDto> result = await _handler.Handle(
            new GetWorkflowExecutionQuery(ORG_ID, USER_ID, executionId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        return result.Value;
    }

    private void SeedTypes()
    {
        CoreDbContext core = _serviceProvider.GetRequiredService<CoreDbContext>();
        _ = core.WorkflowTypes.Add(new WorkflowType { Id = PART_1, Name = "INIA Corporate Analysis - Part 1" });
        _ = core.WorkflowTypes.Add(new WorkflowType { Id = PART_2, Name = "INIA Corporate Analysis - Part 2" });
        core.GrantWorkflowTypeAccess(PART_2, ORG_ID);
        var part2 = new Workflow("INIA Corporate Analysis - Part 2") { WorkflowTypeId = PART_2 };
        _ = new DocumentClass(part2, SPREAD_INPUT) { MadeByWorkflowTypeId = PART_1, MadeByDocumentClassKey = SPREAD_OUTPUT };
        _ = new DocumentClass(part2, ANNUAL_REPORT);
        _ = core.Workflows.Add(part2);
        _ = core.SaveChanges();
        _ = _context.Organizations.Add(new Organization("Test organization", ORG_ID));
        _ = _context.SaveChanges();
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(WorkflowState state)
    {
        var snapshot = new Workflow("INIA Corporate Analysis - Part 1") { WorkflowTypeId = PART_1 };
        _ = new DocumentClass(snapshot, SPREAD_OUTPUT) { IsOutputDocument = true };
        _ = new DocumentClass(snapshot, ANNUAL_REPORT);
        _ = await _context.Workflows.AddAsync(snapshot, TestContext.Current.CancellationToken);

        var execution = new WorkflowExecution
        {
            OrganizationId = ORG_ID,
            WorkflowTypeId = PART_1,
            WorkflowId = snapshot.Id,
            JobId = JOB_ID,
            WorkflowState = state,
        };
        _ = await _context.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return execution;
    }

    private async Task<Document> SeedDocumentAsync(string fileName, MediaType mediaType, string documentClass, bool excluded = false)
    {
        var fileMetadata = new FileMetadata(ORG_ID, fileName, Encoding.UTF8.GetBytes(fileName));
        var document = new Document(fileMetadata, mediaType);
        var documentInfo = new DocumentInfo(document.Id) { Document = document, JobId = JOB_ID, DocumentClass = documentClass };
        if (excluded)
        {
            documentInfo.ExcludeFromRun("NotExpectedDocument", DateTime.UtcNow);
        }

        _ = await _context.Set<DocumentInfo>().AddAsync(documentInfo, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();

        return document;
    }
}
