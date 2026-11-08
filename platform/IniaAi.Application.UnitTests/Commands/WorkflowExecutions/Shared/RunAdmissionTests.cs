using ErrorOr;
using IniaAi.Application.Commands.WorkflowExecutions.Shared;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Application.Workflows.Services;
using IniaAi.Common.Interfaces.Services.Runs;
using IniaAi.Core.Services;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Executions;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Serialization;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.WorkflowExecutions.Shared;

/// <summary>
/// A submission inserts its run and asks the admission inside one transaction: a refusal rolls the
/// run back and queues nothing, and a write the admission makes commits or rolls back with the run.
/// While a deploy has paused run intake, a submission is refused as unavailable for now, with the
/// update message and a time to try again, and leaves nothing either; a retry of a submission made
/// before the pause still gets its run, and intake takes runs again once the pause lifts. SQLite,
/// because the rule is about what a real transaction leaves behind.
/// </summary>
public sealed class RunAdmissionTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-admission";
    private const string USER_ID = "user-admission";
    private const string WORKFLOW_TYPE_ID = "type-admission";
    private const string REFUSAL_CODE = "Admission.Refused";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly RecordingJobsService _jobs = new();
    private readonly HostBootIdentity _hostBoot = new();
    private readonly SettableClock _clock = new(new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero));
    private readonly RunIntake _intake;

    public RunAdmissionTests()
    {
        _intake = new RunIntake(_clock);
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        CoreDbContext core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        _ = core.Set<WorkflowTypeOrganizationAccess>().Add(new WorkflowTypeOrganizationAccess
        {
            WorkflowTypeId = WORKFLOW_TYPE_ID,
            OrganizationId = ORGANIZATION_ID,
        });
        _ = core.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Fact]
    public async Task ARefusedSubmission_LeavesNoRunAndQueuesNothing()
    {
        using IServiceScope scope = _provider.CreateScope();
        var admission = new TestAdmission(scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>(), refuses: true);

        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(scope, admission);

        Assert.True(submitted.IsError);
        Assert.Equal(REFUSAL_CODE, submitted.FirstError.Code);
        Assert.NotNull(admission.SeenExecutionId);
        Assert.Empty(_jobs.ExecutedJobs);
        Assert.Empty(await StoredAsync<WorkflowExecution>());
        Assert.Empty(await StoredAsync<WorkflowReport>());
        Assert.Empty(await StoredAsync<HostBoot>());
    }

    [Fact]
    public async Task ARefusedSubmission_KeepsNothingItAddedTrackedForTheCallersNextSave()
    {
        using IServiceScope scope = _provider.CreateScope();
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();

        _ = await SubmitAsync(scope, new TestAdmission(unitOfWork, refuses: true));
        _ = await unitOfWork.CompleteAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await StoredAsync<WorkflowExecution>());
        Assert.Empty(await StoredAsync<HostBoot>());
    }

    [Fact]
    public async Task AnAdmittedSubmission_CommitsTheRunWithTheAdmissionsWriteAndItsHostThenQueuesIt()
    {
        using IServiceScope scope = _provider.CreateScope();
        var admission = new TestAdmission(scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>(), refuses: false);

        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(scope, admission);

        Assert.False(submitted.IsError, submitted.IsError ? submitted.FirstError.Description : string.Empty);
        Assert.Equal(submitted.Value.Id, admission.SeenExecutionId);
        Job queued = Assert.Single(_jobs.ExecutedJobs);
        Assert.Equal(submitted.Value.Id, queued.WorkflowExecutionId);
        WorkflowExecution stored = Assert.Single(await StoredAsync<WorkflowExecution>());
        Assert.Equal(WorkflowState.Queued, stored.WorkflowState);
        Assert.Equal(_hostBoot.Id, stored.HostBootId);
        _ = Assert.Single(await StoredAsync<HostBoot>());
    }

    [Fact]
    public async Task WhileIntakeIsPaused_ASubmissionIsRefusedAsUnavailable_AndLeavesNoRun()
    {
        _intake.Pause(TimeSpan.FromMinutes(30));
        using IServiceScope scope = _provider.CreateScope();

        ErrorOr<WorkflowExecutionDto> submitted = await SubmitAsync(scope, new AdmitEveryRun());

        Assert.Equal(
            (ServiceUnavailableError.ERROR_TYPE, RunIntake.PAUSED_CODE, RunIntake.PAUSED_MESSAGE),
            ((int)submitted.FirstError.Type, submitted.FirstError.Code, submitted.FirstError.Description));
        Assert.Equal(RunIntake.RetryAfter, ServiceUnavailableError.RetryAfterOf(submitted.FirstError));
        Assert.Empty(_jobs.ExecutedJobs);
        Assert.Empty(await StoredAsync<WorkflowExecution>());
    }

    [Fact]
    public async Task ARetryOfASubmissionMadeBeforeThePause_GetsItsRun()
    {
        using IServiceScope scope = _provider.CreateScope();
        ErrorOr<WorkflowExecutionDto> first = await SubmitAsync(scope, new AdmitEveryRun(), idempotencyKey: "retry-me");
        _intake.Pause(TimeSpan.FromMinutes(30));

        ErrorOr<WorkflowExecutionDto> retried = await SubmitAsync(scope, new AdmitEveryRun(), idempotencyKey: "retry-me");

        Assert.False(retried.IsError, retried.IsError ? retried.FirstError.Description : string.Empty);
        Assert.Equal(first.Value.Id, retried.Value.Id);
        _ = Assert.Single(_jobs.ExecutedJobs);
    }

    [Fact]
    public async Task APauseLiftsByItself_AndOpeningItEarlyTakesRunsAtOnce()
    {
        _intake.Pause(TimeSpan.FromMinutes(30));
        _clock.Advance(TimeSpan.FromMinutes(31));
        using IServiceScope scope = _provider.CreateScope();
        ErrorOr<WorkflowExecutionDto> afterTheLimit = await SubmitAsync(scope, new AdmitEveryRun());

        _intake.Pause(TimeSpan.FromMinutes(30));
        _intake.Open();
        ErrorOr<WorkflowExecutionDto> afterOpening = await SubmitAsync(scope, new AdmitEveryRun());

        Assert.False(afterTheLimit.IsError, afterTheLimit.IsError ? afterTheLimit.FirstError.Description : string.Empty);
        Assert.False(afterOpening.IsError, afterOpening.IsError ? afterOpening.FirstError.Description : string.Empty);
        Assert.Equal(2, _jobs.ExecutedJobs.Count);
    }

    private Task<ErrorOr<WorkflowExecutionDto>> SubmitAsync(IServiceScope scope, IRunAdmission admission, string? idempotencyKey = null)
    {
        IUserDataUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>();
        var orchestrator = new WorkflowExecutionOrchestrator(
            NullLogger<WorkflowExecutionOrchestrator>.Instance,
            _jobs,
            unitOfWork,
            scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>(),
            new WorkflowCanonicalizer(),
            new WorkflowVariableValidationService(),
            new DocumentClassValidationService(NullLogger<DocumentClassValidationService>.Instance),
            new WorkflowOutputService(
                new WorkflowOutputBuilder(),
                new WorkflowOutputValidator(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WorkflowOutputService>.Instance,
                hostBoot: _hostBoot),
            new ExecutionFreezeSourceResolver(unitOfWork, NullLogger<ExecutionFreezeSourceResolver>.Instance),
            new NoCaseFreezeSources(),
            runAdmission: admission,
            runIntake: _intake);

        Workflow workflow = new() { WorkflowTypeId = WORKFLOW_TYPE_ID, Title = "Admission test" };

        return orchestrator.ExecuteWorkflowAsync(
            workflow,
            files: null,
            variables: null,
            ORGANIZATION_ID,
            USER_ID,
            username: "analyst",
            email: null,
            counterpartyId: null,
            peerCompanyIds: null,
            idempotencyKey: idempotencyKey,
            ct: TestContext.Current.CancellationToken);
    }

    private async Task<List<T>> StoredAsync<T>() where T : class
    {
        using IServiceScope scope = _provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .Set<T>()
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Writes a row through the submission's own unit of work, as a credit hold would. A refusing one
    /// then adds a row it never saves, and refuses.
    /// </summary>
    private sealed class TestAdmission(IUserDataUnitOfWork unitOfWork, bool refuses) : IRunAdmission
    {
        public string? SeenExecutionId { get; private set; }

        public async Task<ErrorOr<Success>> AdmitAsync(RunAdmissionRequest request, CancellationToken cancellationToken)
        {
            SeenExecutionId = request.ExecutionId;
            await unitOfWork.Repository<HostBoot>().AddAsync(new HostBoot { Application = "admission", Machine = request.ExecutionId });
            _ = await unitOfWork.CompleteAsync(cancellationToken);
            if (!refuses)
            {
                return Result.Success;
            }

            await unitOfWork.Repository<HostBoot>().AddAsync(new HostBoot { Application = "unsaved", Machine = request.ExecutionId });

            return Error.Validation(REFUSAL_CODE, "You have no credits left.");
        }
    }
}
