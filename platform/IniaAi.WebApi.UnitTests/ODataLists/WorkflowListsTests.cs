using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Queries.Jobs.ListJobs;
using IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;
using IniaAi.Application.Queries.WorkflowOutputs.PaginatedResponseWorkflowOutputs;
using IniaAi.Configuration;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Services.Jobs;
using IniaAi.Services.Models.Jobs;
using IniaAi.Services.Models.Jobs.Enums;
using IniaAi.Testing.DependencyInjection;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.Contracts.Shared.Users;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApi.UnitTests.ODataLists;

public class WorkflowListsTests : ODataListTests
{
    private const string ORGANIZATION_ID = "org-workflows";
    private const string USER_ID = "user-workflows";

    private static readonly DateTime _created = new(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheWorkflowExecutionList_ReturnsTheSameJson()
    {
        await SeedWorkflowExecutionsAsync();

        AssertMatchesFixture(await ListWorkflowExecutionsAsync("?$orderby=title"), "WorkflowExecutions");
    }

    [Fact]
    public async Task TheWorkflowExecutionList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedWorkflowExecutionsAsync();

        ODataPaginatedResponse<WorkflowExecutionSummaryDto> page = await ListWorkflowExecutionsAsync(
            "?$filter=startswith(title, 'Replay') or counterpartyName eq 'Example Corp'&$orderby=createdAt desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["execution-1"], page.Value.Select(execution => execution.Id));
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task TheUserList_ReturnsTheSameJson()
    {
        await SeedUsersAsync();

        AssertMatchesFixture(await ListUsersAsync("?$orderby=email"), "Users");
    }

    [Fact]
    public async Task TheUserList_FiltersSortsPagesAndCountsInTheQuery()
    {
        await SeedUsersAsync();

        ODataPaginatedResponse<UserDto> page = await ListUsersAsync(
            "?$filter=lastName ne null&$orderby=username desc&$top=1&$skip=1&$count=true");

        Assert.Equal(["ada@example.com"], page.Value.Select(user => user.Email));
        Assert.Equal(2, page.Count);
    }

    [Fact]
    public async Task TheJobList_ReturnsEachJobTheUserStarted()
    {
        (Job failed, Job cancelled) = StartJobs();

        ODataPaginatedResponse<JobDto> page = await ListJobsAsync("?$orderby=progress");

        Assert.Equal([cancelled.Id, failed.Id], page.Value.Select(job => job.Id));
        foreach ((JobDto listed, Job source) in page.Value.Zip([cancelled, failed]))
        {
            Assert.Equal(source.State.ToString(), listed.State);
            Assert.Equal((int)source.Type, (int)listed.Type);
            Assert.Equal(source.Error, listed.Error);
            Assert.Equal(source.CreatedAt, listed.CreatedAt);
            Assert.Equal(source.CompletedAt, listed.CompletedAt);
            Assert.Equal(source.Progress, listed.Progress);
            Assert.Equal(source.Description, listed.Description);
            Assert.Equal(USER_ID, listed.UserId);
        }
    }

    [Fact]
    public async Task TheJobList_FiltersSortsPagesAndCountsInTheQuery()
    {
        (_, Job cancelled) = StartJobs();

        ODataPaginatedResponse<JobDto> page = await ListJobsAsync(
            "?$filter=progress lt 100&$orderby=state desc&$top=1&$skip=1&$count=true");

        Assert.Equal([cancelled.Id], page.Value.Select(job => job.Id));
        Assert.Equal(2, page.Count);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        _ = services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new PlatformSettings()));
        _ = services.AddSingleton<IJobsService, JobsService>();
    }

