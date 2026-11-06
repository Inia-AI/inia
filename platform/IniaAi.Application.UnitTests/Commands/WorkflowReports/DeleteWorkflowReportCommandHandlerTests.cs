using ErrorOr;
using IniaAi.Application.Commands.WorkflowReports.DeleteWorkflowReport;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IniaAi.Application.UnitTests.Commands.WorkflowReports;

/// <summary>
/// Deleting a report that has not finished stops its job, whatever stage it is in: a deleted run's
/// state writes are skipped, so a job left going would only spend model calls for nothing.
/// </summary>
public sealed class DeleteWorkflowReportCommandHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-delete";

    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _provider;
    private readonly RecordingJobsService _jobs = new();

    public DeleteWorkflowReportCommandHandlerTests()
    {
        var services = new ServiceCollection();
        _ = services.AddSqliteTestDatabaseServices();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    [Theory]
    [InlineData(WorkflowState.Queued)]
    [InlineData(WorkflowState.ProcessingDocuments)]
    [InlineData(WorkflowState.Running)]
    [InlineData(WorkflowState.Undefined)]
    public async Task DeletingAReportThatHasNotFinished_CancelsItsJob(WorkflowState state)
    {
        string run = await SeedRunAsync(state);

        ErrorOr<Success> deleted = await DeleteAsync(run);

        Assert.False(deleted.IsError);
        Assert.Equal(["job-" + run], _jobs.CancelledJobIds);
    }

    [Theory]
    [InlineData(WorkflowState.Draft)]
    [InlineData(WorkflowState.Failed)]
    public async Task DeletingAFinishedReport_CancelsNothing(WorkflowState state)
    {
        string run = await SeedRunAsync(state);

        _ = await DeleteAsync(run);

        Assert.Empty(_jobs.CancelledJobIds);
    }

    private async Task<ErrorOr<Success>> DeleteAsync(string run)
    {
        using IServiceScope scope = _provider.CreateScope();
        var handler = new DeleteWorkflowReportCommandHandler(
            NullLogger<DeleteWorkflowReportCommandHandler>.Instance,
            scope.ServiceProvider.GetRequiredService<IUserDataUnitOfWork>(),
            _jobs);
        return await handler.Handle(new DeleteWorkflowReportCommand(ORGANIZATION_ID, "analyst", run), TestContext.Current.CancellationToken);
    }

    private async Task<string> SeedRunAsync(WorkflowState state)
    {
        using IServiceScope scope = _provider.CreateScope();
        UserDataDbContext db = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();
        string id = Guid.NewGuid().ToString();
        _ = db.WorkflowExecutions.Add(new WorkflowExecution { Id = id, OrganizationId = ORGANIZATION_ID, JobId = "job-" + id, WorkflowState = state, Title = "Report" });
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }
}
