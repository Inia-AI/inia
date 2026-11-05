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
/// Integration tests for the dequeue→terminal duration flow on
/// <see cref="WorkflowExecution"/>. Targets the
/// <c>WorkflowExecutionProjections.ToSummary</c> projection that runs against
/// real Postgres — the in-memory EF provider used in unit tests cannot
/// meaningfully validate the SQL translation of the
/// <c>CompletedAt - StartedAt</c> diff.
/// </summary>
public class WorkflowExecutionDurationTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string TEST_WORKFLOW_TYPE_ID = "DurationIntegrationTest";

    private readonly ApiTestFixture _fixture;
    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededExecutionIds = [];
    private readonly HashSet<string> _seededOrganizationIds = [];

    public WorkflowExecutionDurationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        // Clean up rows seeded by these tests so they don't bleed into other suites.
        await _apiBuilder.CleanExecutionsByIdsAsync(_seededExecutionIds);
        await _apiBuilder.CleanOrganizationsByIdsAsync(_seededOrganizationIds);
    }

    [Fact]
    public async Task ListWorkflowExecutions_PopulatesDuration_WhenExecutionIsTerminal()
    {
        string organizationId = $"DurationTest-{Guid.NewGuid()}";
        DateTime startedAt = DateTime.UtcNow.AddMinutes(-5);
        DateTime completedAt = startedAt.AddSeconds(150);
        await SeedExecutionAsync(organizationId, WorkflowState.Draft, startedAt, completedAt);

        var query = BuildListQuery(organizationId);
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.Equal(TimeSpan.FromSeconds(150), item.Duration);
    }

    [Fact]
    public async Task ListWorkflowExecutions_LeavesDurationNull_WhenExecutionStillQueued()
    {
        string organizationId = $"DurationTest-{Guid.NewGuid()}";
        await SeedExecutionAsync(organizationId, WorkflowState.Queued, startedAt: null, completedAt: null);

        var query = BuildListQuery(organizationId);
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.Null(item.Duration);
    }

    [Fact]
    public async Task ListWorkflowExecutions_LeavesDurationNull_WhenExecutionStartedButStillRunning()
    {
        string organizationId = $"DurationTest-{Guid.NewGuid()}";
        DateTime startedAt = DateTime.UtcNow.AddMinutes(-1);
        await SeedExecutionAsync(organizationId, WorkflowState.Running, startedAt, completedAt: null);

        var query = BuildListQuery(organizationId);
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.Null(item.Duration);
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
            // FK constraint: Workflows.OrganizationId → Organizations.Id, so the org
            // row needs to exist before we can seed a workflow under it.
            await EnsureOrganizationAsync(uow, organizationId);

            // The detail handler maps from Workflow but pulls timing fields from
            // the matching WorkflowExecution row. Both share an Id by convention.
            var workflow = new Workflow
            {
                Id = id,
                Title = "Duration Test Workflow",
                OrganizationId = organizationId,
            };
            await uow.Workflows.AddAsync(workflow);

            var execution = new WorkflowExecution
            {
                Id = id,
                WorkflowId = id,
                Title = "Duration Test Execution",
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

        var org = new Organization($"Duration Test {organizationId}", customId: organizationId);
        await uow.Repository<Organization>().AddAsync(org);
        _seededOrganizationIds.Add(organizationId);
    }
}
