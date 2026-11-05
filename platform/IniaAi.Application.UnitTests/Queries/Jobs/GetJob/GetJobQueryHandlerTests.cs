using ErrorOr;
using IniaAi.Application.Queries.Jobs.GetJob;
using IniaAi.Configuration;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.Application.UnitTests.Queries.Jobs.GetJob;

/// <summary>
/// A job of another organization answers exactly as a job that does not exist, so its id tells
/// the caller nothing. A job of another user in the caller's own organization is refused.
/// </summary>
public sealed class GetJobQueryHandlerTests : IDisposable
{
    private const string ORGANIZATION_ID = "org-own";
    private const string USER_ID = "user-own";

    private readonly ServiceProvider _provider;
    private readonly JobsService _jobs;
    private readonly GetJobQueryHandler _handler;

    public GetJobQueryHandlerTests()
    {
        ServiceCollection services = new();
        _ = services.AddTransient<IJobExecutor>(_ => new JobThatNeverRuns());
        _provider = services.BuildServiceProvider();
        PlatformSettings settings = new();
        settings.IniaAi.MaxConcurrentJobs = 1;
        _jobs = new JobsService(_provider, NullLogger<JobsService>.Instance, Options.Create(settings));
        _handler = new GetJobQueryHandler(NullLogger<GetJobQueryHandler>.Instance, _jobs);
    }

    public void Dispose()
    {
        _jobs.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public async Task TheCallersOwnJob_IsReturned()
    {
        Job job = Start(ORGANIZATION_ID, USER_ID);

        ErrorOr<JobDto> result = await _handler.Handle(new GetJobQuery(job.Id, ORGANIZATION_ID, USER_ID), TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task AJobOfAnotherOrganization_AnswersAsAJobThatDoesNotExist()
    {
        Job foreign = Start("org-other", USER_ID);

        ErrorOr<JobDto> foreignResult = await _handler.Handle(new GetJobQuery(foreign.Id, ORGANIZATION_ID, USER_ID), TestContext.Current.CancellationToken);
        ErrorOr<JobDto> unknownResult = await _handler.Handle(new GetJobQuery("no-such-job", ORGANIZATION_ID, USER_ID), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.NotFound, foreignResult.FirstError.Type);
        Assert.Equal(unknownResult.FirstError, foreignResult.FirstError);
    }

    [Fact]
    public async Task AJobOfAnotherUserInTheSameOrganization_IsRefused()
    {
        Job colleagues = Start(ORGANIZATION_ID, "user-colleague");

        ErrorOr<JobDto> result = await _handler.Handle(new GetJobQuery(colleagues.Id, ORGANIZATION_ID, USER_ID), TestContext.Current.CancellationToken);

        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
    }

    private Job Start(string organizationId, string userId)
    {
        Job job = _jobs.CreateUserJob(organizationId, userId, workflowTypeId: null);
        _jobs.Execute(job, shouldUseQueue: false);

        return job;
    }

    private sealed class JobThatNeverRuns : IJobExecutor
    {
        public void Execute(Job job, Action<Job> onFinish, CancellationToken cancellationToken)
        {
        }
    }
}
