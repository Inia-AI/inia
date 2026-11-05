using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.UpdateWorkflowExecutionState;
using IniaAi.Application.Errors;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using ContractsWorkflowState = IniaAi.WebApi.Contracts.Enums.WorkflowState;
using DomainWorkflowState = IniaAi.Domain.Entities.WorkflowExecutions.WorkflowState;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Integration tests for the cancel-workflow-execution flow. Validates the full
/// dispatch pipeline (FluentValidation + handler + IJobsService DI + real Postgres
/// persistence) end-to-end.
/// No Organization rows are seeded: nothing in the pipeline requires the org to exist,
/// and any org row created here would get a system user attached by SystemUserSeeder on
/// the next in-proc app boot, making the org impossible to hard-remove during cleanup.
/// </summary>
public class CancelWorkflowExecutionTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededExecutionIds = [];

    public CancelWorkflowExecutionTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _apiBuilder.CleanExecutionsByIdsAsync(_seededExecutionIds);
    }

    [Theory]
    [InlineData(DomainWorkflowState.Running)]
    [InlineData(DomainWorkflowState.ProcessingDocuments)]
    [InlineData(DomainWorkflowState.Queued)]
    [InlineData(DomainWorkflowState.Undefined)]
    public async Task UpdateState_FromCancellableState_FlipsToCancelled(DomainWorkflowState initialState)
    {
        string organizationId = $"CancelTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, initialState, jobId: $"job-{Guid.NewGuid():N}");

        var command = new UpdateWorkflowExecutionStateCommand(
            organizationId, "test-user-id", executionId, ContractsWorkflowState.Cancelled);

        ErrorOr<Success> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(DomainWorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public async Task UpdateState_WithNullJobId_StillFlipsToCancelled()
    {
        string organizationId = $"CancelTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, DomainWorkflowState.Running, jobId: null);

        var command = new UpdateWorkflowExecutionStateCommand(
            organizationId, "test-user-id", executionId, ContractsWorkflowState.Cancelled);

        ErrorOr<Success> result = await _apiBuilder.SendAsync(command);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(DomainWorkflowState.Cancelled, reloaded.WorkflowState);
        Assert.NotNull(reloaded.CompletedAt);
    }

    [Fact]
    public async Task UpdateState_FromConfirmed_ReturnsValidationError()
    {
        string organizationId = $"CancelTest-{Guid.NewGuid()}";
        string executionId = await SeedExecutionAsync(organizationId, DomainWorkflowState.Confirmed);

        var command = new UpdateWorkflowExecutionStateCommand(
            organizationId, "test-user-id", executionId, ContractsWorkflowState.Cancelled);

        ErrorOr<Success> result = await _apiBuilder.SendAsync(command);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Equal(WorkflowExecutionErrorFactory.NOT_CANCELLABLE_CODE, result.FirstError.Code);

        WorkflowExecution? reloaded = await ReloadAsync(executionId);
        Assert.NotNull(reloaded);
        Assert.Equal(DomainWorkflowState.Confirmed, reloaded.WorkflowState);
    }

    private async Task<string> SeedExecutionAsync(
        string organizationId,
        DomainWorkflowState state,
        string? jobId = null)
    {
        string id = Guid.NewGuid().ToString();
        _seededExecutionIds.Add(id);

        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            var execution = new WorkflowExecution
            {
                Id = id,
                Title = "Cancel Test Execution",
                OrganizationId = organizationId,
                UserId = "test-user-id",
                WorkflowState = state,
                JobId = jobId,
            };
            await uow.WorkflowExecutions.AddAsync(execution);

            _ = await uow.CompleteAsync();
        }

        return id;
    }

    private async Task<WorkflowExecution?> ReloadAsync(string id)
    {
        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            return await uow.WorkflowExecutions.SingleOrDefaultAsync(predicate: x => x.Id == id);
        }
    }
}