    private async Task<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> ListWorkflowExecutionsAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result = await Handler<ListWorkflowExecutionsQueryHandler>().Handle(
            new ListWorkflowExecutionsQuery(ORGANIZATION_ID, USER_ID, Options<WorkflowExecutionSummaryDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task<ODataPaginatedResponse<UserDto>> ListUsersAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<UserDto>> result = await Handler<ListUsersQueryHandler>().Handle(
            new ListUsersQuery(ORGANIZATION_ID, USER_ID, Options<UserDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task<ODataPaginatedResponse<JobDto>> ListJobsAsync(string query)
    {
        ErrorOr<ODataPaginatedResponse<JobDto>> result = await Handler<PaginatedResponseJobsQueryHandler>().Handle(
            new PaginatedResponseJobsQuery(ORGANIZATION_ID, USER_ID, Options<JobDto>(query)),
            TestContext.Current.CancellationToken);
        return result.Value;
    }

    private async Task SeedWorkflowExecutionsAsync()
    {
        var counterparty = new Counterparty(ORGANIZATION_ID, "Example Corp") { Id = "counterparty-1", CreatedAt = _created, UpdatedAt = _created };

        WorkflowExecution draft = BuildExecution("execution-1", "Annual review", WorkflowState.Draft, 0);
        draft.CounterpartyId = counterparty.Id;
        draft.StartedAt = _created.AddMinutes(1);
        draft.CompletedAt = _created.AddMinutes(4);

        WorkflowExecution failed = BuildExecution("execution-2", "Quarterly review", WorkflowState.Failed, 1);
        failed.FailureKind = "ModelError";
        failed.FailureDetail = "The model refused the prompt.";
        failed.WarningKind = "SlowDocument";
        failed.WarningDetail = "One page took a minute.";
        failed.StartedAt = _created.AddMinutes(2);

        WorkflowExecution replay = BuildExecution("execution-3", "Replay review", WorkflowState.Draft, 2);
        replay.FreezeSourceExecutionId = "execution-1";
        replay.FreezeReplayedCount = 3;
        replay.FreezeEligibleCount = 5;

        WorkflowExecution running = BuildExecution("execution-4", "Running review", WorkflowState.Running, 3);

        _ = UserData.Counterparties.Add(counterparty);
        UserData.WorkflowExecutions.AddRange(draft, failed, replay, running);
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedUsersAsync()
    {
        UserData.Users.AddRange(
            BuildUser("user-ada", "ada@example.com", "Ada", "Lovelace", null, isSystem: false),
            BuildUser("user-grace", "grace@example.com", "Grace", "Hopper", "Brewster", isSystem: false),
            BuildUser("user-nameless", "nameless@example.com", null, null, null, isSystem: false),
            BuildUser("user-system", "system@example.com", "System", "User", null, isSystem: true));
        _ = await UserData.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private (Job Failed, Job Cancelled) StartJobs()
    {
        var jobsService = (JobsService)Service<IJobsService>();
        _ = jobsService.CancelAllQueuedJobs();

        var failed = new Job(ORGANIZATION_ID, "Corp");
        failed.SetUserId(USER_ID);
        var cancelled = new Job(ORGANIZATION_ID, "Corp");
        cancelled.SetUserId(USER_ID);
        var someoneElses = new Job(ORGANIZATION_ID, "Corp");
        someoneElses.SetUserId("user-other");

        foreach (Job job in new[] { failed, cancelled, someoneElses })
        {
            jobsService.Execute(job, shouldUseQueue: true);
        }

        failed.UpdateProgress(42);
        failed.SetState(JobState.Error, "The model refused the prompt.");
        return (failed, cancelled);
    }

    private static WorkflowExecution BuildExecution(string id, string title, WorkflowState state, int order)
    {
        return new WorkflowExecution
        {
            Id = id,
            WorkflowId = $"workflow-{id}",
            WorkflowTypeId = "Corp",
            Title = title,
            UserId = USER_ID,
            OrganizationId = ORGANIZATION_ID,
            ReviewDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            AnalystName = "Ada Lovelace",
            JobId = $"job-{id}",
            IsBaseline = false,
            WorkflowState = state,
            CreatedAt = _created.AddMinutes(order),
            UpdatedAt = _created.AddMinutes(order),
        };
    }

    private static User BuildUser(string id, string email, string? firstName, string? lastName, string? middleName, bool isSystem)
    {
        return new User(ORGANIZATION_ID)
        {
            Id = id,
            Email = email,
            UserName = email,
            FirstName = firstName,
            LastName = lastName,
            MiddleName = middleName,
            IsSystem = isSystem,
        };
    }
}
