using ErrorOr;
using IniaAi.Application.Queries.WorkflowExecutions.ListWorkflowExecutions;
using IniaAi.Domain.Entities.Counterparties;
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
/// Integration tests for the <c>CounterpartyName</c> projection on the
/// workflow-reports list endpoint. Targets the
/// <c>WorkflowExecutionProjections.ToSummary</c> projection that runs against
/// real Postgres — the in-memory EF provider used in unit tests cannot
/// meaningfully validate the SQL translation of the
/// <c>src.Counterparty != null ? src.Counterparty.Name : null</c> projection.
/// </summary>
public class WorkflowExecutionCounterpartyNameTests : IClassFixture<ApiTestFixture>, IAsyncLifetime
{
    private const string TEST_WORKFLOW_TYPE_ID = "CounterpartyNameIntegrationTest";

    private readonly ClientBuilder _apiBuilder;
    private readonly List<string> _seededExecutionIds = [];
    private readonly List<string> _seededCounterpartyIds = [];
    private readonly HashSet<string> _seededOrganizationIds = [];

    public WorkflowExecutionCounterpartyNameTests(ApiTestFixture fixture)
    {
        _apiBuilder = fixture.ClientBuilder;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _apiBuilder.CleanExecutionsByIdsAsync(_seededExecutionIds);

        // Other suites borrow whatever counterparty exists in the shared DB for their
        // own executions, so the delete must detach foreign references first.
        await _apiBuilder.CleanCounterpartiesByIdsAsync(_seededCounterpartyIds);
        await _apiBuilder.CleanOrganizationsByIdsAsync(_seededOrganizationIds);
    }

    [Fact]
    public async Task ListWorkflowExecutions_PopulatesCounterpartyName_WhenCounterpartySet()
    {
        string organizationId = $"CounterpartyNameTest-{Guid.NewGuid()}";
        const string counterpartyName = "Acme Holdings";
        string counterpartyId = await SeedCounterpartyAsync(organizationId, counterpartyName);
        await SeedExecutionAsync(organizationId, counterpartyId);

        var query = BuildListQuery(organizationId);
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.Equal(counterpartyId, item.CounterpartyId);
        Assert.Equal(counterpartyName, item.CounterpartyName);
    }

    [Fact]
    public async Task ListWorkflowExecutions_LeavesCounterpartyNameNull_WhenCounterpartyIdMissing()
    {
        string organizationId = $"CounterpartyNameTest-{Guid.NewGuid()}";
        await SeedExecutionAsync(organizationId, counterpartyId: null);

        var query = BuildListQuery(organizationId);
        ErrorOr<ODataPaginatedResponse<WorkflowExecutionSummaryDto>> result =
            await _apiBuilder.SendAsync(query);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : "");
        WorkflowExecutionSummaryDto item = Assert.Single(result.Value.Value);
        Assert.Null(item.CounterpartyId);
        Assert.Null(item.CounterpartyName);
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

    private async Task<string> SeedCounterpartyAsync(string organizationId, string name)
    {
        (IServiceScope? scope, IUserDataUnitOfWork? uow) = _apiBuilder.GetNewScope<IUserDataUnitOfWork>();
        using (scope)
        {
            await EnsureOrganizationAsync(uow, organizationId);

            var counterparty = new Counterparty(organizationId, name);
            await uow.Repository<Counterparty>().AddAsync(counterparty);
            _ = await uow.CompleteAsync();
            _seededCounterpartyIds.Add(counterparty.Id);
            return counterparty.Id;
        }
    }

    private async Task<string> SeedExecutionAsync(string organizationId, string? counterpartyId)
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
                Title = "CounterpartyName Test Workflow",
                OrganizationId = organizationId,
            };
            await uow.Workflows.AddAsync(workflow);

            var execution = new WorkflowExecution
            {
                Id = id,
                WorkflowId = id,
                Title = "CounterpartyName Test Execution",
                OrganizationId = organizationId,
                UserId = "test-user-id",
                WorkflowTypeId = TEST_WORKFLOW_TYPE_ID,
                WorkflowState = WorkflowState.Draft,
                CounterpartyId = counterpartyId,
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

        var org = new Organization($"CounterpartyName Test {organizationId}", customId: organizationId);
        await uow.Repository<Organization>().AddAsync(org);
        _seededOrganizationIds.Add(organizationId);
    }
}
