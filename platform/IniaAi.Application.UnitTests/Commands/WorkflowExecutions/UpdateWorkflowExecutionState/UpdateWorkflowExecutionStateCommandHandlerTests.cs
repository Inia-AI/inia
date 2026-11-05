using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;
using IniaAi.Application.Errors;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ContractsWorkflowState = IniaAi.WebApi.Contracts.Enums.WorkflowState;
using DomainWorkflowState = IniaAi.Domain.Entities.WorkflowExecutions.WorkflowState;
using DiServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;

public class UpdateWorkflowExecutionStateCommandHandlerTests : IDisposable
{
    private const string ORG_ID = "org-1";
    private const string USER_ID = "user-1";

    private readonly IServiceProvider _serviceProvider;
    private readonly UserDataDbContext _context;
    private readonly RecordingJobsService _jobsService;
    private readonly RecordingAgentRunTerminalCleanup _terminalCleanup;
    private readonly RecordingRunSettlement _settlement = new();
    private readonly UpdateWorkflowExecutionStateCommandHandler _handler;

    public UpdateWorkflowExecutionStateCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _serviceProvider = services.BuildServiceProvider();

        _context = _serviceProvider.GetRequiredService<UserDataDbContext>();
        IUserDataUnitOfWork unitOfWork = _serviceProvider.GetRequiredService<IUserDataUnitOfWork>();

        _jobsService = new RecordingJobsService();
        _terminalCleanup = new RecordingAgentRunTerminalCleanup();
        _handler = new UpdateWorkflowExecutionStateCommandHandler(unitOfWork, _jobsService, _terminalCleanup, _settlement);
    }

    public void Dispose()
    {
        _context.Dispose();
        ((DiServiceProvider)_serviceProvider).Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<WorkflowExecution> SeedExecutionAsync(
        DomainWorkflowState state,
        string organizationId = ORG_ID,
        string? jobId = "job-1")
    {
        var execution = new WorkflowExecution
        {
            OrganizationId = organizationId,
            UserId = USER_ID,
            WorkflowState = state,
            JobId = jobId,
        };
        _ = _context.WorkflowExecutions.Add(execution);
        _ = await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return execution;
    }

    private UpdateWorkflowExecutionStateCommand CancelCommand(string executionId, string organizationId = ORG_ID)
    {
        return new UpdateWorkflowExecutionStateCommand(
            organizationId,
            USER_ID,
            executionId,
            ContractsWorkflowState.Cancelled);
    }

    [Theory]
    [InlineData(DomainWorkflowState.Running)]
    [InlineData(DomainWorkflowState.ProcessingDocuments)]
    [InlineData(DomainWorkflowState.Queued)]
    [InlineData(DomainWorkflowState.Undefined)]
    public async Task Handle_FromCancellableState_FlipsToCancelled_AndCancelsTheJob(
        DomainWorkflowState initialState)
    {
        WorkflowExecution execution = await SeedExecutionAsync(initialState, jobId: "job-9");

        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand(execution.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");

        WorkflowExecution? reloaded = await _context.WorkflowExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == execution.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(DomainWorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.NotNull(reloaded.CompletedAt);
        Assert.Equal(["job-9"], _jobsService.CancelledJobIds);
    }

    [Theory]
    [InlineData(DomainWorkflowState.Confirmed)]
    [InlineData(DomainWorkflowState.Failed)]
    [InlineData(DomainWorkflowState.Cancelled)]
    [InlineData(DomainWorkflowState.Draft)]
    public async Task Handle_FromNonCancellableState_ReturnsValidationError_AndLeavesRowUnchanged(
        DomainWorkflowState initialState)
    {
        WorkflowExecution execution = await SeedExecutionAsync(initialState, jobId: "job-7");

        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand(execution.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowExecutionErrorFactory.NOT_CANCELLABLE_CODE, result.FirstError.Code);

        WorkflowExecution? reloaded = await _context.WorkflowExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == execution.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(initialState, reloaded.WorkflowState);
        Assert.Null(reloaded.CompletedAt);
        Assert.Empty(_jobsService.CancelledJobIds);
    }

    [Fact]
    public async Task Handle_WonCancel_RunsAgentRunTerminalCleanup()
    {
        WorkflowExecution execution = await SeedExecutionAsync(DomainWorkflowState.Running);

        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand(execution.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        Assert.Equal([(execution.Id, ORG_ID, DomainWorkflowState.Cancelled)], _terminalCleanup.CleanedUp);
    }

    [Fact]
    public async Task Handle_WonCancel_SettlesTheRunOnce()
    {
        WorkflowExecution execution = await SeedExecutionAsync(DomainWorkflowState.Running);

        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand(execution.Id), TestContext.Current.CancellationToken);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        Assert.Equal([(execution.Id, ORG_ID)], _settlement.Settled);
    }

    [Fact]
    public async Task Handle_AlreadyTerminalCancel_NeverRunsCleanup()
    {
        WorkflowExecution terminal = await SeedExecutionAsync(DomainWorkflowState.Cancelled);

        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand(terminal.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Empty(_terminalCleanup.CleanedUp);
        Assert.Empty(_settlement.Settled);
    }

    [Fact]
    public async Task Handle_WithDifferentTenant_ReturnsNotFound()
    {
        WorkflowExecution execution = await SeedExecutionAsync(DomainWorkflowState.Running);

        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand(execution.Id, organizationId: "other-org"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Empty(_jobsService.CancelledJobIds);
    }

    [Fact]
    public async Task Handle_WithUnknownExecutionId_ReturnsNotFound()
    {
        ErrorOr<Success> result = await _handler.Handle(
            CancelCommand("does-not-exist"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Empty(_jobsService.CancelledJobIds);
    }
}
