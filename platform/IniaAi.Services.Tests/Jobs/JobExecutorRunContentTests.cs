using IniaAi.Core.Documents;
using IniaAi.Core.Workflows.Outputs;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Testing.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Services.Tests.Jobs;

/// <summary>A run its content check refused, against a real database: the execution fails with the refusal's kind and detail.</summary>
public sealed class JobExecutorRunContentTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-run-content";
    private const string WORKFLOW_TYPE_ID = "RunContentType";
    private const string DETAIL = "The report stopped because its documents hold more text than this report reads. Start it again with fewer or shorter documents.";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly JobExecutor _executor;

    public JobExecutorRunContentTests()
    {
        var services = new ServiceCollection();
        _ = services.AddTestDatabaseServices();
        _provider = services.BuildServiceProvider();

        _executor = new JobExecutor(
            NullLogger<JobExecutor>.Instance,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            documentProcessing: null!,
            workflowOutputService: new WorkflowOutputService(
                outputBuilder: null!,
                workflowOutputValidator: null!,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<WorkflowOutputService>.Instance),
            emailIntakeJobStage: null!,
            runInputStage: new PassThroughRunInputStage(),
            runContentCheck: new PassRunContentCheck());
    }

    [Fact]
    public async Task ARefusedRun_FailsTheExecutionWithTheRefusalsKindAndDetail()
    {
        var job = new Job(ORGANIZATION_ID, WORKFLOW_TYPE_ID) { Workflow = new Workflow { WorkflowTypeId = WORKFLOW_TYPE_ID } };
        job.WorkflowExecutionId = "execution-refused";
        UserDataDbContext context = _provider.GetRequiredService<UserDataDbContext>();
        _ = await context.WorkflowExecutions.AddAsync(
            new WorkflowExecution { Id = job.WorkflowExecutionId, OrganizationId = ORGANIZATION_ID, WorkflowState = WorkflowState.ProcessingDocuments },
            TestContext.Current.CancellationToken);
        _ = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _executor.HandleJobError(job, new RunContentRefusedException(new RunContentRefusal(WorkflowExecutionFailureKinds.NO_READABLE_DOCUMENTS, DETAIL)));

        using IServiceScope scope = _provider.CreateScope();
        WorkflowExecution execution = await scope.ServiceProvider.GetRequiredService<UserDataDbContext>()
            .WorkflowExecutions.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkflowState.Failed, execution.WorkflowState);
        Assert.Equal(WorkflowExecutionFailureKinds.NO_READABLE_DOCUMENTS, execution.FailureKind);
        Assert.Equal(DETAIL, execution.FailureDetail);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
