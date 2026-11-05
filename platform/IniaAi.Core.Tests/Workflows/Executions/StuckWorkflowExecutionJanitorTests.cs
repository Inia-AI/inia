using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.Models;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Core.Workflows.Outputs.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Executions;

public class StuckWorkflowExecutionJanitorTests
    : IClassFixture<WorkflowsFixture<StuckWorkflowExecutionJanitorTests>>, IDisposable
{
    private const string ORG_ID = "TestOrg";
    private const int MAX_LIFETIME_HOURS = 12;

    private readonly IServiceProvider _testServiceProvider;
    private readonly IServiceScope _serviceScope;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IAgentRunTerminalCleanup _cleanup;
    private readonly StuckWorkflowExecutionJanitor _janitor;

    public StuckWorkflowExecutionJanitorTests(
        WorkflowsFixture<StuckWorkflowExecutionJanitorTests> fixture,
        ITestOutputHelper testOutputHelper)
    {
        _testServiceProvider = fixture.BuildTestServiceProvider(testOutputHelper);
        _serviceScope = _testServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        _userDataUnitOfWork = _serviceScope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        _cleanup = Substitute.For<IAgentRunTerminalCleanup>();
        var outputService = new WorkflowOutputService(
            _testServiceProvider.GetRequiredService<IWorkflowOutputBuilder>(),
            _testServiceProvider.GetRequiredService<IWorkflowOutputValidator>(),
            _testServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            _testServiceProvider.GetRequiredService<ILogger<WorkflowOutputService>>(),
            _cleanup);
        _janitor = new StuckWorkflowExecutionJanitor(
            _userDataUnitOfWork,
            outputService,
            Options.Create(new WorkflowExecutionJanitorSettings
            {
                MaxLifetimeHours = MAX_LIFETIME_HOURS,
            }),
            _testServiceProvider.GetRequiredService<ILogger<StuckWorkflowExecutionJanitor>>());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _serviceScope.Dispose();
    }

    [Fact]
    public async Task SweepOnceAsync_MarksOnlyAgedActiveRows_AndCleansEachMarkedRow()
    {
        DateTime aged = DateTime.UtcNow.AddHours(-(MAX_LIFETIME_HOURS + 1));
        WorkflowExecution agedRunning = await CreateExecutionAsync(
            WorkflowState.Running, startedAt: aged);
        WorkflowExecution agedUndefined = await CreateExecutionAsync(
            WorkflowState.Undefined, createdAt: aged);
        WorkflowExecution freshRunning = await CreateExecutionAsync(
            WorkflowState.Running, startedAt: DateTime.UtcNow.AddHours(-1));
        WorkflowExecution agedConfirmed = await CreateExecutionAsync(
            WorkflowState.Confirmed, startedAt: aged);
        WorkflowExecution agedCancelled = await CreateExecutionAsync(
            WorkflowState.Cancelled, startedAt: aged);

        int marked = await _janitor.SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, marked);

        WorkflowExecution? failedRunning = await ReadFreshAsync(agedRunning.Id);
        Assert.NotNull(failedRunning);
        Assert.Equal(WorkflowState.Failed, failedRunning.WorkflowState);
        Assert.Equal(WorkflowExecutionFailureKinds.LIFETIME_EXCEEDED, failedRunning.FailureKind);
        Assert.False(string.IsNullOrWhiteSpace(failedRunning.FailureDetail));

        WorkflowExecution? failedUndefined = await ReadFreshAsync(agedUndefined.Id);
        Assert.NotNull(failedUndefined);
        Assert.Equal(WorkflowState.Failed, failedUndefined.WorkflowState);
        Assert.Equal(WorkflowExecutionFailureKinds.LIFETIME_EXCEEDED, failedUndefined.FailureKind);

        Assert.Equal(WorkflowState.Running, (await ReadFreshAsync(freshRunning.Id))!.WorkflowState);
        Assert.Equal(WorkflowState.Confirmed, (await ReadFreshAsync(agedConfirmed.Id))!.WorkflowState);
        Assert.Equal(WorkflowState.Cancelled, (await ReadFreshAsync(agedCancelled.Id))!.WorkflowState);

        await _cleanup.Received(1).CleanUpAsync(agedRunning.Id, ORG_ID, WorkflowState.Failed);
        await _cleanup.Received(1).CleanUpAsync(agedUndefined.Id, ORG_ID, WorkflowState.Failed);
        Assert.Equal(2, _cleanup.ReceivedCalls().Count());
    }

    [Fact]
    public async Task SweepOnceAsync_AgedRowWithoutOrganization_IsLeftAloneNotCrashedOn()
    {
        DateTime aged = DateTime.UtcNow.AddHours(-(MAX_LIFETIME_HOURS + 1));
        WorkflowExecution orphan = await CreateExecutionAsync(
            WorkflowState.Running, startedAt: aged, organizationId: null);

        int marked = await _janitor.SweepOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, marked);
        Assert.Equal(WorkflowState.Running, (await ReadFreshAsync(orphan.Id))!.WorkflowState);
        await _cleanup.DidNotReceiveWithAnyArgs().CleanUpAsync(default!, default!, default);
    }

    private async Task<WorkflowExecution> CreateExecutionAsync(
        WorkflowState state,
        DateTime? startedAt = null,
        DateTime? createdAt = null,
        string? organizationId = ORG_ID)
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = organizationId,
            Title = "Janitor test",
            WorkflowState = state,
            StartedAt = startedAt,
        };
        if (createdAt is not null)
        {
            execution.CreatedAt = createdAt.Value;
        }

        await _userDataUnitOfWork.WorkflowExecutions.AddAsync(execution);
        _ = await _userDataUnitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        return execution;
    }

    private async Task<WorkflowExecution?> ReadFreshAsync(string executionId)
    {
        using IServiceScope scope = _testServiceProvider
            .GetRequiredService<IServiceScopeFactory>()
            .CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider
            .GetRequiredService<IUserDataUnitOfWork>();

        return await unitOfWork.WorkflowExecutions
            .SingleOrDefaultAsync(predicate: x => x.Id == executionId);
    }
}
