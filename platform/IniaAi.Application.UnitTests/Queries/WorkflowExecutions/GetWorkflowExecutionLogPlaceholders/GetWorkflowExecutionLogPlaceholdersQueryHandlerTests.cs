using ErrorOr;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecutionLogPlaceholders;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecutionLogPlaceholders;

public class GetWorkflowExecutionLogPlaceholdersQueryHandlerTests : IDisposable
{
    private const string ORG_ID = "org-logs";
    private const string OTHER_ORG_ID = "org-logs-other";
    private const string LOG_NOT_FOUND_CODE = "WorkflowExecutionLog.NotFound";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly GetWorkflowExecutionLogPlaceholdersQueryHandler _handler;

    public GetWorkflowExecutionLogPlaceholdersQueryHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();
        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        _handler = new GetWorkflowExecutionLogPlaceholdersQueryHandler(
            NullLogger<GetWorkflowExecutionLogPlaceholdersQueryHandler>.Instance,
            _serviceProvider.GetRequiredService<IUserDataUnitOfWork>());
    }

    private async Task<WorkflowExecutionLog> SeedLogAsync()
    {
        var execution = new WorkflowExecution { OrganizationId = ORG_ID, Title = "Run" };
        var log = new WorkflowExecutionLog { WorkflowExecutionId = execution.Id, WorkflowExecution = execution, BlockName = "Block" };
        _ = await _context.WorkflowExecutions.AddAsync(execution, TestContext.Current.CancellationToken);
        _ = await _context.WorkflowExecutionLogs.AddAsync(log, TestContext.Current.CancellationToken);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.ChangeTracker.Clear();
        return log;
    }

    [Fact]
    public async Task Handle_ALogOfAnotherOrganization_IsNotFound()
    {
        WorkflowExecutionLog log = await SeedLogAsync();

        ErrorOr<Dictionary<string, string>> result = await _handler.Handle(
            new GetWorkflowExecutionLogPlaceholdersQuery(OTHER_ORG_ID, "user", log.WorkflowExecutionId, log.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(LOG_NOT_FOUND_CODE, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_ALogOfTheCallersOrganization_IsFound()
    {
        WorkflowExecutionLog log = await SeedLogAsync();

        ErrorOr<Dictionary<string, string>> result = await _handler.Handle(
            new GetWorkflowExecutionLogPlaceholdersQuery(ORG_ID, "user", log.WorkflowExecutionId, log.Id),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(LOG_NOT_FOUND_CODE, result.IsError ? result.FirstError.Code : null);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
