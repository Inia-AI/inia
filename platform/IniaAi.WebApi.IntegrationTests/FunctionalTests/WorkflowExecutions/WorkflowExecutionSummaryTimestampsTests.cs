using ErrorOr;
using IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.WorkflowOutput;
using IniaAi.WebApi.IntegrationTests.Builders;
using IniaAi.WebApi.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OData.Edm;

namespace IniaAi.WebApi.IntegrationTests.FunctionalTests.WorkflowExecutions;

/// <summary>
/// Integration tests for the <c>StartedAt</c> and <c>CompletedAt</c> projections
/// on the workflow-reports list endpoint. The projection copies these from the
/// same-named entity columns; this exercises the
/// <c>WorkflowExecutionProjections.ToSummary</c> projection against real Postgres
/// so the SQL translation can't drift silently.
/// </summary>
public class WorkflowExecutionSummaryTimestampsTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string TEST_WORKFLOW_TYPE_ID = "TimestampsIntegrationTest";

    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededExecutionIds = [];
    private readonly HashSet<string> _seededOrganizationIds = [];

    public WorkflowExecutionSummaryTimestampsTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _apiBuilder.CleanExecutionsByIdsAsync(_seededExecutionIds);
        await _apiBuilder.CleanOrganizationsByIdsAsync(_seededOrganizationIds);
    }

    [Fact]
    public async Task ListWorkflowExecutions_PopulatesStartedAtAndCompletedAt_WhenTerminal()
    {
        string organizationId = $"TimestampsTest-{Guid.NewGuid()}";
        DateTime startedAt = DateTime.UtcNow.AddMinutes(-5);
        DateTime completedAt = startedAt.AddSeconds(150);
        await SeedExecutionAsync(organizationId, WorkflowState.Draft, startedAt, completedAt);

        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(BuildListQuery(organizationId));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.NotNull(item.StartedAt);
        Assert.NotNull(item.CompletedAt);
        // Postgres timestamp(6) round-trips with microsecond precision; tolerate
        // a millisecond of slop rather than asserting strict equality on ticks.
        Assert.Equal(startedAt, item.StartedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(completedAt, item.CompletedAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ListWorkflowExecutions_LeavesBothNull_WhenStillQueued()
    {
        string organizationId = $"TimestampsTest-{Guid.NewGuid()}";
        await SeedExecutionAsync(organizationId, WorkflowState.Queued, startedAt: null, completedAt: null);

        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(BuildListQuery(organizationId));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.Null(item.StartedAt);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public async Task ListWorkflowExecutions_PopulatesStartedAtOnly_WhenStartedButStillRunning()
    {
        string organizationId = $"TimestampsTest-{Guid.NewGuid()}";
        DateTime startedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedExecutionAsync(organizationId, WorkflowState.Running, startedAt, completedAt: null);

        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(BuildListQuery(organizationId));

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.NotNull(item.StartedAt);
        Assert.Equal(startedAt, item.StartedAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Null(item.CompletedAt);
    }

    private static ListWorkflowExecutionsQuery BuildListQuery(string organizationId)
    {
        IEdmModel edmModel = ODataTestHelper.BuildEdmModelForWorkflowExecutionSummary();
        ODataQueryOptions<WorkflowExecutionSummaryDto> options =
            ODataTestHelper.BuildFakeODataOptions<WorkflowExecutionSummaryDto>(
                edmModel,
                "?$count=true&$orderby=createdAt desc");
        return new ListWorkflowExecutionsQuery(organizationId, "test-user-id", options);
    }

    private async Task<string> SeedExecutionAsync(
        string organizationId,
        WorkflowState state,
        DateTime? startedAt,
        DateTime? completedAt)
    {
        string id = Guid.NewGuid().ToString();
        _seededExecutionIds.Add(id);

        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            await EnsureOrganizationAsync(uow, organizationId);

            var workflow = new Workflow
            {
                Id = id,
                Title = "Timestamps Test Workflow",
                OrganizationId = organizationId,
            };
            await uow.Workflows.AddAsync(workflow);

            var execution = new WorkflowExecution
            {
                Id = id,
                WorkflowId = id,
                Title = "Timestamps Test Execution",
                OrganizationId = organizationId,
                UserId = "test-user-id",
                WorkflowTypeId = TEST_WORKFLOW_TYPE_ID,
                WorkflowState = state,
                StartedAt = startedAt,
                CompletedAt = completedAt,
            };
            await uow.WorkflowExecutions.AddAsync(execution);

            _ = await uow.CompleteAsync();
        }

        return id;
    }

    private async Task EnsureOrganizationAsync(IUserDataUnitOfWork uow, string organizationId)
    {
        Organization? existing = await uow.Repository<Organization>()
            .SingleOrDefaultAsync(predicate: x => x.Id == organizationId);
        if (existing is not null)
        {
            return;
        }

        var org = new Organization($"Timestamps Test {organizationId}", customId: organizationId);
        await uow.Repository<Organization>().AddAsync(org);
        _seededOrganizationIds.Add(organizationId);
    }
}
